using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL <see cref="CatalogRepository.GetGamesAsync"/> hidden-console filter (#311) — the
/// Library Settings "hide this console" preference — against a temp SQLite file with the real
/// migrations. Hidden consoles disappear from the unfiltered ("All consoles") browse and its total,
/// but an explicit <c>console</c> filter still returns them (the API stays honest even though nothing
/// in the UI produces such a request), and an empty/absent hidden set changes nothing.
/// </summary>
[TestClass]
public class CatalogHiddenConsolesTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmhidden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);

        var snes = await Seed("snes");
        await AddGame(snes, "Super Mario World (USA)");
        await AddGame(snes, "Chrono Trigger (USA)");
        var ps3 = await Seed("ps3");
        await AddGame(ps3, "Heavy Title (USA)");
        var n64 = await Seed("n64");
        await AddGame(n64, "GoldenEye 007 (USA)");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Task<(int Total, List<CatalogGameDto> Games)> Games(string? console, IReadOnlyCollection<string>? hidden = null) =>
        _repo.GetGamesAsync(console, null, "all", false, false, false, "substring", 0, 100, hiddenConsoles: hidden);

    [TestMethod]
    public async Task NoHiddenSetting_UnfilteredBrowse_ReturnsEverything()
    {
        var (total, games) = await Games(null);
        Assert.AreEqual(4, total);
        Assert.HasCount(4, games);
    }

    [TestMethod]
    public async Task EmptyHiddenList_BehavesLikeNoSetting()
    {
        var (total, games) = await Games(null, []);
        Assert.AreEqual(4, total);
        Assert.HasCount(4, games);
    }

    [TestMethod]
    public async Task HiddenConsole_AbsentFromUnfilteredBrowse_AndItsCount()
    {
        var (total, games) = await Games(null, ["ps3"]);
        Assert.AreEqual(3, total);
        Assert.IsFalse(games.Any(g => g.Console == "ps3"));
        Assert.IsTrue(games.Any(g => g.Console == "snes"));
        Assert.IsTrue(games.Any(g => g.Console == "n64"));
    }

    [TestMethod]
    public async Task MultipleHiddenConsoles_AllExcluded()
    {
        var (total, games) = await Games(null, ["ps3", "n64"]);
        Assert.AreEqual(2, total);
        Assert.IsTrue(games.All(g => g.Console == "snes"));
    }

    [TestMethod]
    public async Task HiddenConsole_IsCaseInsensitive()
    {
        var (total, _) = await Games(null, ["PS3"]);
        Assert.AreEqual(3, total);
    }

    [TestMethod]
    public async Task ExplicitConsoleFilter_HonoredEvenWhenHidden()
    {
        // Nothing in the UI produces this request, but the API stays honest: an explicit ?console=
        // always wins over the hidden-console browse preference.
        var (total, games) = await Games("ps3", ["ps3"]);
        Assert.AreEqual(1, total);
        Assert.AreEqual("ps3", games[0].Console);
    }

    [TestMethod]
    public async Task ExplicitConsoleFilter_ForNonHiddenConsole_Unaffected()
    {
        var (total, games) = await Games("snes", ["ps3"]);
        Assert.AreEqual(2, total);
        Assert.IsTrue(games.All(g => g.Console == "snes"));
    }

    // --- seeding helpers (direct SQL) ---

    private async Task<long> Seed(string console) =>
        await ScalarLong($"INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT {console}', '{console}', 'no-intro') RETURNING id");

    private async Task<long> AddGame(long systemId, string name) =>
        await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({systemId}, '{name.Replace("'", "''")}') RETURNING id");

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
