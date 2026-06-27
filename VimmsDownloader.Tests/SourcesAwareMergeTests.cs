using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Catalog;

namespace VimmsDownloader.Tests;

/// <summary>
/// D2b (#162): the REAL catalog merge path (<see cref="CatalogRepository.MergeSystemGamesAsync"/> +
/// migration 025 `catalog_game_source`) against a temp SQLite file. Verifies that syncing one console
/// from two data-source origins ACCUMULATES coverage and dedups by canonical_key, that re-syncing one
/// origin never drops another's games, that a game gone from all origins is removed, and that keyless
/// (CRC-only) games don't duplicate on re-sync.
/// </summary>
[TestClass]
public class SourcesAwareMergeTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;
    private long _sys;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"d2bmerge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);
        _sys = await _repo.UpsertSystemAsync("Nintendo - SNES", "snes", "no-intro", default);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static DatGame Game(string name, string? sha1 = null, string? crc = null, string? region = null)
        => new(name, region, null, [new DatRom($"{name}.sfc", 0, crc, null, sha1, null)], []);

    private async Task<List<string>> OriginsOf(string gameName)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT s.origin FROM catalog_game_source s
            JOIN catalog_game g ON g.id = s.game_id
            WHERE g.name = $n ORDER BY s.origin
            """;
        cmd.Parameters.AddWithValue("$n", gameName);
        var origins = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) origins.Add(r.GetString(0));
        return origins;
    }

    private async Task<int> GameCount()
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM catalog_game WHERE system_id = $sid";
        cmd.Parameters.AddWithValue("$sid", _sys);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> RomCount()
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM catalog_rom";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task TwoOrigins_SameConsole_AccumulateAndDedupByKey()
    {
        // libretro lists A,B; daily-bundle lists B (same content),C. Shared B collapses to one row with
        // both origins; A and C are each present once → coverage = 3, no duplicate row.
        await _repo.MergeSystemGamesAsync(_sys, "libretro",
            [Game("Alpha", sha1: "a1"), Game("Beta", sha1: "b2")], "v1", default);
        await _repo.MergeSystemGamesAsync(_sys, "daily-bundle",
            [Game("Beta (Rev 1)", sha1: "b2"), Game("Gamma", sha1: "c3")], "vd", default);

        Assert.AreEqual(3, await GameCount());
        CollectionAssert.AreEqual(new[] { "libretro" }, await OriginsOf("Alpha"));
        CollectionAssert.AreEqual(new[] { "daily-bundle", "libretro" }, await OriginsOf("Beta")); // both, by content
        CollectionAssert.AreEqual(new[] { "daily-bundle" }, await OriginsOf("Gamma"));
        // The shared game kept its first-seen row (name "Beta"), not a duplicate from the 2nd origin.
        Assert.IsEmpty(await OriginsOf("Beta (Rev 1)"));
    }

    [TestMethod]
    public async Task ResyncOneOrigin_DoesNotDropAnothersGames()
    {
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Alpha", sha1: "a1")], "v1", default);
        await _repo.MergeSystemGamesAsync(_sys, "daily-bundle", [Game("Gamma", sha1: "c3")], "vd", default);
        Assert.AreEqual(2, await GameCount());

        // Re-sync libretro (Alpha only) — the bundle-only Gamma must survive.
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Alpha", sha1: "a1")], "v2", default);
        Assert.AreEqual(2, await GameCount());
        CollectionAssert.AreEqual(new[] { "daily-bundle" }, await OriginsOf("Gamma"));
    }

    [TestMethod]
    public async Task GameGoneFromItsOnlyOrigin_IsRemoved()
    {
        await _repo.MergeSystemGamesAsync(_sys, "libretro",
            [Game("Alpha", sha1: "a1"), Game("Beta", sha1: "b2")], "v1", default);
        Assert.AreEqual(2, await GameCount());

        // Beta dropped from libretro (its only source) → removed, along with its rom.
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Alpha", sha1: "a1")], "v2", default);
        Assert.AreEqual(1, await GameCount());
        Assert.AreEqual(1, await RomCount());
        Assert.IsEmpty(await OriginsOf("Beta"));
    }

    [TestMethod]
    public async Task DroppedFromOneOrigin_SurvivesIfAnotherSourcesIt()
    {
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Beta", sha1: "b2")], "v1", default);
        await _repo.MergeSystemGamesAsync(_sys, "daily-bundle", [Game("Beta", sha1: "b2")], "vd", default);
        CollectionAssert.AreEqual(new[] { "daily-bundle", "libretro" }, await OriginsOf("Beta"));

        // libretro re-syncs empty → its link drops, but daily-bundle still sources Beta, so it stays.
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [], "v2", default);
        Assert.AreEqual(1, await GameCount());
        CollectionAssert.AreEqual(new[] { "daily-bundle" }, await OriginsOf("Beta"));
    }

    [TestMethod]
    public async Task KeylessGame_NotDuplicatedOnResyncOfSameOrigin()
    {
        // CRC-only ⇒ no canonical_key ⇒ can't dedup by content; re-syncing the same origin must still
        // not pile up duplicates (the stale prior copy is pruned, leaving exactly one).
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Gamma", crc: "12345678")], "v1", default);
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Gamma", crc: "12345678")], "v2", default);

        Assert.AreEqual(1, await GameCount());
        Assert.AreEqual(1, await RomCount());
        CollectionAssert.AreEqual(new[] { "libretro" }, await OriginsOf("Gamma"));
    }

    [TestMethod]
    public async Task GetGames_ConsolidatesOriginsOntoTheRow()
    {
        // D2b-2 (#167): the browse row carries the DAT origin(s). Alpha (libretro only), Beta (both),
        // Gamma (bundle only) → one row each, with origins consolidated.
        await _repo.MergeSystemGamesAsync(_sys, "libretro",
            [Game("Alpha", sha1: "a1"), Game("Beta", sha1: "b2")], "v1", default);
        await _repo.MergeSystemGamesAsync(_sys, "daily-bundle",
            [Game("Beta", sha1: "b2"), Game("Gamma", sha1: "c3")], "vd", default);

        var (_, rows) = await _repo.GetGamesAsync("snes", null, "all", dedupe: false,
            english: false, excludeCategories: false, searchMode: "substring", page: 0, pageSize: 50);
        var byName = rows.ToDictionary(g => g.Name);
        CollectionAssert.AreEqual(new[] { "libretro" }, byName["Alpha"].Origins);
        CollectionAssert.AreEqual(new[] { "daily-bundle", "libretro" }, byName["Beta"].Origins.OrderBy(x => x).ToArray());
        CollectionAssert.AreEqual(new[] { "daily-bundle" }, byName["Gamma"].Origins);
    }

    [TestMethod]
    public async Task MergedGame_BindsSecondOrigin_WithoutDuplicatingRoms()
    {
        await _repo.MergeSystemGamesAsync(_sys, "libretro", [Game("Beta", sha1: "b2")], "v1", default);
        await _repo.MergeSystemGamesAsync(_sys, "daily-bundle", [Game("Beta", sha1: "b2")], "vd", default);

        // One game, one rom, two origin links — the 2nd origin merged onto the existing content.
        Assert.AreEqual(1, await GameCount());
        Assert.AreEqual(1, await RomCount());
        Assert.HasCount(2, await OriginsOf("Beta"));
    }
}
