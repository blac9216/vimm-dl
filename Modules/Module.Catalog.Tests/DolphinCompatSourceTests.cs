[TestClass]
public class DolphinCompatSourceTests
{
    [TestMethod]
    [DataRow(5, "Playable")]
    [DataRow(4, "Playable")]
    [DataRow(3, "Ingame")]
    [DataRow(2, "Intro")]
    [DataRow(1, "Nothing")]
    public void MapStars_MapsRatedCategories(int stars, string expected)
        => Assert.AreEqual(expected, DolphinCompatSource.MapStars(stars));

    [TestMethod]
    public void MapStars_ZeroOrUnknown_IsSkipped()
    {
        Assert.IsNull(DolphinCompatSource.MapStars(0)); // unrated/ambiguous default
        Assert.IsNull(DolphinCompatSource.MapStars(6));
    }

    [TestMethod]
    public void BuildUrl_EncodesCategory_AndContinuation()
    {
        var first = DolphinCompatSource.BuildUrl(5, null);
        StringAssert.Contains(first, "cmtitle=Category%3A5%20stars%20%28Rating%29");
        Assert.DoesNotContain("cmcontinue", first);

        var next = DolphinCompatSource.BuildUrl(5, "page|abc|123");
        StringAssert.Contains(next, "cmcontinue=page%7Cabc%7C123"); // '|' → %7C
    }

    [TestMethod]
    public void ParsePage_ExtractsTitlesAndContinuation()
    {
        const string json = """
            {"continue":{"cmcontinue":"page|next","continue":"-||"},
             "query":{"categorymembers":[{"pageid":1,"ns":0,"title":"Super Mario Sunshine"},
                                         {"pageid":2,"ns":0,"title":"Metroid Prime"}]}}
            """;
        var (titles, cont) = DolphinCompatSource.ParsePage(json);
        CollectionAssert.AreEquivalent(new[] { "Super Mario Sunshine", "Metroid Prime" }, titles);
        Assert.AreEqual("page|next", cont);
    }

    [TestMethod]
    public void ParsePage_NoContinuation_ReturnsNull()
    {
        var (titles, cont) = DolphinCompatSource.ParsePage("""{"query":{"categorymembers":[{"title":"Pikmin"}]}}""");
        Assert.AreEqual("Pikmin", titles.Single());
        Assert.IsNull(cont);
    }

    [TestMethod]
    public async Task LoadAsync_PagesEachRatedCategory_NormalizesNames_SkipsZeroStars()
    {
        var fetched = new List<string>();
        CompatFetch fake = (url, ct) =>
        {
            fetched.Add(url);
            if (url.Contains("cmcontinue")) return Task.FromResult(Page(["Wind Waker, The"], null)); // 5★ page 2
            if (url.Contains("5%20stars")) return Task.FromResult(Page(["Super Mario Sunshine", "Metroid Prime"], "P2"));
            if (url.Contains("4%20stars")) return Task.FromResult(Page(["Mario Kart - Double Dash!!"], null));
            if (url.Contains("3%20stars")) return Task.FromResult(Page(["Sonic Adventure 2 Battle"], null));
            if (url.Contains("2%20stars")) return Task.FromResult(Page(["Intro Only Game"], null));
            if (url.Contains("1%20stars")) return Task.FromResult(Page(["Totally Broken"], null));
            throw new InvalidOperationException($"unexpected fetch: {url}");
        };

        var map = (await new DolphinCompatSource().LoadAsync(fake, CancellationToken.None))
            .ToDictionary(e => e.MatchKey, e => e.Status);

        // 0 stars is never fetched (no "0%20stars" URL requested).
        Assert.IsFalse(fetched.Any(u => u.Contains("0%20stars")));
        // Names bridged via Dedup.TitleKey (lowercased, punctuation collapsed) so they join catalog titles.
        Assert.AreEqual("Playable", map["super mario sunshine"]);
        Assert.AreEqual("Playable", map["metroid prime"]);
        Assert.AreEqual("Playable", map["wind waker the"]);          // pagination followed
        Assert.AreEqual("Playable", map["mario kart double dash"]);  // "!!" + "-" collapsed
        Assert.AreEqual("Ingame", map["sonic adventure 2 battle"]);
        Assert.AreEqual("Intro", map["intro only game"]);
        Assert.AreEqual("Nothing", map["totally broken"]);
        Assert.HasCount(7, map);
    }

    [TestMethod]
    public void Source_FeedsRegisteredNameKeyedMultiConsoleEmulator()
    {
        var emu = Emulators.ById(new DolphinCompatSource().EmulatorId);
        Assert.IsNotNull(emu);
        Assert.AreEqual(CompatMatchKind.Name, emu!.MatchKind);
        CollectionAssert.AreEquivalent(new[] { "gc", "wii" }, emu.Consoles.ToList()); // GameCube + Wii
    }

    // Build a categorymembers JSON page from titles (+ optional continuation token).
    private static string Page(string[] titles, string? cont)
    {
        var members = string.Join(",", titles.Select(t => $"{{\"ns\":0,\"title\":\"{t}\"}}"));
        var continuation = cont is null ? "" : $",\"continue\":{{\"cmcontinue\":\"{cont}\",\"continue\":\"-||\"}}";
        return $"{{\"query\":{{\"categorymembers\":[{members}]}}{continuation}}}";
    }
}
