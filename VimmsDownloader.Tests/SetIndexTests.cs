using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core;
using Module.Download.Sources;

namespace VimmsDownloader.Tests;

/// <summary>
/// Set-content indexing (#289): the REAL <see cref="CatalogRepository"/> set-index methods (migration
/// 032) against a temp SQLite file with the real migrations applied, <see cref="SetIndexService"/>'s
/// hash-first/name-fallback matching against a fake archive source, and
/// <see cref="CatalogResolveService.ResolveForQueueAsync"/>'s fast path — an indexed, matched game must
/// resolve without ever touching <see cref="ICatalogSource.ListFilesAsync"/> or HTTP.
/// </summary>
[TestClass]
public class SetIndexTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _repo = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"setidx-{Guid.NewGuid():N}");
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

    // --- CatalogRepository: catalog_set_file CRUD + archive_match application ---

    [TestMethod]
    public async Task ReplaceSetLinkFiles_RoundTrips_AndStampsIndexedAt()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        var setId = await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/some_item"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        Assert.IsNull(await IndexedAt(link.Id)); // never indexed yet

        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Game.zip", 100, "crc1", "md5-1", "sha1-1", game, "sha1")], default);

        var rows = await AllFileRows(link.Id);
        Assert.HasCount(1, rows);
        Assert.AreEqual(("Game.zip", 100L, game, "sha1"), rows[0]);
        Assert.IsNotNull(await IndexedAt(link.Id));
        _ = setId;
    }

    [TestMethod]
    public async Task ReplaceSetLinkFiles_SecondCall_ReplacesWholesale()
    {
        var setId = await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/some_item"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id, [new CatalogRepository.SetFileRow("A.zip", 1, null, null, null, null, null)], default);

        await _repo.ReplaceSetLinkFilesAsync(link.Id, [new CatalogRepository.SetFileRow("B.zip", 2, null, null, null, null, null)], default);

        var rows = await AllFileRows(link.Id);
        Assert.HasCount(1, rows);
        Assert.AreEqual("B.zip", rows[0].Name);
        _ = setId;
    }

    [TestMethod]
    public async Task ApplyArchiveMatches_SetsMatched_AndFlagsRestAsNone()
    {
        var snes = await Seed("snes");
        var matched = await AddGame(snes, "Matched");
        var unmatched = await AddGame(snes, "Unmatched");
        var ps = await Seed("psx");
        var other = await AddGame(ps, "OtherConsole");

        await _repo.ApplyArchiveMatchesAsync("snes", [(matched, "sha1")], default);

        Assert.AreEqual("sha1", await ArchiveMatch(matched));
        Assert.AreEqual("none", await ArchiveMatch(unmatched));
        Assert.IsNull(await ArchiveMatch(other)); // untouched — different console
    }

    [TestMethod]
    public async Task ApplyArchiveMatches_SecondRun_ResetsPriorMatches()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        await _repo.ApplyArchiveMatchesAsync("snes", [(game, "sha1")], default);
        Assert.AreEqual("sha1", await ArchiveMatch(game));

        // A re-index where the game no longer appears in any listing → falls back to 'none', not stuck sha1.
        await _repo.ApplyArchiveMatchesAsync("snes", [], default);

        Assert.AreEqual("none", await ArchiveMatch(game));
    }

    [TestMethod]
    public async Task GetGames_SurfacesArchiveMatch_ForTheBadge()
    {
        var snes = await Seed("snes");
        var bound = await AddGame(snes, "Bound Game");
        var unbound = await AddGame(snes, "Unbound Game");
        await _repo.ApplyArchiveMatchesAsync("snes", [(bound, "name")], default);

        var (_, games) = await _repo.GetGamesAsync("snes", null, "all", false, false, false, "substring", 0, 100);
        var byName = games.ToDictionary(g => g.Name);

        Assert.AreEqual("name", byName["Bound Game"].ArchiveMatch);
        Assert.AreEqual("none", byName["Unbound Game"].ArchiveMatch);
    }

    [TestMethod]
    public async Task GetIndexedArchiveFile_ReturnsNameAndLinkUrl_OrNull()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/some_item"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Game.zip", 100, null, null, "sha1-1", game, "sha1")], default);

        var found = await _repo.GetIndexedArchiveFileAsync(game, default);
        Assert.AreEqual(("Game.zip", "https://archive.org/download/some_item"), found);

        var other = await AddGame(snes, "Not Indexed");
        Assert.IsNull(await _repo.GetIndexedArchiveFileAsync(other, default));
    }

    [TestMethod]
    public async Task UpdateSet_InvalidatesPriorIndexRows_AndClearsArchiveMatch()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        var setId = await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/some_item"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Game.zip", 1, null, null, "s1", game, "sha1")], default);
        await _repo.ApplyArchiveMatchesAsync("snes", [(game, "sha1")], default);
        Assert.AreEqual("sha1", await ArchiveMatch(game));

        Assert.IsTrue(await _repo.UpdateSetAsync((int)setId, "Set", "snes", ["https://archive.org/download/other_item"]));

        Assert.IsEmpty(await AllFileRows(link.Id));      // old link's rows gone (link itself replaced too)
        Assert.IsNull(await ArchiveMatch(game));          // stale marker cleared, not left dangling
    }

    [TestMethod]
    public async Task DeleteSet_InvalidatesIndexRows_AndClearsArchiveMatch()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        var setId = await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/some_item"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Game.zip", 1, null, null, "s1", game, "sha1")], default);
        await _repo.ApplyArchiveMatchesAsync("snes", [(game, "sha1")], default);

        Assert.IsTrue(await _repo.DeleteSetAsync((int)setId));

        Assert.IsNull(await ArchiveMatch(game));
        Assert.IsNull(await _repo.GetIndexedArchiveFileAsync(game, default));
    }

    [TestMethod]
    public async Task Migration032_CreatesOnlyTheIndexesTheHotPathsUse()
    {
        // Review finding 5: matching is in-memory against catalog_rom and nothing selects from
        // catalog_set_file by hash, so the per-hash B-trees were pure insert cost on a table that takes
        // thousands of rows per link. link_id (replace/invalidate) and game_id (fast resolve) stay.
        var indexes = await IndexNames("catalog_set_file");

        CollectionAssert.Contains(indexes, "idx_catalog_set_file_link");
        CollectionAssert.Contains(indexes, "idx_catalog_set_file_game");
        CollectionAssert.DoesNotContain(indexes, "idx_catalog_set_file_sha1");
        CollectionAssert.DoesNotContain(indexes, "idx_catalog_set_file_md5");
        CollectionAssert.DoesNotContain(indexes, "idx_catalog_set_file_crc");
    }

    [TestMethod]
    public async Task Migration032_RerunsCleanly_AndDropsHashIndexesLeftByItsEarlierForm()
    {
        // 032 never shipped in a release, so it is edited in place rather than superseded — but the
        // migrator runs a file once BY NAME, so dev databases that ran its earlier form still carry the
        // three hash indexes. The DROP IF EXISTS statements in the same file converge them the moment it
        // re-runs (the migrator's own idempotency contract: a missing schema_migrations row replays it),
        // and are harmless no-ops on a fresh database. Both directions are asserted here.
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();

        // Recreate the pre-review state: the indexes exist and 032 is marked executed.
        await using (var old = db.CreateCommand())
        {
            old.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_catalog_set_file_sha1 ON catalog_set_file(sha1);
                CREATE INDEX IF NOT EXISTS idx_catalog_set_file_md5 ON catalog_set_file(md5);
                CREATE INDEX IF NOT EXISTS idx_catalog_set_file_crc ON catalog_set_file(crc);
                DELETE FROM schema_migrations WHERE name LIKE '032%';
                """;
            await old.ExecuteNonQueryAsync();
        }

        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance); // must not throw on the existing table/columns

        var indexes = await IndexNames("catalog_set_file");
        CollectionAssert.DoesNotContain(indexes, "idx_catalog_set_file_sha1");
        CollectionAssert.DoesNotContain(indexes, "idx_catalog_set_file_md5");
        CollectionAssert.DoesNotContain(indexes, "idx_catalog_set_file_crc");
        CollectionAssert.Contains(indexes, "idx_catalog_set_file_link"); // the kept ones survive a re-run
        CollectionAssert.Contains(indexes, "idx_catalog_set_file_game");

        // And the table still works after the replay — columns intact, not dropped/recreated empty.
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/item"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Game.zip", 1, null, null, "s1", game, "sha1")], default);
        Assert.HasCount(1, await AllFileRows(link.Id));
    }

    [TestMethod]
    public async Task GetIndexedArchiveFile_MultipleSets_PrefersStrongestMatchKind()
    {
        // Review finding 6: LIMIT 1 without ORDER BY returned whichever row SQLite happened to yield, so
        // a game reported as 'sha1' could resolve to the CRC- or name-matched file from another set.
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        await _repo.AddSetAsync("A Set", "snes", ["https://archive.org/download/weak_item"]);
        await _repo.AddSetAsync("B Set", "snes", ["https://archive.org/download/strong_item"]);
        var links = await _repo.GetLinksForConsoleAsync("snes", default);
        await _repo.ReplaceSetLinkFilesAsync(links[0].Id,
            [new CatalogRepository.SetFileRow("weak.zip", 1, "c1", null, null, game, "crc")], default);
        await _repo.ReplaceSetLinkFilesAsync(links[1].Id,
            [new CatalogRepository.SetFileRow("strong.zip", 1, null, null, "s1", game, "sha1")], default);

        // Deterministic regardless of which link was indexed first — the sha1 row wins on kind, not order.
        Assert.AreEqual("strong.zip", (await _repo.GetIndexedArchiveFileAsync(game, default))!.Value.Name);
    }

    [TestMethod]
    public async Task DeleteSet_AnotherSetStillCarriesTheGame_RederivesMarkerInsteadOfClearingIt()
    {
        // Review finding 3: invalidation used to blanket-null archive_match, dropping the badge of a game
        // a surviving set still carries. The marker must be re-derived from the rows that remain — here
        // 'sha1' (from the deleted set) → 'crc' (what the surviving set actually offers).
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        var strongSet = await _repo.AddSetAsync("A Set", "snes", ["https://archive.org/download/strong_item"]);
        await _repo.AddSetAsync("B Set", "snes", ["https://archive.org/download/weak_item"]);
        var links = await _repo.GetLinksForConsoleAsync("snes", default);
        await _repo.ReplaceSetLinkFilesAsync(links[0].Id,
            [new CatalogRepository.SetFileRow("strong.zip", 1, null, null, "s1", game, "sha1")], default);
        await _repo.ReplaceSetLinkFilesAsync(links[1].Id,
            [new CatalogRepository.SetFileRow("weak.zip", 1, "c1", null, null, game, "crc")], default);
        await _repo.ApplyArchiveMatchesAsync("snes", [(game, "sha1")], default);

        Assert.IsTrue(await _repo.DeleteSetAsync((int)strongSet));

        Assert.AreEqual("crc", await ArchiveMatch(game));                     // not NULL: B Set still has it
        Assert.AreEqual("weak.zip", (await _repo.GetIndexedArchiveFileAsync(game, default))!.Value.Name);
    }

    [TestMethod]
    public async Task DeleteSet_LastSetForConsole_ReturnsOrphanedNoneMarkersToNull()
    {
        // Review finding 4: 'none' means "indexed and absent". Once the console's last set is gone there
        // is nothing it could be absent from, so the truthful state is NULL ("not yet indexed").
        var snes = await Seed("snes");
        var missing = await AddGame(snes, "Never In A Set");
        var setId = await _repo.AddSetAsync("Only Set", "snes", ["https://archive.org/download/item"]);
        await _repo.ApplyArchiveMatchesAsync("snes", [], default);
        Assert.AreEqual("none", await ArchiveMatch(missing));

        Assert.IsTrue(await _repo.DeleteSetAsync((int)setId));

        Assert.IsNull(await ArchiveMatch(missing));
    }

    [TestMethod]
    public async Task DeleteSet_ConsoleStillHasAnotherSet_KeepsNoneMarkers()
    {
        // The flip side of finding 4: with a set still configured, 'none' is still a true statement about
        // the console, so the orphan sweep must not fire.
        var snes = await Seed("snes");
        var missing = await AddGame(snes, "Never In A Set");
        var setId = await _repo.AddSetAsync("First", "snes", ["https://archive.org/download/item"]);
        await _repo.AddSetAsync("Second", "snes", ["https://archive.org/download/other"]);
        await _repo.ApplyArchiveMatchesAsync("snes", [], default);

        Assert.IsTrue(await _repo.DeleteSetAsync((int)setId));

        Assert.AreEqual("none", await ArchiveMatch(missing));
    }

    [TestMethod]
    public async Task GetIndexableConsoles_ReturnsOnlyConsolesWithASet()
    {
        await _repo.AddSetAsync("A", "snes", ["https://archive.org/download/x"]);
        await _repo.AddSetAsync("B", "psx", ["https://archive.org/download/y"]);

        var consoles = await _repo.GetIndexableConsolesAsync(default);

        CollectionAssert.AreEquivalent(new[] { "snes", "psx" }, consoles);
    }

    // --- SetIndexService: hash-first, name fallback, best-of-multiple-links ---

    [TestMethod]
    public async Task IndexAsync_HashMatch_TakesPriorityOverName()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Chrono Trigger");
        await AddRom(game, "Chrono Trigger (USA).sfc", crc: "AABBCCDD", md5: "m1", sha1: "s1");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/romset"]);

        // File name deliberately does NOT match the game's name — only the hash does — proving the
        // hash path (not name-stem) is what bound it.
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>
        {
            ["romset"] = [new CatalogFile("totally-renamed.zip", 1000, "https://archive.org/download/romset/totally-renamed.zip", Sha1: "s1")],
        });

        await NewService(source).IndexAsync("snes", null, default);

        Assert.AreEqual("sha1", await ArchiveMatch(game));
        var indexed = await _repo.GetIndexedArchiveFileAsync(game, default);
        Assert.AreEqual("totally-renamed.zip", indexed!.Value.Name);
    }

    [TestMethod]
    public async Task IndexAsync_NoHash_FallsBackToNameStem()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Super Metroid (USA)"); // No-Intro names carry the region tag
        await AddRom(game, "Super Metroid (USA).sfc", crc: "11111111", md5: "m", sha1: "s"); // hash won't match — listing carries none
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/romset"]);

        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>
        {
            ["romset"] = [new CatalogFile("Super Metroid (USA).zip", 1000, "https://archive.org/download/romset/Super%20Metroid%20(USA).zip")],
        });

        await NewService(source).IndexAsync("snes", null, default);

        Assert.AreEqual("name", await ArchiveMatch(game));
    }

    [TestMethod]
    public async Task IndexAsync_UnlistedGame_FlaggedNone()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Not In Any Set");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/romset"]);
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>> { ["romset"] = [] });

        await NewService(source).IndexAsync("snes", null, default);

        Assert.AreEqual("none", await ArchiveMatch(game));
    }

    [TestMethod]
    public async Task IndexAsync_BestMatchAcrossLinks_Sha1BeatsCrcFromAnotherLink()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        await AddRom(game, "Game (USA).sfc", crc: "CAFEBABE", md5: "m1", sha1: "s1");
        await _repo.AddSetAsync("Set", "snes",
            ["https://archive.org/download/set_a", "https://archive.org/download/set_b"]);

        // set_a matches by CRC only; set_b matches the SAME game by SHA1 — the stronger kind must win
        // regardless of link order.
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>
        {
            ["set_a"] = [new CatalogFile("Game (USA).zip", 1, "u1", Crc32: "cafebabe")],
            ["set_b"] = [new CatalogFile("Game (USA).zip", 1, "u2", Sha1: "s1")],
        });

        await NewService(source).IndexAsync("snes", null, default);

        Assert.AreEqual("sha1", await ArchiveMatch(game));
    }

    [TestMethod]
    public async Task IndexAsync_NonArchiveLink_Skipped()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Game");
        await _repo.AddSetAsync("Set", "snes", ["https://lolroms.com/Nintendo%20-%20SNES"]);
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>()); // never queried

        await NewService(source).IndexAsync("snes", null, default);

        Assert.IsFalse(source.WasQueried);
        Assert.AreEqual("none", await ArchiveMatch(game)); // console still gets its "no match" pass
    }

    [TestMethod]
    public async Task IndexAsync_ListingWithMoreThan200Files_PersistsEveryOne()
    {
        // Review blocker: ArchiveSource.ListFilesAsync used to truncate at 200 files, so a run against a
        // real set (the seeded NDS default holds ~7.3k files) indexed 200 and left the rest of the console
        // marked 'none'. This pins the whole listing reaching the table — and the 201st+ file matching.
        var snes = await Seed("snes");
        var lateGame = await AddGame(snes, "Late Game");           // deliberately the 500th file
        await AddRom(lateGame, "Late Game (USA).sfc", crc: "c", md5: "m", sha1: "late-sha1");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/bigset"]);

        var files = Enumerable.Range(0, 500)
            .Select(i => new CatalogFile($"Filler {i:D4}.zip", 1, $"u{i}", Sha1: $"filler-{i}"))
            .Append(new CatalogFile("Late Game (USA).zip", 1, "ulate", Sha1: "late-sha1"))
            .ToList();
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>> { ["bigset"] = files });

        await NewService(source).IndexAsync("snes", null, default);

        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        Assert.HasCount(501, await AllFileRows(link.Id));         // every file, not the first 200
        Assert.AreEqual("sha1", await ArchiveMatch(lateGame));    // and a late file still binds its game
    }

    [TestMethod]
    public async Task IndexAsync_OneLinkFails_GameCarriedOnlyByItKeepsItsMarker()
    {
        // Review finding 2: the failed link's rows survived by design, but the recompute ran off the
        // successful links alone — so the game those surviving rows still resolve was flagged 'none'.
        var snes = await Seed("snes");
        var carriedByFailing = await AddGame(snes, "Only In The Failing Set");
        var carriedByWorking = await AddGame(snes, "In The Working Set");
        await AddRom(carriedByWorking, "In The Working Set (USA).sfc", crc: "c", md5: "m", sha1: "works-sha1");
        await _repo.AddSetAsync("A Working", "snes", ["https://archive.org/download/works"]);
        await _repo.AddSetAsync("B Broken", "snes", ["https://archive.org/download/broken"]);
        var links = await _repo.GetLinksForConsoleAsync("snes", default);

        // The broken link was indexed successfully at some earlier point.
        await _repo.ReplaceSetLinkFilesAsync(links[1].Id,
            [new CatalogRepository.SetFileRow("Only In The Failing Set.zip", 1, null, null, "s1", carriedByFailing, "sha1")], default);
        await _repo.ApplyArchiveMatchesAsync("snes", [(carriedByFailing, "sha1")], default);

        // Only "works" is listable now — "broken" fails (unknown identifier → Result.Fail).
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>
        {
            ["works"] = [new CatalogFile("In The Working Set (USA).zip", 1, "u", Sha1: "works-sha1")],
        });

        await NewService(source).IndexAsync("snes", null, default);

        Assert.AreEqual("sha1", await ArchiveMatch(carriedByWorking));  // fresh listing applied
        Assert.AreEqual("sha1", await ArchiveMatch(carriedByFailing));  // NOT downgraded to 'none'
        Assert.HasCount(1, await AllFileRows(links[1].Id));             // its rows survive, as before
        // The documented invariant holds: a bound file row still implies a non-'none' marker.
        Assert.IsNotNull(await _repo.GetIndexedArchiveFileAsync(carriedByFailing, default));
    }

    [TestMethod]
    public async Task IndexAsync_EveryLinkFails_LeavesTheConsolesMarkersAlone()
    {
        // Nothing was learned about the console, so rewriting its markers (which would flag everything
        // 'none') would be a lie. Previous state stands until a run actually lists something.
        var snes = await Seed("snes");
        var indexed = await AddGame(snes, "Previously Indexed");
        var absent = await AddGame(snes, "Previously Absent");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/broken"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Previously Indexed.zip", 1, null, null, "s1", indexed, "sha1")], default);
        await _repo.ApplyArchiveMatchesAsync("snes", [(indexed, "sha1")], default);
        Assert.AreEqual("none", await ArchiveMatch(absent));

        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>()); // every listing fails

        await NewService(source).IndexAsync("snes", null, default);

        Assert.AreEqual("sha1", await ArchiveMatch(indexed));
        Assert.AreEqual("none", await ArchiveMatch(absent));   // unchanged, not re-derived from nothing
    }

    [TestMethod]
    public async Task IndexAsync_ReportsProgressPerLink()
    {
        await _repo.AddSetAsync("Set", "snes",
            ["https://archive.org/download/set_a", "https://archive.org/download/set_b"]);
        var source = new FakeArchiveSource(new Dictionary<string, List<CatalogFile>>
        {
            ["set_a"] = [], ["set_b"] = [],
        });
        var calls = new List<(int? Current, int? Total)>();

        await NewService(source).IndexAsync("snes", (_, cur, tot) => calls.Add((cur, tot)), default);

        Assert.HasCount(2, calls);
        Assert.AreEqual((1, 2), calls[0]);
        Assert.AreEqual((2, 2), calls[1]);
    }

    // --- CatalogResolveService: fast path skips the live listing entirely ---

    [TestMethod]
    public async Task ResolveForQueue_IndexedGame_BuildsUrl_WithoutTouchingHttpOrRegistry()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Chrono Trigger");
        await _repo.AddSetAsync("Set", "snes", ["https://archive.org/download/romset"]);
        var link = (await _repo.GetLinksForConsoleAsync("snes", default)).Single();
        await _repo.ReplaceSetLinkFilesAsync(link.Id,
            [new CatalogRepository.SetFileRow("Chrono Trigger (USA).sfc.zip", 100, null, null, "s1", game, "sha1")], default);

        // Empty registry + a factory that throws if asked for a client — proves the fast path never
        // reaches ResolveAsync's live-listing loop.
        var resolver = new CatalogResolveService(_repo, new SourceRegistry([]), new ThrowingHttpClientFactory(),
            NullLogger<CatalogResolveService>.Instance);

        var r = await resolver.ResolveForQueueAsync((int)game, "snes", "Chrono Trigger", null, default);

        Assert.IsNotNull(r);
        Assert.AreEqual("archive", r.Value.Source);
        Assert.AreEqual(0, r.Value.Format);
        Assert.AreEqual(
            ArchiveSource.BuildDownloadUrl("romset", "Chrono Trigger (USA).sfc.zip"),
            r.Value.Url);
    }

    [TestMethod]
    public async Task ResolveForQueue_NotIndexed_FallsThroughToLiveListing_ThenNull()
    {
        var snes = await Seed("snes");
        var game = await AddGame(snes, "Never Indexed");
        // No sets configured at all → ResolveAsync short-circuits to null without touching HTTP either.
        var resolver = new CatalogResolveService(_repo, new SourceRegistry([]), new ThrowingHttpClientFactory(),
            NullLogger<CatalogResolveService>.Instance);

        var r = await resolver.ResolveForQueueAsync((int)game, "snes", "Never Indexed", null, default);

        Assert.IsNull(r);
    }

    // --- helpers ---

    private SetIndexService NewService(FakeArchiveSource source) =>
        new(_repo, new SourceRegistry([source]), new FakeHttpClientFactory(), NullLogger<SetIndexService>.Instance);

    private async Task<long> Seed(string console) =>
        await ScalarLong($"INSERT INTO catalog_system (dat_name, console, source) VALUES ('DAT {console}', '{console}', 'no-intro') RETURNING id");

    private async Task<long> AddGame(long systemId, string name) =>
        await ScalarLong($"INSERT INTO catalog_game (system_id, name) VALUES ({systemId}, '{name}') RETURNING id");

    private async Task AddRom(long gameId, string name, string crc, string md5, string sha1) =>
        await Exec($"INSERT INTO catalog_rom (game_id, name, size, crc, md5, sha1) VALUES ({gameId}, '{name}', 1, '{crc}', '{md5}', '{sha1}')");

    private async Task<string?> ArchiveMatch(long gameId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT archive_match FROM catalog_game WHERE id = {gameId}";
        var v = await cmd.ExecuteScalarAsync();
        return v == DBNull.Value ? null : (string?)v;
    }

    private async Task<string?> IndexedAt(long linkId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT indexed_at FROM catalog_set_link WHERE id = {linkId}";
        var v = await cmd.ExecuteScalarAsync();
        return v == DBNull.Value ? null : (string?)v;
    }

    private async Task<List<(string Name, long Size, long? GameId, string? MatchKind)>> AllFileRows(long linkId)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT name, size, game_id, match_kind FROM catalog_set_file WHERE link_id = {linkId}";
        var list = new List<(string, long, long?, string?)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add((r.GetString(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    private async Task<List<string>> IndexNames(string table)
    {
        await using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}'";
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
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

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("HTTP should not be called on the indexed fast path");
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoopHandler());
        private sealed class NoopHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    /// <summary>A stub archive.org catalog source keyed by item identifier — <see cref="SetIndexService"/>
    /// only ever calls <see cref="ListFilesAsync"/>, never resolves a download, so that's a stub.</summary>
    private sealed class FakeArchiveSource(Dictionary<string, List<CatalogFile>> byIdentifier) : IDownloadSource, ICatalogSource
    {
        public bool WasQueried { get; private set; }

        public string Id => "archive";
        public string DisplayName => "Internet Archive";
        public string HttpClientName => "archive";

        public Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
            => throw new NotSupportedException("the set-index job never resolves a download");

        public Task<Result<IReadOnlyList<CatalogSet>>> SearchSetsAsync(string query, HttpClient http, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<CatalogFile>>> ListFilesAsync(string setId, string? filter, HttpClient http, CancellationToken ct)
        {
            WasQueried = true;
            return Task.FromResult(byIdentifier.TryGetValue(setId, out var files)
                ? Result<IReadOnlyList<CatalogFile>>.Ok(files)
                : Result<IReadOnlyList<CatalogFile>>.Fail($"unknown set id {setId}"));
        }
    }
}
