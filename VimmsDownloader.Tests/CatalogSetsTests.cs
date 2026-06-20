using Microsoft.Data.Sqlite;

namespace VimmsDownloader.Tests;

/// <summary>
/// Mirrors the catalog_set CRUD SQL used by CatalogRepository (internal to the host) against an
/// in-memory database with the real migration 014 applied.
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
    }

    [TestCleanup]
    public async Task Cleanup() => await _db.DisposeAsync();

    [TestMethod]
    public async Task Add_List_Delete_RoundTrips()
    {
        var gbaId = await AddSet("gba", "archive", "ef_gba_no-intro", "GBA No-Intro");
        await AddSet("ps3", "archive", "some_ps3_item", null);

        var all = await GetSets(null);
        Assert.HasCount(2, all);
        // ORDER BY console, id → gba first
        Assert.AreEqual("gba", all[0].Console);
        Assert.AreEqual("ef_gba_no-intro", all[0].Identifier);
        Assert.AreEqual("GBA No-Intro", all[0].Label);
        Assert.IsNull(all[1].Label); // ps3 set had no label

        Assert.HasCount(1, await GetSets("gba"));

        Assert.IsTrue(await DeleteSet((int)gbaId));
        Assert.HasCount(1, await GetSets(null));
        Assert.IsFalse(await DeleteSet(99999)); // unknown id
    }

    [TestMethod]
    public async Task Add_Duplicate_UpsertsInsteadOfDuplicating()
    {
        // #40: re-adding the same (console, source, identifier) must not create a second row; it
        // updates the label and returns the existing id (enforced by the unique index in 018).
        var first = await AddSet("gba", "archive", "ef_gba_no-intro", "First");
        var second = await AddSet("gba", "archive", "ef_gba_no-intro", "Updated");

        Assert.AreEqual(first, second);            // same row id
        var all = await GetSets("gba");
        Assert.HasCount(1, all);                   // no duplicate row
        Assert.AreEqual("Updated", all[0].Label);  // label upserted
    }

    // --- mirrors of CatalogRepository set SQL ---

    private async Task<long> AddSet(string console, string source, string identifier, string? label)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_set (console, source, identifier, label, created_at)
            VALUES ($c,$s,$i,$l,datetime('now'))
            ON CONFLICT(console, source, identifier) DO UPDATE SET label = excluded.label
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("$c", console);
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$i", identifier);
        cmd.Parameters.AddWithValue("$l", (object?)label ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<(int Id, string Console, string Source, string Identifier, string? Label)>> GetSets(string? console)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = console is null
            ? "SELECT id, console, source, identifier, label FROM catalog_set ORDER BY console, id"
            : "SELECT id, console, source, identifier, label FROM catalog_set WHERE console=$c ORDER BY id";
        if (console is not null) cmd.Parameters.AddWithValue("$c", console);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(int, string, string, string, string?)>();
        while (await r.ReadAsync())
            list.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    private async Task<bool> DeleteSet(int id)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM catalog_set WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
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
