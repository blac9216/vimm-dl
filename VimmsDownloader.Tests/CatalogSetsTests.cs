using Microsoft.Data.Sqlite;

namespace VimmsDownloader.Tests;

/// <summary>
/// Mirrors the catalog_set / catalog_set_link CRUD SQL used by CatalogRepository (internal to the
/// host) against an in-memory DB with the real migrations applied. A set is now {name, console,
/// links[]} (migration 020).
/// </summary>
[TestClass]
public class CatalogSetsTests
{
    private SqliteConnection _db = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        await _db.OpenAsync();
        await ApplyMigration("014_catalog_sets.sql");
        await ApplyMigration("018_catalog_set_unique.sql");
        await ApplyMigration("020_catalog_set_links.sql");
    }

    [TestCleanup]
    public async Task Cleanup() => await _db.DisposeAsync();

    [TestMethod]
    public async Task Add_List_Delete_RoundTrips()
    {
        var gbaId = await AddSet("GBA No-Intro", "gba", ["https://archive.org/download/ef_gba_no-intro"]);
        await AddSet("PS1 Archive", "psx",
            ["https://archive.org/download/sony_playstation_part1", "https://archive.org/download/sony_playstation_part2"]);

        var all = await GetSets(null);
        Assert.HasCount(2, all);
        // ORDER BY console, name → gba ("GBA No-Intro") before psx ("PS1 Archive")
        Assert.AreEqual("gba", all[0].Console);
        Assert.AreEqual("GBA No-Intro", all[0].Name);
        Assert.HasCount(1, all[0].Links);
        Assert.AreEqual("PS1 Archive", all[1].Name);
        Assert.HasCount(2, all[1].Links);                                                    // both links kept
        Assert.AreEqual("https://archive.org/download/sony_playstation_part1", all[1].Links[0]); // order preserved

        Assert.HasCount(1, await GetSets("gba"));

        Assert.IsTrue(await DeleteSet((int)gbaId));
        Assert.HasCount(1, await GetSets(null));        // set gone
        Assert.IsEmpty(await AllLinks((int)gbaId));     // its links cascade-deleted
        Assert.IsFalse(await DeleteSet(99999));         // unknown id
    }

    [TestMethod]
    public async Task Update_ReplacesNameConsoleAndLinks()
    {
        var id = await AddSet("Old", "gba", ["https://archive.org/download/a"]);
        Assert.IsTrue(await UpdateSet((int)id, "New", "psx",
            ["https://archive.org/download/b", "https://archive.org/download/c"]));

        var set = (await GetSets(null)).Single();
        Assert.AreEqual("New", set.Name);
        Assert.AreEqual("psx", set.Console);
        CollectionAssert.AreEqual(
            new[] { "https://archive.org/download/b", "https://archive.org/download/c" }, set.Links.ToArray());

        Assert.IsFalse(await UpdateSet(99999, "x", "gba", ["https://archive.org/download/z"])); // unknown id
    }

    [TestMethod]
    public async Task Migration020_Backfill_ConvertsLegacyIdentifierToOneLinkSet()
    {
        // A pre-020 row carrying only the legacy identifier → 020's backfill (re-runnable) names it
        // from the label and creates one archive.org link.
        await Exec("INSERT INTO catalog_set (console, source, identifier, label) VALUES ('gba','archive','legacy_item','Legacy')");
        await Exec("UPDATE catalog_set SET name = COALESCE(NULLIF(label,''), identifier) WHERE name IS NULL");
        await Exec("INSERT INTO catalog_set_link (set_id, url, position) SELECT id, 'https://archive.org/download/' || identifier, 0 FROM catalog_set WHERE identifier IS NOT NULL AND identifier != '' AND NOT EXISTS (SELECT 1 FROM catalog_set_link l WHERE l.set_id = catalog_set.id)");

        var set = (await GetSets("gba")).Single();
        Assert.AreEqual("Legacy", set.Name);
        Assert.HasCount(1, set.Links);
        Assert.AreEqual("https://archive.org/download/legacy_item", set.Links[0]);
    }

    // --- mirrors of CatalogRepository set SQL (catalog_set + catalog_set_link) ---

    private async Task<long> AddSet(string name, string console, string[] links)
    {
        long id;
        await using (var cmd = _db.CreateCommand())
        {
            // legacy source/identifier are NOT NULL but unused → placeholders (mirrors AddSetAsync)
            cmd.CommandText = "INSERT INTO catalog_set (name, console, source, identifier, created_at) VALUES ($n,$c,'archive','',datetime('now')) RETURNING id";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", console);
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await InsertLinks((int)id, links);
        return id;
    }

    private async Task<bool> UpdateSet(int id, string name, string console, string[] links)
    {
        int rows;
        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "UPDATE catalog_set SET name=$n, console=$c WHERE id=$id";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", console);
            cmd.Parameters.AddWithValue("$id", id);
            rows = await cmd.ExecuteNonQueryAsync();
        }
        if (rows == 0) return false;
        await Exec($"DELETE FROM catalog_set_link WHERE set_id = {id}");
        await InsertLinks(id, links);
        return true;
    }

    private async Task InsertLinks(int setId, string[] links)
    {
        int pos = 0;
        foreach (var url in links)
        {
            await using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO catalog_set_link (set_id, url, position) VALUES ($s,$u,$p)";
            cmd.Parameters.AddWithValue("$s", setId);
            cmd.Parameters.AddWithValue("$u", url);
            cmd.Parameters.AddWithValue("$p", pos++);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<(int Id, string Name, string Console, List<string> Links)>> GetSets(string? console)
    {
        var order = new List<(int, string, string)>();
        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = console is null
                ? "SELECT id, name, console FROM catalog_set ORDER BY console, name, id"
                : "SELECT id, name, console FROM catalog_set WHERE console=$c ORDER BY name, id";
            if (console is not null) cmd.Parameters.AddWithValue("$c", console);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                order.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2)));
        }
        var result = new List<(int, string, string, List<string>)>();
        foreach (var (id, name, con) in order)
            result.Add((id, name, con, await AllLinks(id)));
        return result;
    }

    private async Task<List<string>> AllLinks(int setId)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT url FROM catalog_set_link WHERE set_id=$s ORDER BY position, id";
        cmd.Parameters.AddWithValue("$s", setId);
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    private async Task<bool> DeleteSet(int id)
    {
        await Exec($"DELETE FROM catalog_set_link WHERE set_id = {id}");
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM catalog_set WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
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
        var cleaned = string.Join('\n', sql.Split('\n').Select(l => l.TrimStart().StartsWith("--") ? "" : l));
        foreach (var stmt in cleaned.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            await using var cmd = _db.CreateCommand();
            cmd.CommandText = stmt;
            await cmd.ExecuteNonQueryAsync();
        }
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
        throw new FileNotFoundException($"Could not locate migration {name}");
    }
}
