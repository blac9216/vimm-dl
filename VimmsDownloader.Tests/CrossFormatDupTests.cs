using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Phase C / C4: cross-format / cross-source duplicate detection. Exercises the REAL
/// <see cref="QueueRepository.CheckGameDuplicatesAsync"/> against a temp DB — queuing one format of a
/// catalog game when another format/source is already present surfaces a cross-format match keyed on
/// the catalog game_id (the URL-only check can't see it, since a Vimm game's vault URL is identical
/// across formats). The exact (same URL + same format) pair stays the URL check's job and is excluded.
/// </summary>
[TestClass]
public class CrossFormatDupTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private QueueRepository _queue = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"crossfmt-{Guid.NewGuid():N}");
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
    public async Task SameGameDifferentFormat_IsCrossFormatMatch()
    {
        await SeedGame(vaultId: 1001);
        await CompleteVimm("https://vimm.net/vault/1001", format: 0, "Game.7z");

        // Queuing format 1 of the same vault game → cross-format warning naming the existing format 0.
        var matches = await _queue.CheckGameDuplicatesAsync(["https://vimm.net/vault/1001"], incomingFormat: 1);

        Assert.HasCount(1, matches);
        Assert.IsTrue(matches[0].CrossFormat);
        Assert.AreEqual(0, matches[0].Format);
    }

    [TestMethod]
    public async Task SameGameSameFormat_NotReported()
    {
        await SeedGame(vaultId: 1001);
        await CompleteVimm("https://vimm.net/vault/1001", format: 0, "Game.7z");

        // Same URL + same format is the exact-duplicate URL check's job — excluded here.
        var matches = await _queue.CheckGameDuplicatesAsync(["https://vimm.net/vault/1001"], incomingFormat: 0);

        Assert.IsEmpty(matches);
    }

    [TestMethod]
    public async Task SameGameDifferentSource_DetectedByGameId()
    {
        var gameId = await SeedGame(vaultId: 1001);
        // An archive completion of the same catalog game (different URL/source), game_id stamped.
        await InsertCompletedRow("https://archive.org/download/x", "archive", "arc-x", format: 0, "Game.iso", gameId);

        // Queue the game from Vimm (format 1) → the archive copy surfaces as a cross-source match.
        var matches = await _queue.CheckGameDuplicatesAsync(["https://vimm.net/vault/1001"], incomingFormat: 1);

        Assert.HasCount(1, matches);
        Assert.AreEqual("https://archive.org/download/x", matches[0].Url);
        Assert.IsTrue(matches[0].CrossFormat);
    }

    [TestMethod]
    public async Task UnresolvableUrl_NoCrossFormat()
    {
        await SeedGame(vaultId: 1001);
        await CompleteVimm("https://vimm.net/vault/1001", format: 0, "Game.7z");

        // A URL that doesn't resolve to a catalog game yields nothing (URL check still applies).
        Assert.IsEmpty(await _queue.CheckGameDuplicatesAsync(["https://example.com/unknown"], incomingFormat: 1));
        Assert.IsEmpty(await _queue.CheckGameDuplicatesAsync([], incomingFormat: 1));
    }

    [TestMethod]
    public async Task UrlCheck_NowCarriesFormat()
    {
        await SeedGame(vaultId: 1001);
        await CompleteVimm("https://vimm.net/vault/1001", format: 2, "Game.dec.iso");

        var matches = await _queue.CheckDuplicatesAsync(["https://vimm.net/vault/1001"]);
        Assert.HasCount(1, matches);
        Assert.AreEqual(2, matches[0].Format); // format surfaced so the endpoint can compare incoming vs existing
    }

    // --- helpers ---

    private async Task<long> SeedGame(long vaultId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await Exec(db, "INSERT OR IGNORE INTO catalog_system (dat_name, console, source) VALUES ('DAT-ps3', 'ps3', 'redump')");
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO catalog_game (system_id, name, vault_id) VALUES " +
                          "((SELECT id FROM catalog_system WHERE console='ps3'), 'Bound Game', $v); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$v", vaultId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task CompleteVimm(string url, int format, string filename)
    {
        await _queue.AddToQueueAsync(url, format);
        // GetNext returns the first queued item; complete it (CompleteItemAsync stamps game_id for Vimm).
        var next = (await _queue.GetNextQueueItemAsync())!.Value;
        await _queue.CompleteItemAsync(next.Id, next.Url, filename,
            Path.Combine(_dir, "downloads", "completed", filename), format);
    }

    private async Task InsertCompletedRow(string url, string source, string sourceId, int format, string filename, long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO completed_urls (url, filename, filepath, completed_at, format, source, source_id, game_id)
            VALUES ($url, $fn, $fp, datetime('now'), $fmt, $src, $sid, $g)
        """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$fn", filename);
        cmd.Parameters.AddWithValue("$fp", Path.Combine(_dir, "downloads", "completed", filename));
        cmd.Parameters.AddWithValue("$fmt", format);
        cmd.Parameters.AddWithValue("$src", source);
        cmd.Parameters.AddWithValue("$sid", sourceId);
        cmd.Parameters.AddWithValue("$g", gameId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task Exec(SqliteConnection db, string sql)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
