using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Catalog;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the real <see cref="RetroAchievementsSyncService"/> end-to-end against a temp SQLite DB
/// (real migrations, incl. 031's ra_players) and the stub RA endpoints: it no-ops without an API key, and
/// with one it pulls a console's RA game list, hash-joins it to the catalog by MD5 (case-insensitively),
/// fetches each matched game's player count, and stores ra_players + a blended rank_score. Incremental
/// re-runs skip already-populated games unless forced.
/// </summary>
[TestClass]
public class RetroAchievementsSyncServiceTests
{
    private const string ChronoMd5 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string MarioMd5 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string UnmatchedMd5 = "cccccccccccccccccccccccccccccccc";

    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;
    private long _snes;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ra-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance); // real migrations (incl. 031 ra_players)
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
        _snes = await ScalarLong("INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Sync_NoApiKey_NoOps_WithoutAnyRequest()
    {
        await AddGame("Chrono Trigger (USA)", ChronoMd5.ToUpperInvariant(), igdbRating: 94.5, igdbCount: 1200);
        var handler = new StubRaHandler();
        var n = await NewService(handler).SyncAsync(force: false, default);

        Assert.AreEqual(0, n);
        Assert.AreEqual(0, handler.GameListCalls);
    }

    [TestMethod]
    public async Task Sync_HashJoinsCaseInsensitively_StoresPlayersAndBlendedScore()
    {
        await _queue.SaveSettingAsync(SettingsKeys.RetroAchievementsApiKey, "key");
        // Catalog MD5s stored UPPERCASE; RA hashes returned lowercase — the join must still match.
        var chrono = await AddGame("Chrono Trigger (USA)", ChronoMd5.ToUpperInvariant(), igdbRating: 94.5, igdbCount: 1200);
        var mario = await AddGame("Super Mario World (USA)", MarioMd5.ToUpperInvariant()); // no IGDB rating
        var orphan = await AddGame("Unmatched (USA)", UnmatchedMd5.ToUpperInvariant());    // RA has no hash for it

        var handler = SnesHandler();
        var n = await NewService(handler).SyncAsync(force: false, default);

        Assert.AreEqual(2, n);                                   // Chrono + Mario matched; orphan didn't
        Assert.AreEqual(1, handler.GameListCalls);               // only snes has catalog games → one list call
        Assert.AreEqual(2, handler.ExtendedCalls);               // one per matched game
        Assert.AreEqual("key", handler.LastApiKey);

        var (chPlayers, chScore) = await Rank(chrono);
        Assert.AreEqual(50000, chPlayers);
        Assert.AreEqual(RankScore.Blend(94.5, 1200, 50000), chScore!.Value, 0.0001); // quality+popularity blend

        var (mPlayers, mScore) = await Rank(mario);
        Assert.AreEqual(8000, mPlayers);
        Assert.AreEqual(RankScore.Blend(null, null, 8000), mScore!.Value, 0.0001);   // popularity-only

        Assert.IsNull((await Rank(orphan)).Players);             // unmatched → untouched
    }

    [TestMethod]
    public async Task Sync_Incremental_SkipsAlreadyPopulated_UnlessForced()
    {
        await _queue.SaveSettingAsync(SettingsKeys.RetroAchievementsApiKey, "key");
        await AddGame("Chrono Trigger (USA)", ChronoMd5.ToUpperInvariant(), igdbRating: 94.5, igdbCount: 1200);
        await AddGame("Super Mario World (USA)", MarioMd5.ToUpperInvariant());
        var handler = SnesHandler();
        var svc = NewService(handler);

        Assert.AreEqual(2, await svc.SyncAsync(force: false, default)); // first run populates both
        var afterFirst = handler.ExtendedCalls;
        Assert.AreEqual(2, afterFirst);

        // Incremental: both already have ra_players → list is re-fetched but no per-game calls.
        Assert.AreEqual(0, await svc.SyncAsync(force: false, default));
        Assert.AreEqual(afterFirst, handler.ExtendedCalls);

        // Force refetches every matched game.
        Assert.AreEqual(2, await svc.SyncAsync(force: true, default));
        Assert.AreEqual(afterFirst + 2, handler.ExtendedCalls);
    }

    // RA game list for SNES (id 3): Chrono = RA id 10 (50k players), Mario = RA id 11 (8k players).
    private static StubRaHandler SnesHandler() => new()
    {
        GameList = consoleId => consoleId == 3
            ? (System.Net.HttpStatusCode.OK,
               $"[{{\"ID\":10,\"Hashes\":[\"{ChronoMd5}\"]}},{{\"ID\":11,\"Hashes\":[\"{MarioMd5}\"]}}]")
            : (System.Net.HttpStatusCode.OK, "[]"),
        Extended = raId => (System.Net.HttpStatusCode.OK,
            $"{{\"ID\":{raId},\"NumDistinctPlayers\":{(raId == 10 ? 50000 : 8000)}}}"),
    };

    private RetroAchievementsSyncService NewService(StubRaHandler handler) =>
        new(_catalog, _queue, new RetroAchievementsClient(new StubRaFactory(handler), NullLogger<RetroAchievementsClient>.Instance),
            NullLogger<RetroAchievementsSyncService>.Instance);

    private async Task<long> AddGame(string name, string md5, double? igdbRating = null, int? igdbCount = null)
    {
        var gid = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name, igdb_rating, igdb_rating_count) VALUES ({_snes}, $n, $r, $c) RETURNING id",
            ("$n", name), ("$r", (object?)igdbRating ?? DBNull.Value), ("$c", (object?)igdbCount ?? DBNull.Value));
        await Exec($"INSERT INTO catalog_rom (game_id, name, size, md5) VALUES ({gid}, $n, 1, $m)",
            ("$n", name + ".sfc"), ("$m", md5));
        return gid;
    }

    private async Task<(long? Players, double? Score)> Rank(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT ra_players, rank_score FROM catalog_game WHERE id = {gameId}";
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.IsDBNull(0) ? null : r.GetInt64(0), r.IsDBNull(1) ? null : r.GetDouble(1));
    }

    private async Task<long> ScalarLong(string sql, params (string, object)[] ps)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task Exec(string sql, params (string, object)[] ps)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync();
    }
}
