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

    /// <summary>
    /// The same delete-on-merge hazard as an empty scrape, but partial: a transport failure that reads
    /// only some of the listed titles must not let the merge speak for the whole console, or the
    /// unreadable ones are deleted as though Vimm had delisted them.
    /// </summary>
    [TestMethod]
    public async Task Seed_PartialScrape_LeavesExistingCatalogIntact()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        // Both titles still listed, but one vault page now fails → below the completion threshold.
        var svc = NewService(url => url.EndsWith("/vault/222222") ? null : Route(url));
        await svc.SeedAsync("wiiu", default);

        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));
    }

    /// <summary>
    /// The worst partial case, and the one a completion ratio cannot catch: a section listing that
    /// fails contributes no titles to either counter, so the run looks 100% complete while an entire
    /// letter is missing. Merging then deletes that letter's games.
    /// </summary>
    [TestMethod]
    public async Task Seed_SectionListingFails_LeavesExistingCatalogIntact()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        // Section T's listing 404s; section A still lists and reads perfectly.
        var svc = NewService(url =>
            url.Contains("p=list") && url.Contains("section=T") ? null : Route(url));
        await svc.SeedAsync("wiiu", default);

        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(2, games);
        Assert.Contains("Two Disc Game (USA)", games);   // the unlisted section's game survives
    }

    /// <summary>
    /// #297's exact repro: a section listing doesn't fail transport-wise — it comes back HTTP 200 —
    /// but the body is a rate-limit interstitial, not a list page. Structurally indistinguishable from
    /// "legitimately empty" unless the response is sanity-checked against the list-page shape, so
    /// without that check this used to sail through every guard and delete the whole letter.
    /// </summary>
    [TestMethod]
    public async Task Seed_SectionListingReturns200ButNotAListPage_LeavesExistingCatalogIntact()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        // Section T's listing 200s with a WAF/rate-limit body instead of a real list page.
        var svc = NewService(url =>
            url.Contains("p=list") && url.Contains("section=T")
                ? "Too many requests, please slow down."
                : Route(url));
        await svc.SeedAsync("wiiu", default);

        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(2, games);
        Assert.Contains("Two Disc Game (USA)", games);   // the misread section's game survives
    }

    /// <summary>
    /// A title that reads cleanly but publishes no hashes is a legitimate skip, not a transport
    /// failure, so it must not count toward the incomplete-run threshold and block the merge.
    /// </summary>
    [TestMethod]
    public async Task Seed_TitleWithoutHashes_DoesNotBlockTheMerge()
    {
        // Section A's title structurally IS a vault page (carries the "media = [...]" declaration) but
        // the array is genuinely empty — a legitimate title-with-no-hashes, distinct from a page that
        // isn't a vault page at all. Section T's reads fine. 1 of 2 has no hashes, which is well past
        // the 10% threshold — yet the run must still be accepted.
        const string noMedia = "<script>let media=[];</script>";
        var svc = NewService(url => url.EndsWith("/vault/128227") ? noMedia : Route(url));

        await svc.SeedAsync("wiiu", default);

        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(1, games);
        Assert.Contains("Two Disc Game (USA)", games);
    }

    /// <summary>
    /// The sibling of the section-listing case: a title page 200s with a rate-limit body instead of a
    /// real vault page. Without a structural check this looks exactly like "publishes no hashes" (a
    /// legitimate skip that doesn't count against the run) and the title is silently dropped and then
    /// deleted by the merge; with the check it counts as unreadable, same as a fetch failure.
    /// </summary>
    [TestMethod]
    public async Task Seed_TitlePageReturns200ButNotAVaultPage_LeavesExistingCatalogIntact()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        // Two Disc Game's vault page 200s with a WAF/rate-limit body instead of real markup.
        var svc = NewService(url =>
            url.EndsWith("/vault/222222") ? "Too many requests, please slow down." : Route(url));
        await svc.SeedAsync("wiiu", default);

        // 1 of 2 listed titles unreadable — well past the 10% completion threshold — so the whole
        // run is refused rather than merging with Two Disc Game silently dropped.
        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(2, games);
        Assert.Contains("Two Disc Game (USA)", games);
    }

    /// <summary>
    /// The delete-cap guard (#297): even a run that reads everything it listed cleanly can still be an
    /// implausibly short list — a legitimate section delisting most of the console. Every earlier guard
    /// is satisfied (nothing failed to fetch, nothing came back unreadable), yet the merge must still be
    /// refused because it would drop far more of this origin's existing rows than the cap allows, and
    /// the refusal must be visible through <c>report</c> (the Jobs API surface), not just the log.
    /// </summary>
    [TestMethod]
    public async Task Seed_ScrapeWouldDeleteTooManyRows_RefusesAndReportsWhy()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        // Section T now legitimately lists nothing (a real, marker-bearing, empty list — not a fetch
        // failure), so only Adventure Time is scraped: a clean, complete read that would still drop 1
        // of the 2 rows this origin sources — 50%, over the 20% default cap.
        var svc = NewService(url =>
            url.Contains("p=list") && url.Contains("section=T") ? EmptyList : Route(url));
        var seen = new List<string?>();
        await svc.SeedAsync("wiiu", (m, _, _) => seen.Add(m), default);

        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(2, games);
        Assert.Contains("Two Disc Game (USA)", games);
        Assert.IsTrue(seen.Any(m => m is not null && m.Contains("refusing to merge")),
            "the refusal reason must reach Report, not just the log");
    }

    /// <summary>The same shrink as above, but with a cap wide enough that it falls comfortably under
    /// it: the merge proceeds, confirming the cap is a threshold and not a blanket ban on shrinking.</summary>
    [TestMethod]
    public async Task Seed_ScrapeDeletesJustUnderTheCap_Proceeds()
    {
        await NewService(Route).SeedAsync("wiiu", default);
        Assert.HasCount(2, await GamesOfSystem("Nintendo - Wii U (Discs)"));

        var svc = NewService(url =>
            url.Contains("p=list") && url.Contains("section=T") ? EmptyList : Route(url));
        svc.MaxOriginDeleteFraction = 0.6;   // 50% deleted is comfortably under a 60% cap
        await svc.SeedAsync("wiiu", default);

        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.HasCount(1, games);
        Assert.Contains("Adventure Time - Explore the Dungeon (USA)", games);
    }

    /// <summary>
    /// A scrape that reads everything it listed is allowed through, even if that shrank — this test
    /// isolates the completion-ratio guard, so the (separately-covered) delete-cap is relaxed here.
    /// </summary>
    [TestMethod]
    public async Task Seed_CompleteScrape_IsAccepted()
    {
        await NewService(Route).SeedAsync("wiiu", default);

        // Only section A lists anything now, and its one title reads fine → 1/1 complete.
        var svc = NewService(url =>
            url.Contains("p=list") ? (url.Contains("section=A") ? ListA : EmptyList) : Route(url));
        svc.MaxOriginDeleteFraction = 1.0;
        await svc.SeedAsync("wiiu", default);

        var games = await GamesOfSystem("Nintendo - Wii U (Discs)");
        Assert.Contains("Adventure Time - Explore the Dungeon (USA)", games);
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
            return EmptyList;
        }
        if (url.EndsWith("/vault/128227")) return AdventureTimeMedia;
        if (url.EndsWith("/vault/222222")) return TwoDiscMedia;
        if (url.Contains("hashes2.php?id=5001")) return TwoDiscHashes2;
        return null;
    }

    // Real list pages wrap rows in <table><caption>...</caption> — see VimmVaultParserTests' fixture.
    // A section with no games renders the same wrapper with no rows: EmptyList mirrors that shape and
    // is what makes it distinguishable, per #297, from a 200 OK body that isn't a list page at all.
    private const string EmptyList = "<table><caption></caption></table>";
    private const string ListA =
        """<table><caption></caption><tr><td><a href= "/vault/128227">Adventure Time</a></td></tr></table>""";
    private const string ListT =
        """<table><caption></caption><tr><td><a href= "/vault/222222">Two Disc Game</a></td></tr></table>""";

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
