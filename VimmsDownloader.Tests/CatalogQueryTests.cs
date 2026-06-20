using Microsoft.Data.Sqlite;

namespace VimmsDownloader.Tests;

/// <summary>
/// Validates the catalog query shapes (consoles + paged/filtered games) used by
/// CatalogRepository. The repository type is internal to the host, so — like the other
/// host tests — these mirror the repo's SQL against an in-memory SQLite database while
/// applying the *real* migration 012 from disk and seeding representative rows.
/// </summary>
[TestClass]
public class CatalogQueryTests
{
    private SqliteConnection _db = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        await _db.OpenAsync();
        await ApplyMigration("012_catalog.sql");
        await ApplyMigration("013_catalog_owned.sql");
        await ApplyMigration("015_catalog_1g1r.sql");
        await ApplyMigration("016_catalog_compat.sql");

        // Two systems: SNES (no-intro) and PS3 (redump).
        await Exec("INSERT INTO catalog_system (id, dat_name, console, source, game_count) VALUES (1, 'Nintendo - Super Nintendo Entertainment System', 'snes', 'no-intro', 3)");
        await Exec("INSERT INTO catalog_system (id, dat_name, console, source, game_count) VALUES (2, 'Sony - PlayStation 3', 'ps3', 'redump', 1)");

        // SNES games (note: out-of-alphabetical insert order to prove ORDER BY name).
        await AddGame(1, "Super Mario World (USA)", "USA", "SNS-MW-USA", "En", [("Super Mario World (USA).sfc", 524288)]);
        await AddGame(1, "Chrono Trigger (USA)", "USA", null, null, [("Chrono Trigger (USA).sfc", 4194304)]);
        await AddGame(1, "Super Metroid (Japan, USA) (En,Ja)", "Japan, USA", null, "En,Ja", [("Super Metroid.sfc", 3145728)]);

        // PS3 multi-disc game → size is the SUM of its roms.
        await AddGame(2, "Heavy Title (USA) (Disc 1)", "USA", "BLUS-1", null,
            [("d1.iso", 1000), ("d2.iso", 2000)]);

