using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Archive ingest (epic #118 / L3). Exercises the REAL <see cref="ImportService"/> archive path with a
/// <see cref="FakeArchiveExtractor"/> (no 7z): a .zip/.7z is extracted to a temp dir, each inner file
/// imported by its own hash (match → completed/{console}/, miss → rejected/), the archive wrapper discarded,
/// and the temp dir cleaned up. Extraction failures preserve the archive in rejected/. "hello" hashes to a
/// known SHA1 (aaf4c6…).
/// </summary>
[TestClass]
public class ImportServiceArchiveTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private string _downloads = null!;
    private string _importDir = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;
    private FakeArchiveExtractor _extractor = null!;
    private ImportService _import = null!;

    private const string HelloSha1 = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"archimport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";

        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance);
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
        _extractor = new FakeArchiveExtractor();
        _import = new ImportService(_catalog, _queue, _extractor, NullLogger<ImportService>.Instance);

        _downloads = _queue.GetDownloadPath();
        _importDir = Path.Combine(_downloads, "import");
        Directory.CreateDirectory(_importDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Archive_MatchingInnerRom_PlacedAndOwned_ArchiveDiscarded()
    {
        var game = await SeedRom("snes", "Cat (USA).sfc", sha1: HelloSha1);
        var archive = StageArchive("game.zip");
        _extractor.Contains("game.zip", ("game.sfc", "hello"));

        var tempBefore = TempImportDirs();
        var results = await _import.ImportFileAsync(archive, default);

        var r = results.Single();
        Assert.AreEqual(ImportOutcome.Matched, r.Outcome);
        Assert.AreEqual(game, r.GameId);
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "completed", "snes", "game.sfc")), "inner ROM placed by hash");
        Assert.IsFalse(File.Exists(archive), "the archive wrapper is discarded after extraction");
        Assert.AreEqual(tempBefore, TempImportDirs(), "the extraction temp dir is cleaned up");
        Assert.IsTrue(await IsOwned(game));
    }

    [TestMethod]
    public async Task Archive_NonMatchingInner_Rejected_ArchiveDiscarded()
    {
        var archive = StageArchive("junk.7z");
        _extractor.Contains("junk.7z", ("readme.txt", "world"));

        var results = await _import.ImportFileAsync(archive, default);

        Assert.AreEqual(ImportOutcome.Rejected, results.Single().Outcome);
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "rejected", "readme.txt")), "non-matching contents go to rejected/");
        Assert.IsFalse(File.Exists(archive));
    }

    [TestMethod]
    public async Task Archive_MultipleInner_MatchedPerHash()
    {
        var game = await SeedRom("ps3", "Cat (USA).bin", sha1: HelloSha1);
        var archive = StageArchive("disc.zip");
        _extractor.Contains("disc.zip", ("track.bin", "hello"), ("track.cue", "world"));

        var results = await _import.ImportFileAsync(archive, default);

        Assert.HasCount(2, results);
        Assert.AreEqual(1, results.Count(r => r.Outcome == ImportOutcome.Matched));
        Assert.AreEqual(1, results.Count(r => r.Outcome == ImportOutcome.Rejected));
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "completed", "ps3", "track.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "rejected", "track.cue")));
        Assert.AreEqual(game, results.Single(r => r.Outcome == ImportOutcome.Matched).GameId);
    }

    [TestMethod]
    public async Task Archive_ExtractFails_ArchivePreservedInRejected()
    {
        var archive = StageArchive("corrupt.7z");
        _extractor.Fails("corrupt.7z");

        var tempBefore = TempImportDirs();
        var results = await _import.ImportFileAsync(archive, default);

        var r = results.Single();
        Assert.AreEqual(ImportOutcome.Rejected, r.Outcome);
        StringAssert.Contains(r.Reason, "extract failed");
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "rejected", "corrupt.7z")), "a failed archive is kept, not lost");
        Assert.IsFalse(File.Exists(archive));
        Assert.AreEqual(tempBefore, TempImportDirs(), "temp dir is cleaned up even on failure");
    }

    [TestMethod]
    public async Task Archive_Empty_GoesToRejected()
    {
        var archive = StageArchive("empty.zip"); // configured with no inner files
        _extractor.Contains("empty.zip"); // extracts cleanly to nothing

        var results = await _import.ImportFileAsync(archive, default);

        Assert.AreEqual(ImportOutcome.Rejected, results.Single().Outcome);
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "rejected", "empty.zip")));
    }

    // --- helpers ---

    private static int TempImportDirs() => Directory.GetDirectories(Path.GetTempPath(), "vimm-import-*").Length;

    private string StageArchive(string name)
    {
        // The archive must exist on disk (it's deleted/moved afterwards); the fake ignores its bytes.
        var p = Path.Combine(_importDir, name);
        File.WriteAllBytes(p, [0x50, 0x4B]); // arbitrary placeholder bytes
        return p;
    }

    private async Task<long> SeedRom(string console, string romName, string? sha1)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        long sysId;
        await using (var s = db.CreateCommand())
        {
            s.CommandText = "INSERT OR IGNORE INTO catalog_system (dat_name, console, source) VALUES ($d, $c, 'redump'); " +
                            "SELECT id FROM catalog_system WHERE console = $c LIMIT 1;";
            s.Parameters.AddWithValue("$d", $"DAT-{console}");
            s.Parameters.AddWithValue("$c", console);
            sysId = Convert.ToInt64(await s.ExecuteScalarAsync());
        }
        long gameId;
        await using (var g = db.CreateCommand())
        {
            g.CommandText = "INSERT INTO catalog_game (system_id, name) VALUES ($s, $n); SELECT last_insert_rowid();";
            g.Parameters.AddWithValue("$s", sysId);
            g.Parameters.AddWithValue("$n", $"{console} Game {Guid.NewGuid():N}");
            gameId = Convert.ToInt64(await g.ExecuteScalarAsync());
        }
        await using (var r = db.CreateCommand())
        {
            r.CommandText = "INSERT INTO catalog_rom (game_id, name, size, crc, md5, sha1) VALUES ($g, $n, 0, NULL, NULL, $sha1)";
            r.Parameters.AddWithValue("$g", gameId);
            r.Parameters.AddWithValue("$n", romName);
            r.Parameters.AddWithValue("$sha1", (object?)sha1 ?? DBNull.Value);
            await r.ExecuteNonQueryAsync();
        }
        return gameId;
    }

    private async Task<bool> IsOwned(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM catalog_owned WHERE game_id = $g)";
        cmd.Parameters.AddWithValue("$g", gameId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }
}
