using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Catalog;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL <see cref="CatalogRepository"/> compat path end-to-end against a temp SQLite file
/// with the real migrations: <see cref="CatalogRepository.ReplaceCompatAsync"/> stores per-emulator
/// entries under a match kind, and <see cref="CatalogRepository.GetGamesAsync"/> projects them as a
/// per-emulator list and filters by emulator/status.
/// </summary>
[TestClass]
public class CatalogCompatTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;
    private long _ps3;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmcompat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);

        _ps3 = await ScalarLong("INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT ps3', 'ps3', 'redump') RETURNING id");
        await AddGame(_ps3, "Demon's Souls (USA)", "BLUS-30443");
        await AddGame(_ps3, "Heavy Rain (USA)", "BCUS-98164");
        await AddGame(_ps3, "Unlisted Game (USA)", "BLUS-99999"); // no compat entry
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Task<(int Total, List<CatalogGameDto> Games)> Games(string? emulator = null, string? status = null) =>
        _repo.GetGamesAsync("ps3", null, "all", false, false, false, "substring", 0, 100, emulator, status);

    [TestMethod]
    public async Task ReplaceCompat_ProjectsPerEmulatorStatusList()
    {
        await _repo.ReplaceCompatAsync("rpcs3", "serial",
            [new CompatEntry("BLUS30443", "Playable"), new CompatEntry("BCUS98164", "Ingame")], CancellationToken.None);

        var (_, games) = await Games();
        var demons = games.Single(g => g.Name.StartsWith("Demon"));
        Assert.HasCount(1, demons.Compat);
        Assert.AreEqual("rpcs3", demons.Compat[0].Emulator);
        Assert.AreEqual("Playable", demons.Compat[0].Status);

        var unlisted = games.Single(g => g.Name.StartsWith("Unlisted"));
        Assert.IsEmpty(unlisted.Compat); // no entry → empty list, never null
    }

    [TestMethod]
    public async Task EmulatorFilter_NarrowsToGamesWithThatEmulator()
    {
        await _repo.ReplaceCompatAsync("rpcs3", "serial",
            [new CompatEntry("BLUS30443", "Playable")], CancellationToken.None);

        var (total, games) = await Games(emulator: "rpcs3");
        Assert.AreEqual(1, total);                       // only the game with an rpcs3 entry
        Assert.AreEqual("Demon's Souls (USA)", games[0].Name);

        var (none, _) = await Games(emulator: "pcsx2");   // no entries for this emulator
        Assert.AreEqual(0, none);
    }

    [TestMethod]
    public async Task StatusFilter_NarrowsToThatStatus()
    {
        await _repo.ReplaceCompatAsync("rpcs3", "serial",
            [new CompatEntry("BLUS30443", "Playable"), new CompatEntry("BCUS98164", "Ingame")], CancellationToken.None);

        var (playable, games) = await Games(emulator: "rpcs3", status: "Playable");
        Assert.AreEqual(1, playable);
        Assert.AreEqual("Demon's Souls (USA)", games[0].Name);

        var (ingame, _) = await Games(emulator: "rpcs3", status: "Ingame");
        Assert.AreEqual(1, ingame);
    }

    [TestMethod]
    public async Task ReplaceCompat_IsPerEmulatorWholesaleReplace()
    {
        await _repo.ReplaceCompatAsync("rpcs3", "serial",
            [new CompatEntry("BLUS30443", "Playable")], CancellationToken.None);
        // Re-sync with a different status replaces the prior rpcs3 rows for this key.
        await _repo.ReplaceCompatAsync("rpcs3", "serial",
            [new CompatEntry("BLUS30443", "Nothing")], CancellationToken.None);

        var (_, games) = await Games();
        var demons = games.Single(g => g.Name.StartsWith("Demon"));
        Assert.AreEqual("Nothing", demons.Compat.Single().Status);
    }

    // --- seeding helpers ---

    private async Task AddGame(long systemId, string name, string serial) =>
        await ExecAsync(
            "INSERT INTO catalog_game (system_id, name, serial, serial_key) VALUES ($sid, $name, $serial, $skey)",
            ("$sid", systemId), ("$name", name), ("$serial", serial), ("$skey", CompatKeys.NormalizeSerial(serial)));

    private async Task ExecAsync(string sql, params (string Key, object Value)[] ps)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