        // Mark two as owned: Super Mario World (game 1, snes) and Heavy Title (game 4, ps3).
        await Exec("INSERT INTO catalog_owned (game_id, filepath) VALUES (1, '/dl/snes/Super Mario World (USA).sfc')");
        await Exec("INSERT INTO catalog_owned (game_id, filepath) VALUES (4, '/dl/ps3/Heavy Title (USA) (Disc 1).iso')");
    }

    [TestCleanup]
    public async Task Cleanup() => await _db.DisposeAsync();

    [TestMethod]
    public async Task Consoles_ReturnsPerConsoleCountsWithOwned()
    {
        var consoles = await Consoles();
        Assert.HasCount(2, consoles);
        // ORDER BY console → ps3 before snes; owned: ps3=1 (Heavy), snes=1 (SMW)
        Assert.AreEqual(("ps3", 1, 1), consoles[0]);
        Assert.AreEqual(("snes", 3, 1), consoles[1]);
    }

    [TestMethod]
    public async Task Games_FilterByConsole()
    {
        var (total, games) = await Games("snes", null, 0, 100);
        Assert.AreEqual(3, total);
        Assert.HasCount(3, games);
        Assert.IsTrue(games.All(g => g.Console == "snes"));
    }

    [TestMethod]
    public async Task Games_OrderedByName()
    {
        var (_, games) = await Games("snes", null, 0, 100);
        CollectionAssert.AreEqual(
            new[] { "Chrono Trigger (USA)", "Super Mario World (USA)", "Super Metroid (Japan, USA) (En,Ja)" },
            games.Select(g => g.Name).ToArray());
    }

    [TestMethod]
    public async Task Games_NameFilter_CaseInsensitiveSubstring()
    {
        var (total, games) = await Games(null, "mario", 0, 100);
        Assert.AreEqual(1, total);
        Assert.AreEqual("Super Mario World (USA)", games[0].Name);
        Assert.AreEqual("En", games[0].Languages);
        Assert.AreEqual("SNS-MW-USA", games[0].Serial);
    }

    [TestMethod]
    public async Task Games_Paging()
    {
        var (total, page0) = await Games("snes", null, 0, 2);
        Assert.AreEqual(3, total);          // total ignores paging
        Assert.HasCount(2, page0);
        var (_, page1) = await Games("snes", null, 1, 2);
        Assert.HasCount(1, page1);
        Assert.AreEqual("Super Metroid (Japan, USA) (En,Ja)", page1[0].Name);
    }

    [TestMethod]
    public async Task Games_SizeIsSumOfRoms()
    {
        var (_, games) = await Games("ps3", null, 0, 100);
        Assert.HasCount(1, games);
        Assert.AreEqual(3000, games[0].Size); // 1000 + 2000 across the two discs
    }

    [TestMethod]
    public async Task Games_OwnedFlagReflectsCatalogOwned()
    {
        var (_, games) = await Games("snes", null, 0, 100);
        Assert.IsTrue(games.Single(g => g.Name == "Super Mario World (USA)").Owned);
        Assert.IsFalse(games.Single(g => g.Name == "Chrono Trigger (USA)").Owned);
    }

    [TestMethod]
    public async Task Games_LocalFilter_OwnedOnly()
    {
        var (total, games) = await Games(null, null, 0, 100, "owned");
        Assert.AreEqual(2, total); // SMW (snes) + Heavy (ps3)
        Assert.IsTrue(games.All(g => g.Owned));
    }

    [TestMethod]
    public async Task Games_LocalFilter_RemoteOnly()
    {
        var (total, games) = await Games("snes", null, 0, 100, "remote");
        Assert.AreEqual(2, total); // Chrono + Metroid (SMW is owned)
        Assert.IsTrue(games.All(g => !g.Owned));
    }

    [TestMethod]
    public async Task Games_Compat_JoinedByNormalizedSerial()
    {
        // Heavy Title's serial is "BLUS-1" → normalized "BLUS1"; seed an RPCS3 entry for it.
        await Exec("INSERT INTO catalog_compat (emulator, serial_key, status) VALUES ('rpcs3', 'BLUS1', 'Playable')");
        var (_, games) = await Games("ps3", null, 0, 100);
        Assert.AreEqual("Playable", games.Single().Compat);

        var (_, snes) = await Games("snes", null, 0, 100);
        Assert.IsTrue(snes.All(g => g.Compat is null)); // no compat for non-matching serials
    }

    [TestMethod]
    public async Task Games_Dedupe_ExcludesNonParents()
    {
        await Exec("UPDATE catalog_game SET is_parent = 0 WHERE name = 'Chrono Trigger (USA)'");
        var (dedupTotal, _) = await Games("snes", null, 0, 100, "all", dedupe: true);
        Assert.AreEqual(2, dedupTotal); // Chrono (non-parent) excluded
        var (allTotal, _) = await Games("snes", null, 0, 100, "all", dedupe: false);
        Assert.AreEqual(3, allTotal);   // default view unchanged
    }

    // --- mirrors of CatalogRepository query SQL ---

    private async Task<List<(string Console, int Total, int Owned)>> Consoles()
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT s.console, SUM(s.game_count) AS total,
                   (SELECT COUNT(*) FROM catalog_owned o
                      JOIN catalog_game g ON g.id = o.game_id
                      JOIN catalog_system s2 ON s2.id = g.system_id
                      WHERE s2.console = s.console) AS owned
            FROM catalog_system s GROUP BY s.console ORDER BY s.console
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(string, int, int)>();
        while (await r.ReadAsync()) list.Add((r.GetString(0), r.GetInt32(1), r.GetInt32(2)));
        return list;
    }

    private async Task<(int Total, List<(int Id, string Name, string Console, string? Region, string? Serial, string? Languages, long Size, bool Owned, string? Compat)> Games)>
        Games(string? console, string? query, int page, int pageSize, string local = "all", bool dedupe = false)
    {
        var like = string.IsNullOrWhiteSpace(query) ? null : "%" + query.Trim() + "%";
        const string where = """
            WHERE ($console IS NULL OR s.console = $console)
              AND ($like IS NULL OR g.name LIKE $like)
              AND ($local = 'all'
                   OR ($local = 'owned'  AND     EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id))
                   OR ($local = 'remote' AND NOT EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id)))
              AND ($dedupe = 0 OR g.is_parent = 1)
            """;

        int total;
        await using (var cnt = _db.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id {where}";
            cnt.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cnt.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            cnt.Parameters.AddWithValue("$local", local);
            cnt.Parameters.AddWithValue("$dedupe", dedupe ? 1 : 0);
            total = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        var games = new List<(int, string, string, string?, string?, string?, long, bool, string?)>();
        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT g.id, g.name, s.console, g.region, g.serial, g.languages,
                       (SELECT COALESCE(SUM(r.size), 0) FROM catalog_rom r WHERE r.game_id = g.id) AS size,
                       EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id) AS owned,
                       (SELECT c.status FROM catalog_compat c WHERE c.serial_key = UPPER(REPLACE(g.serial, '-', '')) LIMIT 1) AS compat
                FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id
                {where}
                ORDER BY g.name LIMIT $limit OFFSET $offset
                """;
            cmd.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$local", local);
            cmd.Parameters.AddWithValue("$dedupe", dedupe ? 1 : 0);
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", page * pageSize);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                games.Add((r.GetInt32(0), r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt64(6),
                    r.GetInt32(7) != 0,
                    r.IsDBNull(8) ? null : r.GetString(8)));
        }
        return (total, games);
    }

    // --- helpers ---

    private async Task AddGame(int systemId, string name, string? region, string? serial, string? langs,
        (string Name, long Size)[] roms)
    {
        long gid;
        await using (var ig = _db.CreateCommand())
        {
            ig.CommandText = """
                INSERT INTO catalog_game (system_id, name, region, serial, languages)
                VALUES ($sid, $name, $region, $serial, $langs) RETURNING id
                """;
            ig.Parameters.AddWithValue("$sid", systemId);
            ig.Parameters.AddWithValue("$name", name);
            ig.Parameters.AddWithValue("$region", (object?)region ?? DBNull.Value);
            ig.Parameters.AddWithValue("$serial", (object?)serial ?? DBNull.Value);
            ig.Parameters.AddWithValue("$langs", (object?)langs ?? DBNull.Value);
            gid = Convert.ToInt64(await ig.ExecuteScalarAsync());
        }
        foreach (var rom in roms)
        {
            await using var ir = _db.CreateCommand();
            ir.CommandText = "INSERT INTO catalog_rom (game_id, name, size) VALUES ($gid, $name, $size)";
            ir.Parameters.AddWithValue("$gid", gid);
            ir.Parameters.AddWithValue("$name", rom.Name);
            ir.Parameters.AddWithValue("$size", rom.Size);
            await ir.ExecuteNonQueryAsync();
        }
    }

    private async Task Exec(string sql)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ApplyMigration(string name)
    {
        var sql = await File.ReadAllTextAsync(FindMigration(name));
        foreach (var stmt in SplitStatements(sql))
            await Exec(stmt);
    }

    // Mirrors DatabaseMigrator.SplitStatements: strip comment-only lines BEFORE splitting on ';'
    // (so a ';' inside a comment can't break a statement), then drop empty chunks.
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var cleaned = string.Join('\n', sql.Split('\n')
            .Select(line => line.TrimStart().StartsWith("--") ? "" : line));
        return cleaned.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }

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
