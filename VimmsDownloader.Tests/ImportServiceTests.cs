using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Local catalog import (epic #118 / L1). Exercises the REAL <see cref="ImportService"/> +
/// <see cref="CatalogRepository"/> against a temp DB and real files: a file whose SHA1/MD5/CRC32 matches
/// a <c>catalog_rom</c> is moved into <c>completed/{console}/</c> and marked owned (name-independent,
/// case-insensitive); a non-match is moved to <c>rejected/</c> and never deleted; a headered file matches
/// on its headerless bytes. "hello" has known hashes (sha1 aaf4c6…, md5 5d41402a…, crc 3610A686).
/// </summary>
[TestClass]
public class ImportServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private string _downloads = null!;
    private string _importDir = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;
    private ImportService _import = null!;

    private const string HelloSha1 = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";
    private const string HelloMd5 = "5d41402abc4b2a76b9719d911017c592";
    private const string HelloCrc = "3610A686";

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";

        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance); // runs migrations + derives the download path
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
        _import = new ImportService(_catalog, _queue, new FakeArchiveExtractor(), NullLogger<ImportService>.Instance);

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
    public async Task Match_BySha1_PlacesInConsoleFolder_AndMarksOwned()
    {
        var game = await SeedRom("snes", "Real Catalog Name (USA).sfc", sha1: HelloSha1.ToUpperInvariant(), md5: null, crc: null);
        var src = Stage("totally-different-name.sfc", "hello");

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Matched, result.Outcome);
        Assert.AreEqual("sha1", result.MatchKind);
        Assert.AreEqual(game, result.GameId);
        // Placed into the MATCHED game's console folder, original name preserved.
        var dest = Path.Combine(_downloads, "completed", "snes", "totally-different-name.sfc");
        Assert.AreEqual(dest, result.DestPath);
        Assert.IsTrue(File.Exists(dest), "file should be in completed/snes/");
        Assert.IsFalse(File.Exists(src), "source file should have been moved, not copied");

        var owned = await OwnedRow(game);
        Assert.AreEqual((1, "sha1", dest), owned);
    }

    [TestMethod]
    public async Task Match_FallsBackToMd5_WhenNoSha1()
    {
        var game = await SeedRom("nes", "A.nes", sha1: null, md5: HelloMd5, crc: null);
        Assert.AreEqual("md5", (await ImportOne(Stage("a.nes", "hello"))).MatchKind);
        Assert.AreEqual((1, "md5"), await OwnedFlag(game));
    }

    [TestMethod]
    public async Task Match_FallsBackToCrc_WhenNoSha1OrMd5()
    {
        var game = await SeedRom("gba", "B.gba", sha1: null, md5: null, crc: HelloCrc);
        Assert.AreEqual("crc", (await ImportOne(Stage("b.gba", "hello"))).MatchKind);
        Assert.AreEqual((1, "crc"), await OwnedFlag(game));
    }

    [TestMethod]
    public async Task NonMatch_MovesToRejected_NothingDeleted_NotOwned()
    {
        var game = await SeedRom("ps3", "Game.iso", sha1: HelloSha1, md5: null, crc: null);
        var src = Stage("mystery.iso", "world"); // different content → different hashes

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Rejected, result.Outcome);
        Assert.AreEqual("no catalog hash match", result.Reason);
        var rejected = Path.Combine(_downloads, "rejected", "mystery.iso");
        Assert.IsTrue(File.Exists(rejected), "rejected file should be kept under rejected/");
        Assert.AreEqual("world", await File.ReadAllTextAsync(rejected)); // contents intact, nothing deleted
        Assert.IsFalse(File.Exists(src));
        Assert.IsFalse(await IsOwned(game));
    }

    [TestMethod]
    public async Task NonMatch_HonorsExplicitRejectedRoot()
    {
        await SeedRom("ps3", "Game.iso", sha1: HelloSha1, md5: null, crc: null);
        var src = Stage("nope.iso", "world");
        var customRejected = Path.Combine(_dir, "custom-reject");

        var result = await ImportOne(src, customRejected);

        Assert.AreEqual(ImportOutcome.Rejected, result.Outcome);
        Assert.IsTrue(File.Exists(Path.Combine(customRejected, "nope.iso")));
    }

    [TestMethod]
    public async Task HeaderedFile_MatchesOnHeaderlessBytes()
    {
        // catalog stores the hash of the headerless ROM ("hello"); the on-disk file is a 16-byte iNES
        // header followed by "hello", so it only matches after the header is stripped.
        var game = await SeedRom("nes", "Headerless (USA).nes", sha1: HelloSha1, md5: null, crc: null);
        var headered = INesHeader().Concat("hello"u8.ToArray()).ToArray();
        var src = StageBytes("headered.nes", headered);

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Matched, result.Outcome);
        Assert.AreEqual(game, result.GameId);
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "completed", "nes", "headered.nes")));
    }

    [TestMethod]
    public async Task HeaderedFile_StillRejected_WhenNeitherVariantMatches()
    {
        await SeedRom("nes", "Other.nes", sha1: HelloSha1, md5: null, crc: null);
        // iNES header + "world" — neither the full bytes nor the stripped "world" match "hello".
        var src = StageBytes("nomatch.nes", INesHeader().Concat("world"u8.ToArray()).ToArray());

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Rejected, result.Outcome);
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "rejected", "nomatch.nes")));
    }

    [TestMethod]
    public async Task Reimport_UpsertsOwnedRow()
    {
        var game = await SeedRom("gb", "Tetris.gb", sha1: HelloSha1, md5: null, crc: null);
        Assert.AreEqual(ImportOutcome.Matched, (await ImportOne(Stage("t1.gb", "hello"))).Outcome);
        // A second copy under a different name re-matches the same game and refreshes its owned filepath.
        var second = await ImportOne(Stage("t2.gb", "hello"));

        Assert.AreEqual(ImportOutcome.Matched, second.Outcome);
        var owned = await OwnedRow(game);
        Assert.AreEqual(Path.Combine(_downloads, "completed", "gb", "t2.gb"), owned.Filepath);
    }

    // --- defensive error paths (#176) ---
    // Forced with a directory at the target path, which makes File.OpenRead / File.Move throw on every
    // platform — and, unlike chmod, still fails for root (the test container runs as root).

    [TestMethod]
    public async Task UnreadableSource_IsRejected_WithReason()
    {
        // A directory can't be opened as a file, so hashing fails before any match is attempted.
        var src = Path.Combine(_importDir, "broken.iso");
        Directory.CreateDirectory(src);

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Rejected, result.Outcome);
        Assert.AreEqual("unreadable (could not hash)", result.Reason);
    }

    [TestMethod]
    public async Task MatchedFile_MoveIntoCompletedFails_RejectedAndNotOwned()
    {
        var game = await SeedRom("snes", "Game (USA).sfc", sha1: HelloSha1, md5: null, crc: null);
        var src = Stage("game.sfc", "hello");
        // Occupy completed/snes/game.sfc with a directory so the placement move fails after the match.
        Directory.CreateDirectory(Path.Combine(_downloads, "completed", "snes", "game.sfc"));

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Rejected, result.Outcome);
        StringAssert.Contains(result.Reason, "move failed");
        Assert.IsFalse(await IsOwned(game), "a failed placement must not mark the game owned");
    }

    [TestMethod]
    public async Task NonMatch_RejectMoveFails_StillRejected_WithNullDest()
    {
        await SeedRom("ps3", "Other.iso", sha1: HelloSha1, md5: null, crc: null);
        var src = Stage("mystery.iso", "world"); // no catalog match → headed for rejected/
        // Occupy rejected/mystery.iso with a directory so even the reject move fails.
        Directory.CreateDirectory(Path.Combine(_downloads, "rejected", "mystery.iso"));

        var result = await ImportOne(src);

        Assert.AreEqual(ImportOutcome.Rejected, result.Outcome);
        Assert.AreEqual("no catalog hash match", result.Reason);
        Assert.IsNull(result.DestPath, "when the reject move fails the result carries no dest path");
    }

    // --- helpers ---

    // A raw file yields exactly one result; unwrap it so these (raw-only) assertions stay readable.
    private async Task<ImportResult> ImportOne(string path) => (await _import.ImportFileAsync(path, default)).Single();
    private async Task<ImportResult> ImportOne(string path, string rejectedRoot) => (await _import.ImportFileAsync(path, rejectedRoot, default)).Single();

    private static byte[] INesHeader()
    {
        var h = new byte[16];
        h[0] = (byte)'N'; h[1] = (byte)'E'; h[2] = (byte)'S'; h[3] = 0x1A;
        return h;
    }

    private string Stage(string name, string content)
    {
        var p = Path.Combine(_importDir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private string StageBytes(string name, byte[] content)
    {
        var p = Path.Combine(_importDir, name);
        File.WriteAllBytes(p, content);
        return p;
    }

    private async Task<long> SeedRom(string console, string romName, string? sha1, string? md5, string? crc)
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
            r.CommandText = "INSERT INTO catalog_rom (game_id, name, size, crc, md5, sha1) VALUES ($g, $n, 0, $crc, $md5, $sha1)";
            r.Parameters.AddWithValue("$g", gameId);
            r.Parameters.AddWithValue("$n", romName);
            r.Parameters.AddWithValue("$crc", (object?)crc ?? DBNull.Value);
            r.Parameters.AddWithValue("$md5", (object?)md5 ?? DBNull.Value);
            r.Parameters.AddWithValue("$sha1", (object?)sha1 ?? DBNull.Value);
            await r.ExecuteNonQueryAsync();
        }
        return gameId;
    }

    private async Task<(int Verified, string? Hash, string Filepath)> OwnedRow(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT verified, verified_hash, filepath FROM catalog_owned WHERE game_id = $g";
        cmd.Parameters.AddWithValue("$g", gameId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.IsTrue(await r.ReadAsync(), $"game {gameId} not in catalog_owned");
        return (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2));
    }

    private async Task<(int Verified, string? Hash)> OwnedFlag(long gameId)
    {
        var (v, h, _) = await OwnedRow(gameId);
        return (v, h);
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
