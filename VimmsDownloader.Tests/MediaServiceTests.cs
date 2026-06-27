using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL <see cref="MediaService"/> against a temp SQLite file (real migrations) and a
/// stub libretro-thumbnails endpoint: cache-on-success, negative-cache-on-404, the
/// truncated-name fallback, and that transient (non-404) errors are NOT negative-cached. No real network.
/// </summary>
[TestClass]
public class MediaServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private string _dir = null!;
    private string _connStr = null!;
    private string _mediaRoot = null!;
    private CatalogRepository _repo = null!;
    private long _gameId;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _mediaRoot = Path.Combine(_dir, "data", "media");
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);

        var system = await ScalarLong(
            "INSERT INTO catalog_system (dat_name, console, source) " +
            "VALUES ('Nintendo - Nintendo Entertainment System', 'nes', 'no-intro') RETURNING id");
        _gameId = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name) VALUES ({system}, 'Mega Man 2 (USA)') RETURNING id");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Success_CachesOnDisk_AndServesFromCacheWithoutRefetch()
    {
        var handler = new RoutingHandler(_ => (HttpStatusCode.OK, Png));
        var media = NewService(handler);

        var path = await media.GetImageAsync((int)_gameId, "boxart", default);

        Assert.IsNotNull(path);
        Assert.IsTrue(File.Exists(path));
        CollectionAssert.AreEqual(Png, await File.ReadAllBytesAsync(path));
        Assert.AreEqual(1, handler.Calls);                  // one fetch (exact name hit)
        Assert.AreEqual(("ok", path), await _repo.GetMediaAsync((int)_gameId, "boxart"));
        StringAssert.Contains(path, Path.Combine("libretro", "nes", "boxart")); // console-grouped layout

        // Second request is served from the cache — no further network.
        var again = await media.GetImageAsync((int)_gameId, "boxart", default);
        Assert.AreEqual(path, again);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task AllCandidates404_NegativeCaches_AndDoesNotRefetch()
    {
        var handler = new RoutingHandler(_ => (HttpStatusCode.NotFound, null));
        var media = NewService(handler);

        var path = await media.GetImageAsync((int)_gameId, "boxart", default);

        Assert.IsNull(path);
        Assert.AreEqual(2, handler.Calls);                  // exact + truncated, both 404
        Assert.AreEqual(("missing", (string?)null), await _repo.GetMediaAsync((int)_gameId, "boxart"));

        // The negative cache is honoured — no further network.
        Assert.IsNull(await media.GetImageAsync((int)_gameId, "boxart", default));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task TruncatedNameFallback_UsedWhenExactNameMisses()
    {
        // Exact "Mega Man 2 (USA)" 404s; the truncated "Mega Man 2" exists.
        var handler = new RoutingHandler(path => path.Contains("(USA)")
            ? (HttpStatusCode.NotFound, null)
            : (HttpStatusCode.OK, Png));
        var media = NewService(handler);

        var path = await media.GetImageAsync((int)_gameId, "boxart", default);

        Assert.IsNotNull(path);
        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(2, handler.Calls);                  // exact (404) then truncated (200)
        Assert.AreEqual("ok", (await _repo.GetMediaAsync((int)_gameId, "boxart"))!.Value.Status);
    }

    [TestMethod]
    public async Task TransientError_IsNotNegativeCached_AndRetriesNextTime()
    {
        var handler = new RoutingHandler(_ => (HttpStatusCode.InternalServerError, null));
        var media = NewService(handler);

        var path = await media.GetImageAsync((int)_gameId, "boxart", default);

        Assert.IsNull(path);
        Assert.AreEqual(1, handler.Calls);                  // 5xx stops the walk immediately
        Assert.IsNull(await _repo.GetMediaAsync((int)_gameId, "boxart")); // NOT cached

        // A later view retries the network rather than serving a poisoned miss.
        Assert.IsNull(await media.GetImageAsync((int)_gameId, "boxart", default));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task UnknownGame_ReturnsNull_WithoutFetching()
    {
        var handler = new RoutingHandler(_ => (HttpStatusCode.OK, Png));
        var media = NewService(handler);

        Assert.IsNull(await media.GetImageAsync(999999, "boxart", default));
        Assert.AreEqual(0, handler.Calls);
    }

    // --- helpers ---

    private MediaService NewService(RoutingHandler handler)
    {
        var svc = new MediaService(_repo, new StubFactory(handler), NullLogger<MediaService>.Instance);
        svc.Configure(_mediaRoot);
        return svc;
    }

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Stub libretro endpoint: routes on the request's unescaped path; counts calls.</summary>
    private sealed class RoutingHandler(Func<string, (HttpStatusCode Code, byte[]? Body)> route) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var (code, body) = route(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            var resp = new HttpResponseMessage(code);
            if (body is not null) resp.Content = new ByteArrayContent(body);
            return Task.FromResult(resp);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
