using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Download.Sources;

namespace VimmsDownloader.Tests;

/// <summary>
/// Covers the archive→Vimm download fallback (the "doesn't fall back to Vimm" fix): the repository's
/// vault-binding read, and <see cref="CatalogResolveService.ResolveForQueueAsync"/> falling back to
/// the pre-bound vault URL with a sensible format when no archive set provides the game.
/// </summary>
[TestClass]
public class VimmFallbackTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmfb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private CatalogResolveService NewResolver() =>
        // No archive sets are configured in these tests, so ResolveAsync short-circuits to null before
        // touching the registry/HTTP — an empty registry + factory is enough.
        new(_repo, new SourceRegistry([]), new ThrowingHttpClientFactory(), NullLogger<CatalogResolveService>.Instance);

    [TestMethod]
    public async Task GetVaultBinding_ReturnsVaultAndFormats_OrNull()
    {
        var ps3 = await Seed("ps3");
        var game = await AddGame(ps3, "Uncharted");
        await _repo.BindVimmAsync(game, 24614, "sha1",
            [new(0, "JB Folder", 6397335, "6.1 GB"), new(1, ".dec.iso", 6586788, "6.28 GB")], default);
        var unbound = await AddGame(ps3, "Unbound");

        var binding = await _repo.GetVaultBindingAsync((int)game);
        Assert.IsNotNull(binding);
        Assert.AreEqual(24614L, binding.Value.VaultId);
        Assert.HasCount(2, binding.Value.Formats);
        Assert.AreEqual((1, ".dec.iso"), (binding.Value.Formats[1].Alt, binding.Value.Formats[1].Label));

        Assert.IsNull(await _repo.GetVaultBindingAsync((int)unbound));
    }

    [TestMethod]
    public async Task ResolveForQueue_FallsBackToVimm_WhenNoArchiveSet()
    {
        var ps3 = await Seed("ps3");
        var game = await AddGame(ps3, "Uncharted");
        await _repo.BindVimmAsync(game, 24614, "sha1",
            [new(0, "JB Folder", 6397335, "6.1 GB"), new(1, ".dec.iso", 6586788, "6.28 GB")], default);

        // Requested format offered → used.
        var r = await NewResolver().ResolveForQueueAsync((int)game, "ps3", "Uncharted", 1, default);
        Assert.AreEqual(("https://vimm.net/vault/24614", "vimm", 1), r);

        // Requested format not offered → first available (0).
        var r2 = await NewResolver().ResolveForQueueAsync((int)game, "ps3", "Uncharted", 9, default);
        Assert.AreEqual(("https://vimm.net/vault/24614", "vimm", 0), r2);

        // No format requested → first available (0).
        var r3 = await NewResolver().ResolveForQueueAsync((int)game, "ps3", "Uncharted", null, default);
        Assert.AreEqual(0, r3!.Value.Format);
    }

    [TestMethod]
    public async Task ResolveForQueue_ReturnsNull_WhenNeitherArchiveNorVimm()
    {
        var ps3 = await Seed("ps3");
        var game = await AddGame(ps3, "No Source");   // no archive set, no Vimm binding

        var r = await NewResolver().ResolveForQueueAsync((int)game, "ps3", "No Source", null, default);
        Assert.IsNull(r);
    }

    // --- helpers ---

    private async Task<long> Seed(string console) =>
        await ScalarLong($"INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT {console}', '{console}', 'redump') RETURNING id");

    private async Task<long> AddGame(long systemId, string name) =>
        await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({systemId}, '{name}') RETURNING id");

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("HTTP should not be called when no archive sets exist");
    }
}
