using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Catalog;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL catalog sync path (migration 024 + <see cref="CatalogRepository.MergeSystemGamesAsync"/>
/// + <see cref="CanonicalKey"/>) against a temp SQLite file: the canonical content key is persisted per
/// game, and the SAME ROM content synced under two different systems/DATs yields the SAME key — the
/// cross-source identity D2 (#129) is built on (within-system row-merge lands in D2b / #162).
/// </summary>
[TestClass]
public class CanonicalKeyRepositoryTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"canonkey-{Guid.NewGuid():N}");
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

    private static DatGame Game(string name, params DatRom[] roms) => new(name, null, null, roms, []);
    private static DatRom Rom(string name, string? sha1 = null, string? crc = null)
        => new(name, 0, crc, null, sha1, null);

    private async Task<string?> KeyOf(string gameName)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT canonical_key FROM catalog_game WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", gameName);
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? null : (string)r;
    }

    [TestMethod]
    public async Task SameContent_AcrossTwoSystems_GetsSameCanonicalKey()
    {
        // Two distinct DATs/systems that happen to carry the same dump (e.g. a game re-listed across
        // mirrors) — keyed by content, they share one identity even though names/systems differ.
        var sysA = await _repo.UpsertSystemAsync("Mirror A - SNES", "snes", "no-intro", default);
        var sysB = await _repo.UpsertSystemAsync("Mirror B - SNES", "snes", "no-intro", default);

        await _repo.MergeSystemGamesAsync(sysA, "libretro", [Game("Quest (USA)", Rom("q.sfc", sha1: "AAAA"))], "v1", default);
        await _repo.MergeSystemGamesAsync(sysB, "libretro", [Game("Quest (Europe)", Rom("q.sfc", sha1: "aaaa"))], "v1", default);

        var keyA = await KeyOf("Quest (USA)");
        var keyB = await KeyOf("Quest (Europe)");
        Assert.AreEqual("sha1:aaaa", keyA);
        Assert.AreEqual(keyA, keyB);
    }

    [TestMethod]
    public async Task DifferentContent_GetsDifferentKeys_AndCrcOnlyIsNull()
    {
        var sys = await _repo.UpsertSystemAsync("Mirror A - SNES", "snes", "no-intro", default);
        await _repo.MergeSystemGamesAsync(sys, "libretro",
        [
            Game("Alpha", Rom("a.sfc", sha1: "1111")),
            Game("Beta", Rom("b.sfc", sha1: "2222")),
            Game("Gamma", Rom("c.sfc", crc: "12345678")),   // CRC-only ⇒ no reliable key
        ], "v1", default);

        Assert.AreEqual("sha1:1111", await KeyOf("Alpha"));
        Assert.AreEqual("sha1:2222", await KeyOf("Beta"));
        Assert.IsNull(await KeyOf("Gamma"));
    }

    [TestMethod]
    public async Task MultiDisc_KeyedOnSortedSet_SurvivesResync()
    {
        var sys = await _repo.UpsertSystemAsync("Redump - PS1", "psx", "redump", default);
        await _repo.MergeSystemGamesAsync(sys, "libretro",
            [Game("Epic (USA)", Rom("d1.bin", sha1: "1111"), Rom("d2.bin", sha1: "2222"))], "v1", default);
        var first = await KeyOf("Epic (USA)");
        StringAssert.StartsWith(first, "set:");

        // Re-sync the same system/origin: the set key matches, so the row merges onto itself — stable identity.
        await _repo.MergeSystemGamesAsync(sys, "libretro",
            [Game("Epic (USA)", Rom("d2.bin", sha1: "2222"), Rom("d1.bin", sha1: "1111"))], "v2", default);
        Assert.AreEqual(first, await KeyOf("Epic (USA)"));
    }
}
