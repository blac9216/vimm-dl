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

        // Requested format offered → used. For Vimm the source id is the vault URL itself.
        var r = await NewResolver().ResolveForQueueAsync((int)game, "ps3", "Uncharted", 1, default);
        Assert.AreEqual(("https://vimm.net/vault/24614", "vimm", 1, "https://vimm.net/vault/24614"), r);

        // Requested format not offered → first available (0).
        var r2 = await NewResolver().ResolveForQueueAsync((int)game, "ps3", "Uncharted", 9, default);
        Assert.AreEqual(("https://vimm.net/vault/24614", "vimm", 0, "https://vimm.net/vault/24614"), r2);

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

    /// <summary>
    /// #266 AC: a Wii U digital title has no archive set and no vault binding — it downloads from NUS,
    /// keyed by the 16-hex title id the CDN DAT stored as its serial.
    /// </summary>
    [TestMethod]
    public async Task ResolveForQueue_WiiUDigital_ResolvesToNusByTitleId()
    {
        var wiiu = await Seed("wiiu");
        var game = await AddGame(wiiu, "Super Mario Maker (USA)", "000500001018DC00");

        var r = await NewResolver().ResolveForQueueAsync((int)game, "wiiu", "Super Mario Maker (USA)", null, default);

        Assert.IsNotNull(r);
        Assert.AreEqual("wiiu", r.Value.Source);
        Assert.AreEqual("000500001018DC00", r.Value.SourceId);   // what WiiUNusSource resolves against
        Assert.AreEqual(0, r.Value.Format);
        StringAssert.Contains(r.Value.Url, "000500001018dc00");  // real, unique per-title NUS URL
    }

    /// <summary>A Wii U *disc* bound to a vault entry must still take the Vimm path, not NUS.</summary>
    [TestMethod]
    public async Task ResolveForQueue_WiiUDisc_PrefersVimmBindingOverNus()
    {
        var wiiu = await Seed("wiiu");
        var game = await AddGame(wiiu, "Adventure Time (USA)", "000500001014E100");
        await _repo.BindVimmAsync(game, 128227, "sha1", [new(0, ".wux", 2100000, "2.01 GB")], default);

        var r = await NewResolver().ResolveForQueueAsync((int)game, "wiiu", "Adventure Time (USA)", null, default);

        Assert.AreEqual("vimm", r!.Value.Source);
        Assert.AreEqual("https://vimm.net/vault/128227", r.Value.Url);
    }

    [TestMethod]
    public async Task ResolveForQueue_WiiU_NonTitleIdSerial_ReturnsNull()
    {
        var wiiu = await Seed("wiiu");
        var game = await AddGame(wiiu, "Odd One", "NOT-A-TITLE-ID");

        Assert.IsNull(await NewResolver().ResolveForQueueAsync((int)game, "wiiu", "Odd One", null, default));
    }

    /// <summary>The NUS branch is Wii U only — a title-id-shaped serial elsewhere must not trigger it.</summary>
    [TestMethod]
    public async Task ResolveForQueue_TitleIdShapedSerial_OnOtherConsole_ReturnsNull()
    {
        var ps3 = await Seed("ps3");
        var game = await AddGame(ps3, "Coincidence", "000500001018DC00");

        Assert.IsNull(await NewResolver().ResolveForQueueAsync((int)game, "ps3", "Coincidence", null, default));
    }

    // --- helpers ---

    private async Task<long> Seed(string console) =>
        await ScalarLong("INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT ' || $console, $console, 'redump') RETURNING id",
            ("$console", console));

    private async Task<long> AddGame(long systemId, string name, string? serial = null) =>
        await ScalarLong("INSERT INTO catalog_game (system_id, name, serial) VALUES ($sid, $name, $serial) RETURNING id",
            ("$sid", systemId), ("$name", name), ("$serial", (object?)serial ?? DBNull.Value));

    private async Task<long> ScalarLong(string sql, params (string Name, object Value)[] parameters)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("HTTP should not be called when no archive sets exist");
    }
}
