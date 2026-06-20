using Microsoft.Data.Sqlite;

namespace VimmsDownloader.Tests;

/// <summary>
/// Validates the Phase 2a (source, source_id) identity migration (010) against a
/// pre-010 schema, plus the QueueRepository query shapes that read/write it. The
/// repository type is internal to the host, so — like the duplicate-detection
/// tests — these mirror the repo's SQL against an in-memory SQLite database while
/// applying the *real* migration file from disk.
/// </summary>
[TestClass]
public class SourceIdentityTests
{
    private SqliteConnection _db = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        await _db.OpenAsync();
        // Pre-010 schema (no source columns), matching migrations 001/009.
        await Exec("""
            CREATE TABLE queued_urls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                format INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE completed_urls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                filename TEXT NOT NULL,
                filepath TEXT,
                completed_at TEXT,
                format INTEGER
            );
        """);
    }

    [TestCleanup]
    public async Task Cleanup() => await _db.DisposeAsync();

    [TestMethod]
    public async Task Migration010_BackfillsLegacyRows()
    {
        await Exec("INSERT INTO queued_urls (url, format) VALUES ('https://vimm.net/vault/1', 0)");
        await Exec("INSERT INTO completed_urls (url, filename, format) VALUES ('https://vimm.net/vault/2', 'Game.7z', 1)");

        await ApplyMigration("010_add_source_identity.sql", ignoreDuplicates: false);

        var (qSource, qSourceId) = await ReadSource("queued_urls", "https://vimm.net/vault/1");
        Assert.AreEqual("vimm", qSource);
        Assert.AreEqual("https://vimm.net/vault/1", qSourceId);

        var (cSource, cSourceId) = await ReadSource("completed_urls", "https://vimm.net/vault/2");
        Assert.AreEqual("vimm", cSource);
        Assert.AreEqual("https://vimm.net/vault/2", cSourceId);
    }

    [TestMethod]
    public async Task Migration010_IsIdempotent()
    {
        await ApplyMigration("010_add_source_identity.sql", ignoreDuplicates: false);
        // Re-applying must not throw on duplicate columns (mirrors DatabaseMigrator's ignore rule).
        await ApplyMigration("010_add_source_identity.sql", ignoreDuplicates: true);
        // Columns still usable afterwards.
        await Exec("INSERT INTO queued_urls (url, format, source, source_id) VALUES ('u', 0, 'vimm', 'u')");
    }

    [TestMethod]
    public async Task AddThenComplete_PreservesNonDefaultSource()
    {
        await ApplyMigration("010_add_source_identity.sql", ignoreDuplicates: false);

        // Mirrors AddToQueueAsync with a non-default source.
        await Exec("INSERT INTO queued_urls (url, format, source, source_id) VALUES ('https://x/1', 0, 'myrient', 'myr-123')");

        // Mirrors GetNextQueueItemAsync (now returns source).
        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, url, format, source FROM queued_urls ORDER BY id LIMIT 1";
            await using var r = await cmd.ExecuteReaderAsync();
            Assert.IsTrue(await r.ReadAsync());
            Assert.AreEqual("myrient", r.GetString(3));
        }

        // Mirrors CompleteItemAsync: read (source, source_id) from the queued row, carry onto completed.
        int id; string source, sourceId;
        await using (var sel = _db.CreateCommand())
        {
            sel.CommandText = "SELECT id, source, source_id FROM queued_urls WHERE url = 'https://x/1'";
            await using var r = await sel.ExecuteReaderAsync();
            Assert.IsTrue(await r.ReadAsync());
            id = r.GetInt32(0); source = r.GetString(1); sourceId = r.GetString(2);
        }
        await Exec($"DELETE FROM queued_urls WHERE id = {id}");
        await using (var ins = _db.CreateCommand())
        {
            ins.CommandText = "INSERT INTO completed_urls (url, filename, format, source, source_id) VALUES ('https://x/1', 'G.iso', 0, $s, $sid)";
            ins.Parameters.AddWithValue("$s", source);
            ins.Parameters.AddWithValue("$sid", sourceId);
            await ins.ExecuteNonQueryAsync();
        }

        var (cSource, cSourceId) = await ReadSource("completed_urls", "https://x/1");
        Assert.AreEqual("myrient", cSource);
        Assert.AreEqual("myr-123", cSourceId);
    }

    // --- helpers ---

    private async Task Exec(string sql)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(string Source, string SourceId)> ReadSource(string table, string url)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT source, source_id FROM {table} WHERE url = $url";
        cmd.Parameters.AddWithValue("$url", url);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.IsTrue(await r.ReadAsync(), $"row not found in {table}");
        return (r.GetString(0), r.IsDBNull(1) ? null! : r.GetString(1));
    }

    private async Task ApplyMigration(string name, bool ignoreDuplicates)
    {
        foreach (var stmt in SplitStatements(await File.ReadAllTextAsync(FindMigration(name))))
        {
            try { await Exec(stmt); }
            catch (SqliteException ex) when (ignoreDuplicates
                && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) { }
        }
    }

    /// <summary>Split on ';' and drop comment-only / empty chunks (SQLite ignores inline -- comments).</summary>
    private static IEnumerable<string> SplitStatements(string sql)
        => sql.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
              .Where(s => s.Split('\n').Any(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith("--")));

    /// <summary>Locate the real migration file by walking up from the test binary directory.</summary>
    private static string FindMigration(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "VimmsDownloader", "Migrations", name);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Could not locate migration {name} from {AppContext.BaseDirectory}");
    }
}
