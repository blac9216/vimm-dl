using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the REAL <see cref="CatalogRepository"/> Vimm-binding methods (migration 021) against a
/// temp SQLite file with the real migrations applied: the per-console hash index (lowercased,
/// console-scoped), binding a game to a vault entry with formats (idempotent replace), and flagging a
/// console's unbound games as "no Vimm match".
/// </summary>
[TestClass]
public class VimmBindingTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmbind-{Guid.NewGuid():N}");
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
    public async Task GetVimmHashIndex_IsLowercased_AndConsoleScoped()
    {
        var snes = await Seed("snes");
        var snesGame = await AddGame(snes, "ActRaiser");
        await AddRom(snesGame, "ActRaiser (USA).sfc", crc: "EAC3358D", md5: "635D5D7D", sha1: "E8365852");
        var ps = await Seed("psx");
        var psGame = await AddGame(ps, "Other");
        await AddRom(psGame, "Other.bin", crc: "12345678", md5: "AA", sha1: "BB");

        var idx = await _repo.GetVimmHashIndexAsync("snes", default);

        Assert.AreEqual(snesGame, idx.ByCrc["eac3358d"]);   // lowercased
        Assert.AreEqual(snesGame, idx.ByMd5["635d5d7d"]);
        Assert.AreEqual(snesGame, idx.BySha1["e8365852"]);
        Assert.IsFalse(idx.ByCrc.ContainsKey("12345678"));  // psx rom excluded (console-scoped)
    }

    [TestMethod]
    public async Task BindVimm_SetsVaultAndFormats_AndReplacesOnRebind()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");

        await _repo.BindVimmAsync(game, 5000, "sha1",
            [new(0, "JB Folder", 100, "100 B"), new(1, ".dec.iso", 200, "200 B")], default);

        Assert.AreEqual((5000L, "sha1"), await GameBinding(game));
        var formats = await Formats(game);
        Assert.HasCount(2, formats);
        Assert.AreEqual((0, "JB Folder", 100L), formats[0]);
        Assert.AreEqual((1, ".dec.iso", 200L), formats[1]);

        // Re-bind with a single format → prior formats replaced, not appended.
        await _repo.BindVimmAsync(game, 5000, "md5", [new(0, "JB Folder", 100, "100 B")], default);
        Assert.AreEqual((5000L, "md5"), await GameBinding(game));
        Assert.HasCount(1, await Formats(game));
    }

    [TestMethod]
    public async Task MarkVimmUnmatched_FlagsOnlyUnboundGamesOnThatConsole()
    {
        var snes = await Seed("snes");
        var bound = await AddGame(snes, "Bound");
        var unbound = await AddGame(snes, "Unbound");
        var ps = await Seed("psx");
        var other = await AddGame(ps, "OtherConsole");
        await _repo.BindVimmAsync(bound, 1, "sha1", [], default);

        var flagged = await _repo.MarkVimmUnmatchedAsync("snes", default);

        Assert.AreEqual(1, flagged);                              // only the unbound snes game
        Assert.AreEqual("none", await Match(unbound));
        Assert.AreEqual("sha1", await Match(bound));              // bound game untouched
        Assert.IsNull(await Match(other));                        // other console untouched (still unscraped)
    }

    [TestMethod]
    public async Task GetGames_SurfacesVimmMatch_ForTheBadge()
    {
        var snes = await Seed("snes");
        var bound = await AddGame(snes, "Bound Game");
        await AddRom(bound, "Bound Game.sfc", "aa", "bb", "cc");
        var unbound = await AddGame(snes, "Unbound Game");
        await AddRom(unbound, "Unbound Game.sfc", "dd", "ee", "ff");
        await _repo.BindVimmAsync(bound, 100, "sha1", [], default);

        var (_, games) = await _repo.GetGamesAsync("snes", null, "all", false, false, false, 0, 100);
        var byName = games.ToDictionary(g => g.Name);
        Assert.AreEqual("sha1", byName["Bound Game"].VimmMatch);
        Assert.IsNull(byName["Unbound Game"].VimmMatch);
    }

    [TestMethod]
    public async Task GetGames_ConsolidatesFormatsAndSources_PerGame()
    {
        // Phase C / C5: one row per game carrying its available + owned formats and owned sources.
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Multi Format Game");
        await AddRom(game, "Multi Format Game.sfc", "aa", "bb", "cc");
        // Vimm offers two formats (available); the user owns both — one from Vimm, one from archive.
        await _repo.BindVimmAsync(game, 200, "sha1",
            [new(0, "JB Folder", 10, "10 B"), new(1, ".dec.iso", 20, "20 B")], default);
        await AddCompleted(game, format: 0, source: "vimm", "G.7z");
        await AddCompleted(game, format: 1, source: "archive", "G.dec.iso");

        var (_, games) = await _repo.GetGamesAsync("snes", null, "all", false, false, false, 0, 100);
        var g = games.Single(x => x.Name == "Multi Format Game");

        CollectionAssert.AreEqual(new[] { 0, 1 }, g.AvailableFormats);
        CollectionAssert.AreEqual(new[] { 0, 1 }, g.OwnedFormats);
        CollectionAssert.AreEquivalent(new[] { "vimm", "archive" }, g.OwnedSources);
    }

    [TestMethod]
    public async Task GetGames_EmptyFormatsAndSources_WhenNeitherBoundNorOwned()
    {
        var snes = await Seed("snes");
        await AddGame(snes, "Bare Game");

        var (_, games) = await _repo.GetGamesAsync("snes", null, "all", false, false, false, 0, 100);
        var g = games.Single(x => x.Name == "Bare Game");

        Assert.IsEmpty(g.AvailableFormats);
        Assert.IsEmpty(g.OwnedFormats);
        Assert.IsEmpty(g.OwnedSources);
    }

    // --- seeding / verification helpers (direct SQL) ---

    private async Task AddCompleted(long gameId, int format, string source, string filename) =>
        await Exec($"INSERT INTO completed_urls (url, filename, format, source, source_id, game_id) " +
                   $"VALUES ('u-{gameId}-{format}', '{filename}', {format}, '{source}', 'sid-{gameId}-{format}', {gameId})");

    private async Task<long> Seed(string console) =>
        await ScalarLong($"INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT {console}', '{console}', 'no-intro') RETURNING id");

    private async Task<long> AddGame(long systemId, string name) =>
        await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({systemId}, '{name}') RETURNING id");

    private async Task AddRom(long gameId, string name, string crc, string md5, string sha1) =>
        await Exec($"INSERT INTO catalog_rom (game_id, name, size, crc, md5, sha1) VALUES ({gameId}, '{name}', 1, '{crc}', '{md5}', '{sha1}')");

    private async Task<(long Vault, string Match)> GameBinding(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT vault_id, vimm_match FROM catalog_game WHERE id = {gameId}";
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt64(0), r.GetString(1));
    }

    private async Task<string?> Match(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT vimm_match FROM catalog_game WHERE id = {gameId}";
        var v = await cmd.ExecuteScalarAsync();
        return v == DBNull.Value ? null : (string?)v;
    }

    private async Task<List<(int Alt, string Label, long Size)>> Formats(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT alt, label, size_bytes FROM catalog_vimm_format WHERE game_id = {gameId} ORDER BY alt";
        var list = new List<(int, string, long)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private async Task<long> ScalarLong(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task Exec(string sql)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
