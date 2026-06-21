using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL <see cref="DatabaseMigrator"/> and <see cref="QueueRepository"/> (host
/// internals, reachable via InternalsVisibleTo) against a temp SQLite file — not a mirror of their
/// SQL. Covers the migration path and the enqueue → next → complete round-trip the DB-heavy phases
/// rely on (#6).
/// </summary>
[TestClass]
public class RepositoryTests
{
    private string _dir = null!;
    private string _connStr = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"repo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools(); // drop pooled handles (WAL) so the temp dir can be removed
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Migrator_CreatesAllTables_AndIsIdempotent()
    {
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance); // re-run must be a no-op, not throw
        }

        var tables = await TableNames();
        foreach (var t in new[] { "queued_urls", "completed_urls", "source_meta", "settings",
                                  "events", "schema_migrations", "catalog_system", "catalog_game",
                                  "catalog_rom", "catalog_owned", "catalog_set", "catalog_compat" })
            CollectionAssert.Contains(tables, t, $"missing table {t}");

        // Tracked + idempotent: the latest migration is recorded exactly once after two runs.
        Assert.AreEqual(1L, await ScalarLong(
            "SELECT COUNT(*) FROM schema_migrations WHERE name = '019_catalog_serial_key.sql'"));
    }

    [TestMethod]
    public async Task QueueRepository_EnqueueNextComplete_RoundTrips()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        await repo.AddToQueueAsync("https://vimm.net/vault/1001", 1);

        var next = await repo.GetNextQueueItemAsync();
        Assert.IsNotNull(next);
        Assert.AreEqual("https://vimm.net/vault/1001", next.Value.Url);
        Assert.AreEqual(1, next.Value.Format);
        Assert.AreEqual("vimm", next.Value.Source); // default source backfilled

        await repo.CompleteItemAsync(next.Value.Id, next.Value.Url, "Game.7z",
            Path.Combine(_dir, "downloads", "completed", "Game.7z"), next.Value.Format);

        // Queue drained; the item now lives in completed history.
        Assert.IsNull(await repo.GetNextQueueItemAsync());
        Assert.IsFalse(await repo.HasQueuedUrlsAsync());
        var completed = await repo.GetCompletedItemsEnrichedAsync();
        Assert.HasCount(1, completed);
        Assert.AreEqual("Game.7z", completed[0].Filename);
        Assert.AreEqual("https://vimm.net/vault/1001", completed[0].Url);
    }

    [TestMethod]
    public async Task QueueRepository_Settings_RoundTripAndUpsert()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        // Use a key no migration seeds, so the initial read is genuinely null.
        const string key = "test_only_setting";
        Assert.IsNull(await repo.GetSettingAsync(key));
        await repo.SaveSettingAsync(key, "4");
        Assert.AreEqual("4", await repo.GetSettingAsync(key));
        await repo.SaveSettingAsync(key, "8"); // INSERT OR REPLACE
        Assert.AreEqual("8", await repo.GetSettingAsync(key));
    }

    [TestMethod]
    public async Task QueueRepository_CheckDuplicates_DetectsQueued_IgnoresUnknown()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        await repo.AddToQueueAsync("https://vimm.net/vault/2001", 0);

        var dups = await repo.CheckDuplicatesAsync(["https://vimm.net/vault/2001"]);
        Assert.HasCount(1, dups);
        Assert.AreEqual("queued", dups[0].Source);

        Assert.IsEmpty(await repo.CheckDuplicatesAsync(["https://vimm.net/vault/9999"]));
    }

    // --- helpers ---

    private async Task<List<string>> TableNames()
    {
        var names = new List<string>();
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) names.Add(r.GetString(0));
        return names;
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
