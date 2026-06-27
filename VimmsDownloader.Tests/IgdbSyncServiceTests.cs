using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the real <see cref="IgdbSyncService"/> end-to-end against a temp SQLite DB (real
/// migrations) and the stub IGDB endpoints: it no-ops without Twitch creds, and with creds it pulls a
/// platform's games, joins them to the catalog by normalized name, and stores descriptions for matches.
/// </summary>
[TestClass]
public class IgdbSyncServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"igdb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance); // applies the real migrations (incl. 029)
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
        var n = await NewService(handler).SyncAsync(default);

        Assert.AreEqual(0, n);
        Assert.AreEqual(0, handler.TokenCalls);
    }

    [TestMethod]
    public async Task Sync_WithCreds_StoresDescriptionsForMatchedGamesOnly()
    {
        await _queue.SaveSettingAsync(SettingsKeys.IgdbClientId, "cid");
        await _queue.SaveSettingAsync(SettingsKeys.IgdbClientSecret, "secret");
        var snes = await ScalarLong(
            "INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
        var chrono = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name) VALUES ({snes}, 'Chrono Trigger (USA)') RETURNING id");
        var mario = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name) VALUES ({snes}, 'Super Mario World (USA)') RETURNING id");

        var handler = new StubIgdbHandler
        {
            // First page of a platform returns one matching game; subsequent pages are empty (stop).
            Games = body => body.Contains("offset 0;")
                ? (System.Net.HttpStatusCode.OK,
                   "[{\"id\":1,\"name\":\"Chrono Trigger\",\"summary\":\"A time-travel RPG.\"}]")
                : (System.Net.HttpStatusCode.OK, "[]"),
        };

        var matched = await NewService(handler).SyncAsync(default);

        Assert.AreEqual(1, matched);                              // only Chrono matched
        Assert.AreEqual("A time-travel RPG.", await Description(chrono));
        Assert.IsNull(await Description(mario));                  // unmatched → no description
        Assert.AreEqual(1, handler.TokenCalls);                   // one token for the whole run
    }

    private IgdbSyncService NewService(StubIgdbHandler handler) =>
        new(_catalog, _queue, new IgdbClient(new StubIgdbFactory(handler), NullLogger<IgdbClient>.Instance),
            NullLogger<IgdbSyncService>.Instance);

    private async Task<string?> Description(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT description FROM catalog_game WHERE id = {gameId}";
        var v = await cmd.ExecuteScalarAsync();
        return v == DBNull.Value ? null : (string?)v;
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
