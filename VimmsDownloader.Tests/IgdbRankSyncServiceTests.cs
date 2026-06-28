using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Catalog;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the real <see cref="IgdbRankSyncService"/> end-to-end against a temp SQLite DB (real
/// migrations) and the stub IGDB endpoints: it no-ops without Twitch creds, and with creds it pulls a
/// platform's rated games, joins them to the catalog by normalized name, and stores the rating + a
/// derived rank_score for matches. Also covers the Library "sort by rank" order (rank desc, unranked last).
/// </summary>
[TestClass]
public class IgdbRankSyncServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"igdb-rank-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance); // applies the real migrations (incl. 030)
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Sync_NoCreds_NoOps_WithoutEvenRequestingAToken()
    {
        var handler = new StubIgdbHandler();
        var n = await NewService(handler).SyncAsync(force: false, default);

        Assert.AreEqual(0, n);
        Assert.AreEqual(0, handler.TokenCalls);
    }

    [TestMethod]
    public async Task Sync_WithCreds_StoresRankForMatchedGamesOnly()
    {
        await SeedCreds();
        var snes = await ScalarLong(
            "INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
        var chrono = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name) VALUES ({snes}, 'Chrono Trigger (USA)') RETURNING id");
        var mario = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name) VALUES ({snes}, 'Super Mario World (USA)') RETURNING id");

        var handler = new StubIgdbHandler
        {
            // First page returns one matching rated game; subsequent pages are empty (stop).
            Games = body => body.Contains("offset 0;")
                ? (System.Net.HttpStatusCode.OK,
                   "[{\"id\":1,\"name\":\"Chrono Trigger\",\"total_rating\":94.5,\"total_rating_count\":1200}]")
                : (System.Net.HttpStatusCode.OK, "[]"),
        };

        var ranked = await NewService(handler).SyncAsync(force: false, default);

        Assert.AreEqual(1, ranked);                           // only Chrono matched
        var (rating, count, score) = await Rank(chrono);
        Assert.AreEqual(94.5, rating);
        Assert.AreEqual(1200, count);
        Assert.AreEqual(RankScore.Bayesian(94.5, 1200), score!.Value, 0.0001); // derived score stored
        Assert.IsNull((await Rank(mario)).Score);             // unmatched → unranked
        Assert.AreEqual(1, handler.TokenCalls);               // one token for the whole run
    }

    [TestMethod]
    public async Task Sync_Incremental_SkipsRankedConsole_UnlessForced()
    {
        await SeedCreds();
        var snes = await ScalarLong(
            "INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
        await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({snes}, 'Chrono Trigger (USA)') RETURNING id");

        var handler = new StubIgdbHandler
        {
            Games = body => body.Contains("offset 0;")
                ? (System.Net.HttpStatusCode.OK,
                   "[{\"id\":1,\"name\":\"Chrono Trigger\",\"total_rating\":94.5,\"total_rating_count\":1200}]")
                : (System.Net.HttpStatusCode.OK, "[]"),
        };
        var svc = NewService(handler);

        Assert.AreEqual(1, await svc.SyncAsync(force: false, default)); // first run ranks Chrono
        var afterFirst = handler.GamesCalls;
        Assert.IsGreaterThan(0, afterFirst);

        // Second incremental run: the console has no unranked games left → no IGDB query at all.
        Assert.AreEqual(0, await svc.SyncAsync(force: false, default));
        Assert.AreEqual(afterFirst, handler.GamesCalls);

        // Forcing re-pulls + re-ranks everything → the platform is queried again.
        Assert.AreEqual(1, await svc.SyncAsync(force: true, default));
        Assert.IsGreaterThan(afterFirst, handler.GamesCalls);
    }

    [TestMethod]
    public async Task Games_SortByRank_OrdersByScoreDesc_UnrankedLast()
    {
        var snes = await ScalarLong(
            "INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
        // Insert out of rank order to prove the ORDER BY (and an unranked game to prove it sorts last).
        await AddRankedGame(snes, "Mid Game", 70);
        await AddRankedGame(snes, "Best Game", 95);
        await AddRankedGame(snes, "Worst Game", 40);
        await AddRankedGame(snes, "Unranked Game", null);

        var (_, byRank) = await _catalog.GetGamesAsync("snes", null, "all", false, false, false,
            "substring", 0, 100, sort: "rank");
        CollectionAssert.AreEqual(
            new[] { "Best Game", "Mid Game", "Worst Game", "Unranked Game" },
            byRank.Select(g => g.Name).ToArray());

        // Default sort stays alphabetical and surfaces the score on the DTO.
        var (_, byName) = await _catalog.GetGamesAsync("snes", null, "all", false, false, false,
            "substring", 0, 100);
        CollectionAssert.AreEqual(
            new[] { "Best Game", "Mid Game", "Unranked Game", "Worst Game" },
            byName.Select(g => g.Name).ToArray());
        Assert.AreEqual(95, byName.Single(g => g.Name == "Best Game").RankScore);
        Assert.IsNull(byName.Single(g => g.Name == "Unranked Game").RankScore);
    }

    private IgdbRankSyncService NewService(StubIgdbHandler handler) =>
        new(_catalog, _queue, new IgdbClient(new StubIgdbFactory(handler), NullLogger<IgdbClient>.Instance),
            NullLogger<IgdbRankSyncService>.Instance);

    private async Task SeedCreds()
    {
        await _queue.SaveSettingAsync(SettingsKeys.IgdbClientId, "cid");
        await _queue.SaveSettingAsync(SettingsKeys.IgdbClientSecret, "secret");
    }

    private async Task AddRankedGame(long systemId, string name, double? score)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"INSERT INTO catalog_game (system_id, name, rank_score) VALUES ({systemId}, $n, $s)";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$s", (object?)score ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(double? Rating, long? Count, double? Score)> Rank(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT igdb_rating, igdb_rating_count, rank_score FROM catalog_game WHERE id = {gameId}";
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.IsDBNull(0) ? null : r.GetDouble(0),
                r.IsDBNull(1) ? null : r.GetInt64(1),
                r.IsDBNull(2) ? null : r.GetDouble(2));
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
