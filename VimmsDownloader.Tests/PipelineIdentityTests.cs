using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Phase C / C1: the source-agnostic pipeline identity schema (migration 022). Exercises the REAL
/// <see cref="QueueRepository"/> + <see cref="DatabaseMigrator"/> against a temp SQLite file for the
/// live persistence path (CompleteItemAsync resolving a Vimm completion's catalog game_id;
/// AppendEventAsync carrying the new identity columns), plus a focused in-memory check of the 022
/// best-effort backfill against pre-existing rows. Additive + null-safe: archive / unmatched items
/// stay game_id NULL and fall back to filename downstream (the C2 cutover consumes these columns).
/// </summary>
[TestClass]
public class PipelineIdentityTests
{
    private string _dir = null!;
    private string _connStr = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pipeid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Migration022_AddsIdentityColumns()
    {
        await using var db = await Migrate();

        CollectionAssert.IsSubsetOf(
            new[] { "game_id", "format", "source" }, await ColumnNames(db, "events"),
            "events should gain game_id/format/source");
        CollectionAssert.Contains(await ColumnNames(db, "completed_urls"), "game_id",
            "completed_urls should gain game_id");
    }

    [TestMethod]
    public async Task CompleteItem_ResolvesVimmGameId()
    {
        var gameId = await SeedGame(vaultId: 1001);

        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        await repo.AddToQueueAsync("https://vimm.net/vault/1001", 1);
        var next = (await repo.GetNextQueueItemAsync())!.Value;
        await repo.CompleteItemAsync(next.Id, next.Url, "Game.7z",
            Path.Combine(_dir, "downloads", "completed", "Game.7z"), next.Format);

        Assert.AreEqual(gameId, await ScalarLong(
            "SELECT game_id FROM completed_urls WHERE source_id = 'https://vimm.net/vault/1001'"));
    }

    [TestMethod]
    public async Task CompleteItem_ArchiveSource_LeavesGameIdNull()
    {
        await SeedGame(vaultId: 1001);

        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        // Archive items resolve via (source, source_id) — no inline hash here, so they stay game_id
        // NULL (source != 'vimm') and key on filename downstream.
        await repo.AddToQueueAsync("https://archive.org/x", 0, source: "archive");
        var next = (await repo.GetNextQueueItemAsync())!.Value;
        await repo.CompleteItemAsync(next.Id, next.Url, "Arc.iso",
            Path.Combine(_dir, "downloads", "completed", "Arc.iso"), next.Format);

        Assert.IsTrue(await IsNull(
            "SELECT game_id FROM completed_urls WHERE source_id = 'https://archive.org/x'"));
    }

    [TestMethod]
    public async Task CompleteItem_UnmatchedVimmUrl_LeavesGameIdNull()
    {
        await SeedGame(vaultId: 1001); // catalog only has vault 1001 bound

        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        await repo.AddToQueueAsync("https://vimm.net/vault/9999", 0); // not in catalog
        var next = (await repo.GetNextQueueItemAsync())!.Value;
        await repo.CompleteItemAsync(next.Id, next.Url, "Other.7z",
            Path.Combine(_dir, "downloads", "completed", "Other.7z"), next.Format);

        Assert.IsTrue(await IsNull(
            "SELECT game_id FROM completed_urls WHERE source_id = 'https://vimm.net/vault/9999'"));
    }

