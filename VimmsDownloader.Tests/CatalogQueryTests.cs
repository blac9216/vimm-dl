using Microsoft.Data.Sqlite;
using Module.Catalog;

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
        await ApplyMigration("017_catalog_verified.sql");
        await ApplyMigration("019_catalog_serial_key.sql");

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
    public async Task Games_Compat_JoinsSerialWithNonDashSeparator()
    {
        // #48: a serial whose normalized form requires stripping a non-dash char (a space here) must
        // still join. The old inline UPPER(REPLACE(serial,'-','')) kept the space and missed this.
        await AddGame(2, "Spacey (USA)", "USA", "BLUS 30443", null, [("s.iso", 10)]);
        await Exec("INSERT INTO catalog_compat (emulator, serial_key, status) VALUES ('rpcs3', 'BLUS30443', 'Ingame')");
        var (_, games) = await Games("ps3", "Spacey", 0, 100);
        Assert.AreEqual("Ingame", games.Single().Compat);
    }

    [TestMethod]
    public async Task Games_VerifiedFlag_ReflectsCatalogOwned()
    {
        await Exec("UPDATE catalog_owned SET verified = 1 WHERE game_id = 1"); // Super Mario World
        var (_, games) = await Games("snes", null, 0, 100);
        Assert.IsTrue(games.Single(g => g.Name == "Super Mario World (USA)").Verified);
        Assert.IsNull(games.Single(g => g.Name == "Chrono Trigger (USA)").Verified); // not owned → null
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

    [TestMethod]
    public async Task Games_EnglishOnly_FiltersToWesternReleases()
    {
        // A pure-Japan title (no English/Western region, no En language) is hidden in English-only mode.
        await AddGame(1, "Seiken Densetsu 3 (Japan)", "Japan", null, "Ja", [("sd3.sfc", 100)]);
        var (total, games) = await Games("snes", null, 0, 100, english: true);
        Assert.AreEqual(3, total); // SMW (USA), Chrono (USA), Super Metroid (En,Ja) — Seiken excluded
        Assert.IsFalse(games.Any(g => g.Name.Contains("Seiken")));
    }

    [TestMethod]
    public async Task Games_EnglishOnly_KeepsJapanRegionWithEnglishLanguage()
    {
        // Super Metroid is region "Japan, USA" with languages "En,Ja" — English via its language list.
        var (_, games) = await Games("snes", "Metroid", 0, 100, english: true);
        Assert.AreEqual("Super Metroid (Japan, USA) (En,Ja)", games.Single().Name);
    }

    [TestMethod]
    public async Task Games_ExcludeCategories_HidesDemoBetaProtoKioskSample()
    {
        await AddGame(1, "Cat Demo (USA) (Demo)", "USA", null, "En", [("d.sfc", 1)]);
        await AddGame(1, "Cat Beta (USA) (Beta)", "USA", null, "En", [("b.sfc", 1)]);
        await AddGame(1, "Cat Proto (USA) (Proto)", "USA", null, "En", [("p.sfc", 1)]);
        await AddGame(1, "Cat Kiosk (USA) (Kiosk)", "USA", null, "En", [("k.sfc", 1)]);
        await AddGame(1, "Cat Sample (USA) (Sample)", "USA", null, "En", [("s.sfc", 1)]);

        var (unfiltered, _) = await Games("snes", null, 0, 100);
        Assert.AreEqual(8, unfiltered); // 3 base + 5 category variants

        var (filtered, games) = await Games("snes", null, 0, 100, excludeCategories: true);
        Assert.AreEqual(3, filtered);   // all five non-final variants dropped
        Assert.IsFalse(games.Any(g => g.Name.StartsWith("Cat ")));
    }

    [TestMethod]
    public async Task Games_EnglishAndExcludeCategories_Compose()
    {
        await AddGame(1, "JP Demo (Japan) (Demo)", "Japan", null, "Ja", [("j.sfc", 1)]);
        var (total, games) = await Games("snes", null, 0, 100, english: true, excludeCategories: true);
        Assert.AreEqual(3, total); // the Japanese demo is excluded by both filters; base 3 English retail remain
        Assert.IsFalse(games.Any(g => g.Name.Contains("JP Demo")));
    }

    [TestMethod]
    public async Task Games_EnglishOnly_RegionEmptyName_BoundaryAwareUkToken()
    {
        // #197: region column empty → the filter falls back to the name. The "uk" bigram inside
        // "Sukeban" must NOT count as English, but a genuine "(UK)" tag still must.
        await AddGame(1, "Sukeban Deka (Japan)", null, null, null, [("s.sfc", 1)]);
        await AddGame(1, "Manchester Utd (UK)", null, null, null, [("u.sfc", 1)]);

        var (jpTotal, _) = await Games("snes", "Sukeban", 0, 100, english: true);
        Assert.AreEqual(0, jpTotal); // not English — "uk" is inside a word, not a region tag

        var (ukTotal, ukGames) = await Games("snes", "Manchester", 0, 100, english: true);
        Assert.AreEqual(1, ukTotal); // a real (UK) release still classifies as English
        Assert.AreEqual("Manchester Utd (UK)", ukGames[0].Name);
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

    private async Task<(int Total, List<(int Id, string Name, string Console, string? Region, string? Serial, string? Languages, long Size, bool Owned, string? Compat, bool? Verified)> Games)>
        Games(string? console, string? query, int page, int pageSize, string local = "all", bool dedupe = false,
              bool english = false, bool excludeCategories = false)
    {
        var like = string.IsNullOrWhiteSpace(query) ? null : "%" + query.Trim() + "%";

        // Mirror CatalogRepository.GetGamesAsync: the English-only + hide-demos clauses are built from
        // the same Dedup token/tag lists, so this test exercises the real filter shape (no drift).
        const string engHaystack =
            "' ' || replace(replace(replace(lower(coalesce(g.region, g.name)), '(', ' '), ')', ' '), ',', ' ') || ' '";
        var englishClause = english
            ? " AND (instr(lower(coalesce(g.languages, '')), 'en') > 0"
              + string.Concat(Enumerable.Range(0, Dedup.EnglishRegionTokens.Length)
                    .Select(i => $" OR instr({engHaystack}, ' ' || $eng{i} || ' ') > 0"))
              + ")"
            : "";
        var categoryClause = excludeCategories
            ? " AND NOT ("
              + string.Join(" OR ", Enumerable.Range(0, Dedup.ExcludedCategoryTags.Length)
                    .Select(i => $"instr(lower(g.name), $cat{i}) > 0"))
              + ")"
            : "";

        var where = $"""
            WHERE ($console IS NULL OR s.console = $console)
              AND ($like IS NULL OR g.name LIKE $like)
              AND ($local = 'all'
                   OR ($local = 'owned'  AND     EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id))
                   OR ($local = 'remote' AND NOT EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id)))
              AND ($dedupe = 0 OR g.is_parent = 1){englishClause}{categoryClause}
            """;

        void BindFilters(SqliteCommand cmd)
        {
            cmd.Parameters.AddWithValue("$console", (object?)console ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$like", (object?)like ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$local", local);
            cmd.Parameters.AddWithValue("$dedupe", dedupe ? 1 : 0);
            if (english)
                for (int i = 0; i < Dedup.EnglishRegionTokens.Length; i++)
                    cmd.Parameters.AddWithValue($"$eng{i}", Dedup.EnglishRegionTokens[i]);
            if (excludeCategories)
                for (int i = 0; i < Dedup.ExcludedCategoryTags.Length; i++)
                    cmd.Parameters.AddWithValue($"$cat{i}", Dedup.ExcludedCategoryTags[i]);
        }

        int total;
        await using (var cnt = _db.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id {where}";
            BindFilters(cnt);
            total = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        var games = new List<(int, string, string, string?, string?, string?, long, bool, string?, bool?)>();
        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT g.id, g.name, s.console, g.region, g.serial, g.languages,
                       (SELECT COALESCE(SUM(r.size), 0) FROM catalog_rom r WHERE r.game_id = g.id) AS size,
                       EXISTS(SELECT 1 FROM catalog_owned o WHERE o.game_id = g.id) AS owned,
                       (SELECT c.status FROM catalog_compat c WHERE c.serial_key = g.serial_key LIMIT 1) AS compat,
                       (SELECT o.verified FROM catalog_owned o WHERE o.game_id = g.id) AS verified
                FROM catalog_game g JOIN catalog_system s ON s.id = g.system_id
                {where}
                ORDER BY g.name LIMIT $limit OFFSET $offset
                """;
            BindFilters(cmd);
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
                    r.IsDBNull(8) ? null : r.GetString(8),
                    r.IsDBNull(9) ? null : r.GetInt32(9) != 0));
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
            // Populate serial_key the same way CatalogRepository.ReplaceSystemGamesAsync does, so the
            // compat join (c.serial_key = g.serial_key) is exercised against realistic data.
            ig.CommandText = """
                INSERT INTO catalog_game (system_id, name, region, serial, serial_key, languages)
                VALUES ($sid, $name, $region, $serial, $skey, $langs) RETURNING id
                """;
            ig.Parameters.AddWithValue("$sid", systemId);
            ig.Parameters.AddWithValue("$name", name);
            ig.Parameters.AddWithValue("$region", (object?)region ?? DBNull.Value);
            ig.Parameters.AddWithValue("$serial", (object?)serial ?? DBNull.Value);
            ig.Parameters.AddWithValue("$skey", string.IsNullOrEmpty(serial) ? DBNull.Value : NormalizeSerial(serial));
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

    // Mirrors Module.Catalog RpcsCompat.NormalizeSerial: strip non-alphanumerics, uppercase.
    private static string NormalizeSerial(string serial)
        => new(serial.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

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
