using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Local-import background job (epic #118 / L2). Exercises the REAL <see cref="CatalogImportService"/> +
/// <see cref="ImportService"/> + <see cref="CatalogRepository"/> against a temp DB and real files: the
/// import folder is walked, each file placed (matched) or set aside (rejected), a per-file event logged,
/// and configured folder settings honored. Plus the single-flight gate (202/409). "hello" hashes to a
/// known SHA1 (aaf4c6…).
/// </summary>
[TestClass]
public class CatalogImportServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private string _downloads = null!;
    private string _importDir = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;
    private CatalogImportService _svc = null!;

    private const string HelloSha1 = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"catimport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";

        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance);
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
        var import = new ImportService(_catalog, _queue, new FakeArchiveExtractor(), NullLogger<ImportService>.Instance);
        _svc = new CatalogImportService(import, _queue, NullLogger<CatalogImportService>.Instance);

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
    public async Task ImportAsync_PlacesMatch_RejectsNonMatch_ReturnsSummary()
    {
        await SeedRom("snes", "Cat (USA).sfc", sha1: HelloSha1);
        Drop(_importDir, "match.sfc", "hello");
        Drop(_importDir, "nomatch.sfc", "world");

        var summary = await _svc.ImportAsync(default);

        Assert.AreEqual(new ImportSummary(2, 1, 1), summary);
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "completed", "snes", "match.sfc")));
        Assert.IsTrue(File.Exists(Path.Combine(_downloads, "rejected", "nomatch.sfc")));
        Assert.IsEmpty(Directory.GetFiles(_importDir), "both files should have been moved out of import/");
    }

    [TestMethod]
    public async Task ImportAsync_EmitsPerFileEvents()
    {
        var game = await SeedRom("gba", "Cat (USA).gba", sha1: HelloSha1);
        Drop(_importDir, "match.gba", "hello");
        Drop(_importDir, "nomatch.gba", "world");

        await _svc.ImportAsync(default);

        var events = await ImportEvents();
        Assert.HasCount(2, events);

        var matched = events.Single(e => e.Item == "match.gba");
        Assert.AreEqual(("import", "matched"), (matched.Type, matched.Phase));
        Assert.AreEqual(game, matched.GameId);
        StringAssert.Contains(matched.Message, "Matched");

        var rejected = events.Single(e => e.Item == "nomatch.gba");
        Assert.AreEqual(("import", "rejected"), (rejected.Type, rejected.Phase));
        Assert.IsNull(rejected.GameId);
        StringAssert.Contains(rejected.Message, "Rejected");
    }

    [TestMethod]
    public async Task ImportAsync_HonorsConfiguredFolders()
    {
        var customImport = Path.Combine(_dir, "drop");
        var customRejected = Path.Combine(_dir, "set-aside");
        Directory.CreateDirectory(customImport);
        await _queue.SaveSettingAsync(SettingsKeys.ImportPath, customImport);
        await _queue.SaveSettingAsync(SettingsKeys.RejectedPath, customRejected);
        Drop(customImport, "nope.iso", "world"); // no catalog → rejected

        var summary = await _svc.ImportAsync(default);

        Assert.AreEqual(new ImportSummary(1, 0, 1), summary);
        Assert.IsTrue(File.Exists(Path.Combine(customRejected, "nope.iso")));
        Assert.IsEmpty(Directory.GetFiles(_importDir), "the default import folder must be ignored when a path is configured");
    }

    [TestMethod]
    public async Task ImportAsync_EmptyFolder_NoMovesNoEvents()
    {
        var summary = await _svc.ImportAsync(default);

        Assert.AreEqual(new ImportSummary(0, 0, 0), summary);
        Assert.IsEmpty(await ImportEvents());
    }

    [TestMethod]
    public async Task ImportAsync_TopLevelOnly_IgnoresSubfolders()
    {
        await SeedRom("nes", "Cat (USA).nes", sha1: HelloSha1);
        var sub = Path.Combine(_importDir, "subfolder");
        Directory.CreateDirectory(sub);
        Drop(sub, "deep.nes", "hello"); // would match, but it's nested

        var summary = await _svc.ImportAsync(default);

        Assert.AreEqual(new ImportSummary(0, 0, 0), summary);
        Assert.IsTrue(File.Exists(Path.Combine(sub, "deep.nes")), "nested files are left untouched");
    }

    [TestMethod]
    public void Gate_IsSingleFlight_AcceptsThenConflictsWhileRunning()
    {
        var gate = new CatalogImportState();
        var block = new TaskCompletionSource();

        var first = gate.Run(NullLogger.Instance, "Import", _ => block.Task); // stays running until released
        var second = gate.Run(NullLogger.Instance, "Import", _ => Task.CompletedTask);

        Assert.AreEqual(202, (first as IStatusCodeHttpResult)?.StatusCode);
        Assert.AreEqual(409, (second as IStatusCodeHttpResult)?.StatusCode);
        Assert.IsTrue(gate.IsRunning);

        block.SetResult(); // let the first job finish
    }

    // --- helpers ---

    private static void Drop(string dir, string name, string content)
        => File.WriteAllText(Path.Combine(dir, name), content);

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

    private async Task<List<(string Item, string Type, string? Phase, string Message, long? GameId)>> ImportEvents()
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT item_name, event_type, phase, message, game_id FROM events WHERE event_type = 'import' ORDER BY item_name";
        var list = new List<(string, string, string?, string, long?)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4)));
        return list;
    }
}
