using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the real <see cref="CatalogRepository.GetDescriptionAsync"/> (epic #122 / M3) — the read
/// path behind <c>GET /api/catalog/games/{id}/image</c>'s sibling description endpoint — against a temp
/// SQLite file with the real migrations (incl. 029): a stored description round-trips; none → null.
/// </summary>
[TestClass]
public class CatalogDescriptionTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"desc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";
        await using (var db = new SqliteConnection(_connStr))
        {
            await db.OpenAsync();
            await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        }
        _repo = new CatalogRepository();
        _repo.Configure(_connStr);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task GetDescription_ReturnsStoredText_OrNullWhenAbsent()
    {
        var system = await ScalarLong(
            "INSERT INTO catalog_system (dat_name, console, source) VALUES ('SNES DAT', 'snes', 'no-intro') RETURNING id");
        var described = await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({system}, 'Chrono Trigger (USA)') RETURNING id");
        var bare = await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({system}, 'Bare Game (USA)') RETURNING id");
        await _repo.SetDescriptionsAsync([(described, "A time-travel RPG.")], default);

        Assert.AreEqual("A time-travel RPG.", await _repo.GetDescriptionAsync((int)described));
        Assert.IsNull(await _repo.GetDescriptionAsync((int)bare));        // game exists, no description
        Assert.IsNull(await _repo.GetDescriptionAsync(999999));            // unknown game id
    }

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
