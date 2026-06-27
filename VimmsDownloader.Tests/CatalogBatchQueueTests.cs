using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Download.Sources;

namespace VimmsDownloader.Tests;

/// <summary>
/// Covers the batch-queue loop (<see cref="CatalogQueueOps.ResolveAndQueueBatchAsync"/>, behind
/// <c>POST /api/catalog/games/queue</c>): the per-id classification (queued / duplicate / unavailable /
/// unknown), the queued/skipped/failed tallies, and that two distinct ids resolving to the same URL are
/// not double-enqueued. No archive sets are configured, so each bound game resolves to its Vimm vault
/// URL — the same fallback path VimmFallbackTests exercises.
/// </summary>
[TestClass]
public class CatalogBatchQueueTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmbatch-{Guid.NewGuid():N}");
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        // InitAsync creates the data dir and runs every migration (queue + catalog tables share queue.db).
        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance);
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // Resolve with no sources/sets configured: archive resolution short-circuits to null, so the HTTP
    // factory is never used and each bound game falls back to its Vimm vault URL.
    private CatalogResolveService NewResolver() =>
        new(_catalog, new SourceRegistry([]), new ThrowingHttpClientFactory(),
            NullLogger<CatalogResolveService>.Instance);

    private Task<CatalogQueueBatchResponse> QueueBatch(IReadOnlyList<int> ids) =>
        CatalogQueueOps.ResolveAndQueueBatchAsync(ids, null, _catalog, NewResolver(), _queue, default);

    [TestMethod]
    public async Task Batch_ClassifiesEachId_AndTallies()
    {
        var ps3 = await Seed("ps3");
        var resolvable = (int)await AddGame(ps3, "Resolvable");
        await Bind(resolvable, 100);
        var dup = (int)await AddGame(ps3, "Already Queued");
        await Bind(dup, 200);
        await _queue.AddToQueueAsync("https://vimm.net/vault/200", 0, "vimm"); // pre-queue → duplicate
        var unavailable = (int)await AddGame(ps3, "No Source"); // no Vimm binding, no archive set
        const int unknown = 987654;

        var res = await QueueBatch([resolvable, dup, unavailable, unknown]);

        Assert.AreEqual(1, res.Queued);
        Assert.AreEqual(1, res.Skipped);
        Assert.AreEqual(2, res.Failed);
        Assert.AreEqual("queued", res.Results.Single(r => r.Id == resolvable).Status);
        Assert.AreEqual("vimm", res.Results.Single(r => r.Id == resolvable).Source);
        Assert.AreEqual("duplicate", res.Results.Single(r => r.Id == dup).Status);
        Assert.AreEqual("unavailable", res.Results.Single(r => r.Id == unavailable).Status);
        Assert.AreEqual("unknown", res.Results.Single(r => r.Id == unknown).Status);
    }

    [TestMethod]
    public async Task Batch_TwoIdsSameUrl_SecondIsDuplicate_NoDoubleEnqueue()
    {
        var ps3 = await Seed("ps3");
        var a = (int)await AddGame(ps3, "Disc One");
        var b = (int)await AddGame(ps3, "Disc Two");
        await Bind(a, 300);
        await Bind(b, 300); // same vault → identical resolved URL

        var res = await QueueBatch([a, b]);

        Assert.AreEqual(1, res.Queued);
        Assert.AreEqual(1, res.Skipped);
        Assert.AreEqual("queued", res.Results.Single(r => r.Id == a).Status);
        Assert.AreEqual("duplicate", res.Results.Single(r => r.Id == b).Status);
        // The shared URL was enqueued exactly once.
        Assert.AreEqual(1L, await ScalarLong(
            "SELECT COUNT(*) FROM queued_urls WHERE url = $u", ("$u", "https://vimm.net/vault/300")));
    }

    // --- helpers ---

    private async Task Bind(int gameId, long vaultId) =>
        await _catalog.BindVimmAsync(gameId, vaultId, "sha1", [new(0, "JB Folder", 100, "100 B")], default);

    private async Task<long> Seed(string console) =>
        await ScalarLong("INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT ' || $c, $c, 'redump') RETURNING id", ("$c", console));

    private async Task<long> AddGame(long systemId, string name) =>
        await ScalarLong("INSERT INTO catalog_game (system_id, name) VALUES ($sid, $name) RETURNING id", ("$sid", systemId), ("$name", name));

    private async Task<long> ScalarLong(string sql, params (string Name, object Value)[] ps)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("HTTP should not be called when no archive sets exist");
    }
}