    [TestMethod]
    public async Task AppendEvent_PersistsIdentityColumns()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        await repo.AppendEventAsync("Game.7z", "pipeline_status", "Done", "ok", null,
            correlationId: "abc123", gameId: 42, format: 1, source: "vimm");

        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT game_id, format, source FROM events WHERE correlation_id = 'abc123'";
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.IsTrue(await r.ReadAsync());
        Assert.AreEqual(42L, r.GetInt64(0));
        Assert.AreEqual(1L, r.GetInt64(1));
        Assert.AreEqual("vimm", r.GetString(2));
    }

    [TestMethod]
    public async Task AppendEvent_DefaultsIdentityToNull()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        await repo.AppendEventAsync("Game.7z", "download_status", null, "m", null, correlationId: "noid");

        Assert.IsTrue(await IsNull("SELECT game_id FROM events WHERE correlation_id = 'noid'"));
    }

    [TestMethod]
    public async Task Migration022_BackfillsExistingVimmCompletions()
    {
        // Focused check of the 022 backfill against rows that pre-date the migration: build the
        // minimal pre-022 shape, seed a Vimm + an archive completion, then apply the REAL 022 file.
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        await Exec(db, """
            -- is_parent (added by migration 015) is present when 022 runs in the real migrator; the 022
            -- backfill uses it as a deterministic tie-break, so the pre-022 shape must carry it too.
            CREATE TABLE catalog_game (id INTEGER PRIMARY KEY AUTOINCREMENT, system_id INTEGER, name TEXT, is_parent INTEGER, vault_id INTEGER);
            CREATE TABLE events (id INTEGER PRIMARY KEY AUTOINCREMENT, item_name TEXT, event_type TEXT, correlation_id TEXT);
            CREATE TABLE completed_urls (
                id INTEGER PRIMARY KEY AUTOINCREMENT, url TEXT, filename TEXT,
                source TEXT, source_id TEXT, format INTEGER
            );
        """);
        await Exec(db, "INSERT INTO catalog_game (id, name, vault_id) VALUES (7, 'Bound', 1001)");
        await Exec(db, "INSERT INTO completed_urls (url, filename, source, source_id, format) VALUES ('u1', 'G.7z', 'vimm', 'https://vimm.net/vault/1001', 0)");
        await Exec(db, "INSERT INTO completed_urls (url, filename, source, source_id, format) VALUES ('u2', 'A.iso', 'archive', 'arc-1', 0)");

        foreach (var stmt in SplitStatements(await File.ReadAllTextAsync(FindMigration("022_pipeline_identity.sql"))))
            await Exec(db, stmt);

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT game_id FROM completed_urls WHERE source_id = 'https://vimm.net/vault/1001'";
            Assert.AreEqual(7L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
        }
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT game_id FROM completed_urls WHERE source_id = 'arc-1'";
            Assert.AreEqual(DBNull.Value, await cmd.ExecuteScalarAsync()); // archive stays legacy/null
        }
    }

    // --- C2: identity resolution at the bridge boundary + per-game event grouping ---

    [TestMethod]
    public async Task ResolveEventIdentity_FromCompletedFilename()
    {
        var gameId = await SeedGame(vaultId: 1001);
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        await repo.AddToQueueAsync("https://vimm.net/vault/1001", 1);
        var next = (await repo.GetNextQueueItemAsync())!.Value;
        await repo.CompleteItemAsync(next.Id, next.Url, "Game.7z",
            Path.Combine(_dir, "downloads", "completed", "Game.7z"), next.Format);

        // The pipeline bridge resolves by the completed filename it emits status for.
        var (gid, fmt, src) = await repo.ResolveEventIdentityAsync("Game.7z");
        Assert.AreEqual(gameId, gid);
        Assert.AreEqual(1, fmt);
        Assert.AreEqual("vimm", src);
    }

    [TestMethod]
    public async Task ResolveEventIdentity_InFlightFromQueue()
    {
        var gameId = await SeedGame(vaultId: 1001);
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        // Still queued (not completed) — the download bridge resolves by the vault URL.
        await repo.AddToQueueAsync("https://vimm.net/vault/1001", 2);

        var (gid, fmt, src) = await repo.ResolveEventIdentityAsync("https://vimm.net/vault/1001");
        Assert.AreEqual(gameId, gid);
        Assert.AreEqual(2, fmt);
        Assert.AreEqual("vimm", src);
    }

    [TestMethod]
    public async Task ResolveEventIdentity_UnknownOrLifecycle_ReturnsNulls()
    {
        await SeedGame(vaultId: 1001);
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        foreach (var name in new[] { "NoSuchFile.7z", "_queue", "" })
        {
            var (gid, fmt, src) = await repo.ResolveEventIdentityAsync(name);
            Assert.IsNull(gid, $"game_id should be null for '{name}'");
            Assert.IsNull(fmt);
            Assert.IsNull(src);
        }
    }

    [TestMethod]
    public async Task PipelineEvent_StampedWithIdentity_LikeBridge()
    {
        var gameId = await SeedGame(vaultId: 1001);
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);
        await repo.AddToQueueAsync("https://vimm.net/vault/1001", 1);
        var next = (await repo.GetNextQueueItemAsync())!.Value;
        await repo.CompleteItemAsync(next.Id, next.Url, "Game.7z",
            Path.Combine(_dir, "downloads", "completed", "Game.7z"), next.Format);

        // Mirror SignalRPs3PipelineBridge: resolve identity, then append the event with it.
        var (gid, fmt, src) = await repo.ResolveEventIdentityAsync("Game.7z");
        await repo.AppendEventAsync("Game.7z", "pipeline_status", "Done", "ISO ready", null,
            correlationId: "run1", gameId: gid, format: fmt, source: src);

        var events = (await repo.GetEventsAsync(gameId: gameId)).Events;
        Assert.HasCount(1, events);
        Assert.AreEqual(gameId, events[0].GameId);
        Assert.AreEqual(1, events[0].Format);
    }

    [TestMethod]
    public async Task EventsFilteredByGameId_GroupAcrossFormatsAndRetries()
    {
        var repo = new QueueRepository();
        await repo.InitAsync(_connStr, NullLogger.Instance);

        // Same game, two formats + a retry; plus an unrelated game and a legacy (null) event.
        await repo.AppendEventAsync("A.7z", "pipeline_status", "Done", "m", null, correlationId: "r1", gameId: 5, format: 0, source: "vimm");
        await repo.AppendEventAsync("A.dec.iso", "pipeline_status", "Done", "m", null, correlationId: "r2", gameId: 5, format: 1, source: "vimm");
        await repo.AppendEventAsync("A.7z", "pipeline_status", "Done", "m", null, correlationId: "r3", gameId: 5, format: 0, source: "vimm");
        await repo.AppendEventAsync("B.7z", "pipeline_status", "Done", "m", null, correlationId: "r4", gameId: 6, format: 0, source: "vimm");
        await repo.AppendEventAsync("Legacy.7z", "pipeline_status", "Done", "m", null, correlationId: "r5");

        var grouped = (await repo.GetEventsAsync(gameId: 5)).Events;
        Assert.HasCount(3, grouped, "all three game-5 events (both formats + retry) group together");
        CollectionAssert.AreEquivalent(new[] { 0, 1, 0 }, grouped.Select(e => e.Format!.Value).ToList());
        Assert.IsTrue(grouped.All(e => e.GameId == 5));
    }

    // --- helpers ---

    private async Task<SqliteConnection> Migrate()
    {
        var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        return db;
    }

    /// <summary>Seed a catalog system + game bound to the given Vimm vault id; returns the game id.</summary>
    private async Task<long> SeedGame(long vaultId)
    {
        await using var db = await Migrate();
        await Exec(db, "INSERT INTO catalog_system (dat_name, console, source) VALUES ('Test', 'ps3', 'redump')");
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO catalog_game (system_id, name, vault_id) VALUES (1, 'Bound Game', $v); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$v", vaultId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> IsNull(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync();
        return v is null || v == DBNull.Value;
    }

    private static async Task Exec(SqliteConnection db, string sql)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ColumnNames(SqliteConnection db, string table)
    {
        var cols = new List<string>();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) cols.Add(r.GetString(1));
        return cols.ToArray();
    }

    private static IEnumerable<string> SplitStatements(string sql)
        => string.Join('\n', sql.Split('\n').Select(l => l.TrimStart().StartsWith("--") ? "" : l))
              .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
              .Where(s => !string.IsNullOrWhiteSpace(s));

    private static string FindMigration(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "VimmsDownloader", "Migrations", name);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Could not locate migration {name} from {AppContext.BaseDirectory}");
    }
}
