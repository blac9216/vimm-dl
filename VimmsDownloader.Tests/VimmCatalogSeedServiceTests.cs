using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Integration test for <see cref="VimmCatalogSeedService"/> — Vimm as the <b>authoritative</b> catalog
/// source for a console with no public DAT (Wii U discs), against a temp-file catalog DB and a stubbed
/// Vimm. Covers row creation from a scrape (name/serial/hashes), the origin + system provenance that
/// keeps these rows distinguishable from DAT-backed ones, vault/format binding, multi-disc titles, and
/// the refusal to wipe an existing catalog when a scrape comes back empty.
/// </summary>
[TestClass]
public class VimmCatalogSeedServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"vimmseed-{Guid.NewGuid():N}");
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

    private VimmCatalogSeedService NewService(Func<string, string?> route) =>
        new(_repo, new FakeHttpClientFactory(new StubHandler(route)),
            NullLogger<VimmCatalogSeedService>.Instance)
        { PoliteDelayMs = 0, UtcNow = () => new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc) };

    [TestMethod]
    public async Task Seed_CreatesGamesFromVimm_WithHashesAndProvenance()
    {
        await NewService(Route).SeedAsync("wiiu", default);

        // The system is filed under its own dat_name with source 'vimm' — never mistaken for a DAT.
        var system = await SystemRow("Nintendo - Wii U (Discs)");
        Assert.AreEqual(("wiiu", "vimm", "2026-08-01"), system);

        // The game's name is the canonical GoodTitle minus its extension, exactly as a DAT would hold it.
        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(2, games);
        Assert.Contains("Adventure Time - Explore the Dungeon (USA)", games);

        // Vimm's published hashes land in catalog_rom unchanged — the whole point of treating it as
        // authoritative rather than inventing a parallel storage shape.
        var roms = await RomsOf("Adventure Time - Explore the Dungeon (USA)");
        Assert.HasCount(1, roms);
        Assert.AreEqual(("Adventure Time - Explore the Dungeon (USA).wux", "463276da",
            "c381cad41fbd27dd54bad1430e0fc2f8", "0dde5b0e5db659e1b8d49e8f4e1946d9d4615602"), roms[0]);

        // Provenance: origin 'vimm' on the game, so a future DAT can supersede it in place.
        CollectionAssert.AreEqual(new[] { "vimm" }, await OriginsOf("Adventure Time - Explore the Dungeon (USA)"));
    }

    [TestMethod]
    public async Task Seed_BindsVaultIdAndFormats()
    {
        await NewService(Route).SeedAsync("wiiu", default);

        var (vault, match) = await BindingOf("Adventure Time - Explore the Dungeon (USA)");
        Assert.AreEqual(128227L, vault);
        Assert.AreEqual("sha1", match);      // bound back through the hashes we just stored

        var formats = await FormatsOf("Adventure Time - Explore the Dungeon (USA)");
        Assert.HasCount(1, formats);
        Assert.AreEqual((0, ".wux", 2100000L), formats[0]);
    }

    [TestMethod]
    public async Task Seed_MultiDiscTitle_TakesHashesFromHashes2()
    {
        await NewService(Route).SeedAsync("wiiu", default);

        var roms = await RomsOf("Two Disc Game (USA)");
        Assert.HasCount(2, roms);
        Assert.AreEqual("Two Disc Game (USA) (Disc 1).wud", roms[0].Name);
        Assert.AreEqual("aaaa1111", roms[0].Crc);
        Assert.AreEqual("Two Disc Game (USA) (Disc 2).wud", roms[1].Name);
    }

    [TestMethod]
    public async Task Seed_UnknownConsole_DoesNothing()
    {
        await NewService(Route).SeedAsync("snes", default);   // snes is DAT-sourced, not Vimm-authoritative
        Assert.IsNull(await SystemRow("Nintendo - Wii U (Discs)"));
    }

    /// <summary>
    /// A failed scrape must not look like "Vimm no longer lists anything": merging an empty set would
    /// strip the 'vimm' origin from every row and delete the ones left with no other origin.
    /// </summary>
    [TestMethod]
    public async Task Seed_EmptyScrape_LeavesExistingCatalogIntact()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        await NewService(_ => "").SeedAsync("wiiu", default);   // every fetch returns an empty list

        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));
    }

    [TestMethod]
    public async Task Seed_IsIdempotent()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        await NewService(Route).SeedAsync("wiiu", default);

        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));
        Assert.HasCount(1, await RomsOf("Adventure Time - Explore the Dungeon (USA)"));
    }

    [TestMethod]
    public async Task Seed_ReportsProgressPerSection()
    {
        var seen = new List<(string? Message, int? Current, int? Total)>();
        await NewService(Route).SeedAsync("wiiu", (m, c, t) => seen.Add((m, c, t)), default);

        Assert.HasCount(27, seen);                                  // A–Z + "number"
        Assert.IsTrue(seen.All(s => s.Total == 27));
        Assert.IsTrue(seen.All(s => s.Message!.StartsWith("wiiu: ")));
    }

    [TestMethod]
    [DataRow("Some Game (USA).wux", "Some Game (USA)")]
    [DataRow("Name.With.Dots (Europe).wud", "Name.With.Dots (Europe)")]
    [DataRow("", "FALLBACK")]
    [DataRow(null, "FALLBACK")]
    public void GameNameFrom_StripsExtension_OrFallsBack(string? goodTitle, string expected)
        => Assert.AreEqual(expected, VimmCatalogSeedService.GameNameFrom(goodTitle, "FALLBACK"));

    // --- stubbed Vimm ---

    private static string? Route(string url)
    {
        if (url.Contains("p=list"))
        {
            if (url.Contains("system=WiiU") && url.Contains("section=A")) return ListA;
            if (url.Contains("system=WiiU") && url.Contains("section=T")) return ListT;
            return "";
        }
        if (url.EndsWith("/vault/128227")) return AdventureTimeMedia;
        if (url.EndsWith("/vault/222222")) return TwoDiscMedia;
        if (url.Contains("hashes2.php?id=5001")) return TwoDiscHashes2;
        return null;
    }

    private const string ListA =
        """<tr><td><a href= "/vault/128227">Adventure Time</a></td></tr>""";
    private const string ListT =
        """<tr><td><a href= "/vault/222222">Two Disc Game</a></td></tr>""";

    // GoodTitle base64 = "Adventure Time - Explore the Dungeon (USA).wux"
    private const string AdventureTimeMedia =
        """<script>let media=[{"ID":4001,"GoodTitle":"QWR2ZW50dXJlIFRpbWUgLSBFeHBsb3JlIHRoZSBEdW5nZW9uIChVU0EpLnd1eA==","Serial":"WUP-P-ADVE-USA-0","Zipped":"2100000","AltZipped":"0","AltZipped2":"0","GoodHash":"463276DA","GoodMd5":"C381CAD41FBD27DD54BAD1430E0FC2F8","GoodSha1":"0DDE5B0E5DB659E1B8D49E8F4E1946D9D4615602","ZippedText":"2.01 GB"}];</script>""";
    // Multi-disc: no inline hashes → the service must fetch hashes2.php for this media id.
    private const string TwoDiscMedia =
        """<script>let media=[{"ID":5001,"GoodTitle":"VHdvIERpc2MgR2FtZSAoVVNBKS53dWQ=","Serial":"WUP-P-TWOD-USA-0","Zipped":"3000000","AltZipped":"0","AltZipped2":"0","ZippedText":"3 GB"}];</script>""";
    private const string TwoDiscHashes2 =
        """<div style="grid-column:span 2">Two Disc Game (USA) (Disc 1).wud</div><div>Crc</div><div>aaaa1111</div><div>Md5</div><div>bbbb2222bbbb2222bbbb2222bbbb2222</div><div>Sha1</div><div>cccc3333cccc3333cccc3333cccc3333cccc3333</div><div style="grid-column:span 2">Two Disc Game (USA) (Disc 2).wud</div><div>Crc</div><div>dddd4444</div><div>Md5</div><div>eeee5555eeee5555eeee5555eeee5555</div><div>Sha1</div><div>ffff6666ffff6666ffff6666ffff6666ffff6666</div>""";

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<string, string?> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = route(req.RequestUri!.ToString());
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    // --- verification helpers ---

    private async Task<(string Console, string Source, string? Version)?> SystemRow(string datName)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT console, source, dat_version FROM catalog_system WHERE dat_name = $d";
        cmd.Parameters.AddWithValue("$d", datName);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
    }

    private async Task<List<string>> GamesOfSystem(string datName)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT g.name FROM catalog_game g
            JOIN catalog_system s ON s.id = g.system_id
            WHERE s.dat_name = $d ORDER BY g.name
            """;
        cmd.Parameters.AddWithValue("$d", datName);
        return await ReadStrings(cmd);
    }

    private async Task<List<(string Name, string? Crc, string? Md5, string? Sha1)>> RomsOf(string gameName)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT r.name, r.crc, r.md5, r.sha1 FROM catalog_rom r
            JOIN catalog_game g ON g.id = r.game_id
            WHERE g.name = $n ORDER BY r.name
            """;
        cmd.Parameters.AddWithValue("$n", gameName);
        var list = new List<(string, string?, string?, string?)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add((r.GetString(0), Str(r, 1), Str(r, 2), Str(r, 3)));
        return list;
    }

    private async Task<string[]> OriginsOf(string gameName)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT src.origin FROM catalog_game_source src
            JOIN catalog_game g ON g.id = src.game_id
            WHERE g.name = $n ORDER BY src.origin
            """;
        cmd.Parameters.AddWithValue("$n", gameName);
        return [.. await ReadStrings(cmd)];
    }

    private async Task<(long Vault, string? Match)> BindingOf(string gameName)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT vault_id, vimm_match FROM catalog_game WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", gameName);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.IsDBNull(0) ? 0L : r.GetInt64(0), Str(r, 1));
    }

    private async Task<List<(int Alt, string Label, long Size)>> FormatsOf(string gameName)
    {
        await using var db = await OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT f.alt, f.label, f.size_bytes FROM catalog_vimm_format f
            JOIN catalog_game g ON g.id = f.game_id
            WHERE g.name = $n ORDER BY f.alt
            """;
        cmd.Parameters.AddWithValue("$n", gameName);
        var list = new List<(int, string, long)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static async Task<List<string>> ReadStrings(SqliteCommand cmd)
    {
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        return db;
    }
}
