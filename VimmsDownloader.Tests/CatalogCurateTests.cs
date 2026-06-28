using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the real <see cref="CatalogRepository.SelectBestWithinBudgetAsync"/> (epic #123 / R3): the
/// "best N up to X GB" curation selector. Verifies rank-ordered greedy accumulation within a byte
/// budget, the skip-doesn't-fit behaviour, the maxCount cap, owned-exclusion, the console filter, and
/// the empty-budget guard — against a temp SQLite DB with the real migrations (incl. 030's rank_score).
/// </summary>
[TestClass]
public class CatalogCurateTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _catalog = null!;
    private long _snes;
    private long _ps3;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"curate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        var queue = new QueueRepository();
        await queue.InitAsync(_connStr, NullLogger.Instance); // real migrations (incl. 030 rank_score)
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);

        _snes = await ScalarLong("INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
        _ps3 = await ScalarLong("INSERT INTO catalog_system (dat_name, console, source) VALUES ('PS3 DAT', 'ps3', 'redump') RETURNING id");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task SelectBest_AccumulatesTopRanked_SkippingOnesThatDontFit()
    {
        var best = await AddGame(_snes, "Best", rank: 95, size: 10);
        await AddGame(_snes, "Big", rank: 90, size: 100);   // ranks high but won't fit → skipped
        var mid = await AddGame(_snes, "Mid", rank: 80, size: 10);
        var small = await AddGame(_snes, "Small", rank: 70, size: 5);
        await AddGame(_snes, "Unranked", rank: null, size: 1); // unranked sorts last

        var (ids, total) = await Select(budgetBytes: 25);

        // Greedy in rank order: Best(10) ✓, Big(100) ✗ skip, Mid(10) ✓, Small(5) ✓ → 25 exactly; Unranked(1) ✗.
        CollectionAssert.AreEqual(new[] { (int)best, (int)mid, (int)small }, ids.ToArray());
        Assert.AreEqual(25, total);
    }

    [TestMethod]
    public async Task SelectBest_MaxCount_CapsTheSelectionToTopRanked()
    {
        var best = await AddGame(_snes, "Best", rank: 95, size: 10);
        var big = await AddGame(_snes, "Big", rank: 90, size: 100);
        await AddGame(_snes, "Mid", rank: 80, size: 10);

        var (ids, total) = await Select(budgetBytes: 10_000, maxCount: 2);

        CollectionAssert.AreEqual(new[] { (int)best, (int)big }, ids.ToArray()); // top 2 by rank
        Assert.AreEqual(110, total);
    }

    [TestMethod]
    public async Task SelectBest_ExcludesOwnedGames()
    {
        await AddGame(_snes, "Owned Gem", rank: 99, size: 10, owned: true); // highest rank but owned
        var free = await AddGame(_snes, "Free", rank: 50, size: 10);

        var (ids, _) = await Select(budgetBytes: 1_000);

        CollectionAssert.AreEqual(new[] { (int)free }, ids.ToArray()); // owned never selected
    }

    [TestMethod]
    public async Task SelectBest_RespectsConsoleFilter()
    {
        await AddGame(_ps3, "PS3 Hit", rank: 99, size: 10);   // different console
        var snesGame = await AddGame(_snes, "SNES Hit", rank: 50, size: 10);

        var (ids, _) = await Select(console: "snes", budgetBytes: 1_000);

        CollectionAssert.AreEqual(new[] { (int)snesGame }, ids.ToArray());
    }

    [TestMethod]
    public async Task SelectBest_NonPositiveBudget_ReturnsEmpty()
    {
        await AddGame(_snes, "Anything", rank: 90, size: 10);
        var (ids, total) = await Select(budgetBytes: 0);
        Assert.IsEmpty(ids);
        Assert.AreEqual(0, total);
    }

    private Task<(List<int> Ids, long TotalBytes)> Select(string? console = null, long budgetBytes = 0, int maxCount = 0) =>
        _catalog.SelectBestWithinBudgetAsync(console, query: null, dedupe: false, english: false,
            excludeCategories: false, searchMode: "substring", emulator: null, compatStatus: null,
            budgetBytes, maxCount);

    private async Task<long> AddGame(long systemId, string name, double? rank, long size, bool owned = false)
    {
        var gid = await ScalarLong(
            $"INSERT INTO catalog_game (system_id, name, rank_score) VALUES ({systemId}, $n, $r) RETURNING id",
            ("$n", name), ("$r", (object?)rank ?? DBNull.Value));
        await Exec($"INSERT INTO catalog_rom (game_id, name, size) VALUES ({gid}, $n, {size})", ("$n", name + ".rom"));
        if (owned)
            await Exec($"INSERT INTO catalog_owned (game_id, filepath) VALUES ({gid}, $p)", ("$p", "/dl/" + name));
        return gid;
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
