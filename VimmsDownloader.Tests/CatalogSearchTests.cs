using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL <see cref="CatalogRepository.GetGamesAsync"/> name-search modes (E3b) against a
/// temp SQLite file with the real migrations: substring (default), glob (<c>*</c>,<c>?</c>), and regex
/// (evaluated in C#). An invalid regex must degrade to an empty result, never throw; all modes stay
/// case-insensitive and honour the other filters (console scoping, paging).
/// </summary>
[TestClass]
public class CatalogSearchTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;
    private long _snes;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmsearch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);

        _snes = await Seed("snes");
        await AddGame(_snes, "Super Mario World (USA)");
        await AddGame(_snes, "Super Mario All-Stars (USA)");
        await AddGame(_snes, "Super Metroid (Japan, USA)");
        await AddGame(_snes, "Chrono Trigger (USA)");
        await AddGame(_snes, "Final Fantasy III (USA)");
        // A second console proves the regex path still honours the console filter.
        var ps = await Seed("psx");
        await AddGame(ps, "Final Fantasy VII (USA)");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Task<(int Total, List<CatalogGameDto> Games)> Search(string? q, string mode, string? console = null, int page = 0, int pageSize = 100) =>
        _repo.GetGamesAsync(console, q, "all", false, false, false, mode, page, pageSize);

    [TestMethod]
    public async Task Substring_MatchesAnywhere_CaseInsensitive()
    {
        var (total, games) = await Search("mario", "substring");
        Assert.AreEqual(2, total);
        Assert.IsTrue(games.All(g => g.Name.Contains("Mario")));
    }

    [TestMethod]
    public async Task Glob_StarMatchesPrefix()
    {
        var (total, _) = await Search("Super M*", "glob");
        Assert.AreEqual(3, total); // Mario World, Mario All-Stars, Metroid
    }

    [TestMethod]
    public async Task Glob_QuestionMarkMatchesSingleChar()
    {
        var (total, games) = await Search("Chr?no*", "glob"); // Chr_no% → "Chrono ..."
        Assert.AreEqual(1, total);
        Assert.AreEqual("Chrono Trigger (USA)", games[0].Name);
    }

    [TestMethod]
    public async Task Glob_EscapesLiteralPercent_NotAWildcard()
    {
        await AddGame(_snes, "100% Orange (USA)");
        var (total, games) = await Search("100%*", "glob"); // '%' is a literal here, '*' the wildcard
        Assert.AreEqual(1, total);
        Assert.AreEqual("100% Orange (USA)", games[0].Name);
    }

    [TestMethod]
    public async Task Regex_AnchorsAndAlternation()
    {
        var (total, games) = await Search("^Super M(ario|etroid)", "regex");
        Assert.AreEqual(3, total);
        Assert.IsTrue(games.All(g => g.Name.StartsWith("Super M")));
    }

    [TestMethod]
    public async Task Regex_CaseInsensitive()
    {
        var (total, _) = await Search("final fantasy", "regex");
        Assert.AreEqual(2, total); // FF III (snes) + FF VII (psx), no console filter
    }

    [TestMethod]
    public async Task Regex_RespectsConsoleFilter()
    {
        var (total, games) = await Search("Final Fantasy", "regex", console: "snes");
        Assert.AreEqual(1, total);
        Assert.AreEqual("Final Fantasy III (USA)", games[0].Name);
    }

    [TestMethod]
    public async Task Regex_InvalidPattern_ReturnsEmpty_DoesNotThrow()
    {
        var (total, games) = await Search("(unclosed", "regex");
        Assert.AreEqual(0, total);
        Assert.IsEmpty(games);
    }

    [TestMethod]
    public async Task Regex_PagesTheMatchedSet()
    {
        // "USA" matches all five snes titles; page size 2 → 2 on the first page, total still 5.
        var (total, page0) = await Search("USA", "regex", console: "snes", page: 0, pageSize: 2);
        Assert.AreEqual(5, total);
        Assert.HasCount(2, page0);
        var (_, page2) = await Search("USA", "regex", console: "snes", page: 2, pageSize: 2);
        Assert.HasCount(1, page2); // 5 = 2 + 2 + 1
    }

    [TestMethod]
    public async Task UnknownMode_FallsBackToSubstring()
    {
        var (total, _) = await Search("mario", "bogus");
        Assert.AreEqual(2, total);
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
