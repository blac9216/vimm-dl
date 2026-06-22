using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Phase C / C3: hash-based owned detection. Exercises the REAL <see cref="CatalogVerifyService"/> +
/// <see cref="CatalogRepository"/> against a temp DB and real files under <c>completed/{console}/</c>:
/// a file whose SHA1/MD5/CRC32 matches a <c>catalog_rom</c> marks that game owned regardless of its
/// name; the matched hash is recorded; non-matching files are not owned. "hello" has known hashes
/// (sha1 aaf4c6…, md5 5d41402a…, crc 3610A686).
/// </summary>
[TestClass]
public class HashOwnedTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private string _completed = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;
    private CatalogVerifyService _verify = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"hashowned-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";

        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance); // runs migrations + derives the download path
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
        _verify = new CatalogVerifyService(_catalog, _queue, NullLogger<CatalogVerifyService>.Instance);

        _completed = Path.Combine(_queue.GetDownloadPath(), "completed", "ps3");
        Directory.CreateDirectory(_completed);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task Verify_MarksOwnedBySha1_RegardlessOfFilename()
    {
        // catalog_rom hash stored uppercase; the file is named nothing like the catalog title.
        var game = await SeedRom("ps3", "Real Catalog Name (USA).iso",
            sha1: "AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D", md5: "deadbeef", crc: "FFFFFFFF");
        await WriteFile("totally-different-name.iso", "hello");

        var matched = await _verify.VerifyAsync(default);

        Assert.AreEqual(1, matched);
        var (verified, hash) = await OwnedState(game);
        Assert.AreEqual(1, verified);
        Assert.AreEqual("sha1", hash);
    }

    [TestMethod]
    public async Task Verify_FallsBackToMd5_WhenNoSha1()
    {
        var game = await SeedRom("ps3", "Game.iso",
            sha1: null, md5: "5D41402ABC4B2A76B9719D911017C592", crc: null);
        await WriteFile("g.iso", "hello");

        Assert.AreEqual(1, await _verify.VerifyAsync(default));
        Assert.AreEqual((1, "md5"), await OwnedState(game));
    }

    [TestMethod]
    public async Task Verify_NonMatchingFile_NotOwned()
    {
        var game = await SeedRom("ps3", "Game.iso",
            sha1: "AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D", md5: null, crc: null);
        await WriteFile("g.iso", "world"); // different content → different hashes

        Assert.AreEqual(0, await _verify.VerifyAsync(default));
        Assert.IsFalse(await IsOwned(game)); // non-matching content never marks the game owned
    }

    [TestMethod]
    public async Task Verify_IsConsoleScoped()
    {
        // A psx rom with the same hash must not be matched by a file under completed/ps3/.
        var psx = await SeedRom("psx", "Other.bin", sha1: "AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D", md5: null, crc: null);
        await WriteFile("hello.iso", "hello"); // lives under completed/ps3/

        Assert.AreEqual(0, await _verify.VerifyAsync(default));
        Assert.IsFalse(await IsOwned(psx));
    }

    [TestMethod]
    public async Task Verify_MarksOwnedByZipEntryCrc()
    {
        // The rom lives inside a .zip; its CRC is read from the central directory (no decompress).
        // A .zip entry stores the CRC32 of its *uncompressed* content, so "hello" → 3610A686.
        var game = await SeedRom("ps3", "Real Catalog Name (USA).iso", sha1: null, md5: null, crc: "3610A686");
        WriteZip("archived.zip", "inner.iso", "hello");

        Assert.AreEqual(1, await _verify.VerifyAsync(default));
        Assert.AreEqual((1, "crc"), await OwnedState(game)); // matched by the zip entry's CRC
    }

    [TestMethod]
    public async Task Verify_Skips7zArchive_NotOwned()
    {
        // .7z can't be hashed without extracting, so it's skipped — even though the bytes are "hello"
        // (crc 3610A686) and a rom with that CRC exists, the archive is left unmatched (and no throw).
        var game = await SeedRom("ps3", "Game.iso", sha1: null, md5: null, crc: "3610A686");
        await WriteFile("archived.7z", "hello");

        Assert.AreEqual(0, await _verify.VerifyAsync(default));
        Assert.IsFalse(await IsOwned(game));
    }

    // --- helpers ---

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

    private async Task WriteFile(string name, string content)
        => await File.WriteAllTextAsync(Path.Combine(_completed, name), content);

    private void WriteZip(string name, string entryName, string content)
    {
        using var zip = ZipFile.Open(Path.Combine(_completed, name), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
        writer.Write(content); // UTF-8 (no BOM); ASCII content hashes to the same CRC32
    }

    private async Task<(int Verified, string? Hash)> OwnedState(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT verified, verified_hash FROM catalog_owned WHERE game_id = $g";
        cmd.Parameters.AddWithValue("$g", gameId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.IsTrue(await r.ReadAsync(), $"game {gameId} not in catalog_owned");
        return (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1));
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
