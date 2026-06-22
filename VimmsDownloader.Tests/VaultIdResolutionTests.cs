using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// #144: vault_id → game_id resolution must be deterministic. vault_id is indexed but NOT unique, so if
/// one Vimm vault entry ever binds to multiple catalog games, completing a Vimm download (which resolves
/// the catalog game via <see cref="QueueRepository"/>.ResolveGameIdAsync) must still pick a single,
/// predictable game — the 1G1R parent, then the lowest id — matching migration 022's backfill. Exercises
/// the REAL repository against a temp DB.
/// </summary>
[TestClass]
public class VaultIdResolutionTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private QueueRepository _queue = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vaultres-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task CompleteItem_PicksParentGame_WhenVaultIdBindsMultiple()
    {
        // Two catalog games share vault_id 2002. The parent is inserted SECOND (higher id), so a naive
        // "LIMIT 1" by rowid would wrongly pick the non-parent — this proves the is_parent tie-break.
        await SeedSystem();
        var child = await AddGame("Game (USA)", vaultId: 2002, isParent: 0);
        var parent = await AddGame("Game (World)", vaultId: 2002, isParent: 1);
        Assert.IsGreaterThan(child, parent, "parent must have the higher id for this test to be meaningful");

        Assert.AreEqual(parent, await CompleteVimmAndGetGameId("https://vimm.net/vault/2002"));
    }

    [TestMethod]
    public async Task CompleteItem_PicksLowestId_WhenNoParentAmongBindings()
    {
        // No parent flagged on either binding → fall back to the lowest id, deterministically.
        await SeedSystem();
        var first = await AddGame("Game (USA)", vaultId: 2003, isParent: 0);
        await AddGame("Game (Europe)", vaultId: 2003, isParent: 0);

        Assert.AreEqual(first, await CompleteVimmAndGetGameId("https://vimm.net/vault/2003"));
    }

    // --- helpers ---

    private async Task<long> CompleteVimmAndGetGameId(string url)
    {
        await _queue.AddToQueueAsync(url, 0);
        var next = (await _queue.GetNextQueueItemAsync())!.Value;
        await _queue.CompleteItemAsync(next.Id, next.Url, "Game.7z",
            Path.Combine(_dir, "downloads", "completed", "Game.7z"), 0);

        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT game_id FROM completed_urls WHERE url = $u";
        cmd.Parameters.AddWithValue("$u", url);
        var v = await cmd.ExecuteScalarAsync();
        return v is null || v == DBNull.Value ? 0 : Convert.ToInt64(v);
    }

    private async Task SeedSystem()
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO catalog_system (dat_name, console, source) VALUES ('DAT-ps3', 'ps3', 'redump')";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> AddGame(string name, long vaultId, int isParent)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO catalog_game (system_id, name, vault_id, is_parent) VALUES " +
                          "((SELECT id FROM catalog_system WHERE console = 'ps3'), $n, $v, $p); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$v", vaultId);
        cmd.Parameters.AddWithValue("$p", isParent);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
