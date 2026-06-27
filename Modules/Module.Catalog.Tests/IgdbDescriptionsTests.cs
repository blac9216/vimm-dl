/// <summary>
/// Guards the pure IGDB description logic (epic #122 / M2): JSON parsing, the summary-then-storyline
/// preference, the normalized-name index (collision + no-description handling), and the catalog join
/// that lets "Mega Man 2 (USA)" match IGDB's "Mega Man 2". No network — the host service fetches.
/// </summary>
[TestClass]
public class IgdbDescriptionsTests
{
    [TestMethod]
    public void ParseGames_ReadsNameSummaryStoryline_SkipsNameless()
    {
        const string json = """
            [
              {"id":1,"name":"Chrono Trigger","summary":"A time-travel RPG.","storyline":"Crono..."},
              {"id":2,"name":"Tetris"},
              {"id":3,"storyline":"no name here"}
            ]
            """;
        var games = IgdbDescriptions.ParseGames(json);
        Assert.HasCount(2, games); // the nameless entry is dropped
        Assert.AreEqual("Chrono Trigger", games[0].Name);
        Assert.AreEqual("A time-travel RPG.", games[0].Summary);
        Assert.AreEqual("Crono...", games[0].Storyline);
        Assert.IsNull(games[1].Summary);
    }

    [TestMethod]
    public void ParseGames_NonArray_ReturnsEmpty()
    {
        Assert.IsEmpty(IgdbDescriptions.ParseGames("{}"));
        Assert.IsEmpty(IgdbDescriptions.ParseGames("[]"));
    }

    [TestMethod]
    public void Description_PrefersSummary_ThenStoryline_ThenNull()
    {
        Assert.AreEqual("sum", new IgdbGame("G", "sum", "story").Description);
        Assert.AreEqual("story", new IgdbGame("G", null, "story").Description);
        Assert.AreEqual("story", new IgdbGame("G", "  ", "story").Description); // blank summary ignored
        Assert.IsNull(new IgdbGame("G", null, null).Description);
    }

    [TestMethod]
    public void BuildIndex_KeysByNormalizedName_SkipsDescriptionless_FirstWins()
    {
        var games = new[]
        {
            new IgdbGame("Mega Man 2", "The blue bomber returns.", null),
            new IgdbGame("Tetris", null, null),                  // no description → not indexed
            new IgdbGame("mega man II", "duplicate-ish, ignored", null), // different normalized key, kept
            new IgdbGame("Mega Man 2", "a later duplicate", null),       // same key → first wins
        };
        var index = IgdbDescriptions.BuildIndex(games);

        Assert.AreEqual("The blue bomber returns.", index["mega man 2"]);
        Assert.IsFalse(index.ContainsKey("tetris"));            // description-less entry skipped
        Assert.AreEqual("duplicate-ish, ignored", index["mega man ii"]);
    }

    [TestMethod]
    public void Match_JoinsCatalogNamesIgnoringRegionParentheticals()
    {
        var index = IgdbDescriptions.BuildIndex(new[]
        {
            new IgdbGame("Chrono Trigger", "A time-travel RPG.", null),
            new IgdbGame("Super Mario World", "Yoshi's debut.", null),
        });
        var catalog = new[]
        {
            (10L, "Chrono Trigger (USA)"),
            (11L, "Super Mario World (USA)"),
            (12L, "Unmatched Game (Japan)"),
        };

        var matched = IgdbDescriptions.Match(index, catalog).ToDictionary(m => m.Id, m => m.Description);

        Assert.HasCount(2, matched);
        Assert.AreEqual("A time-travel RPG.", matched[10]);
        Assert.AreEqual("Yoshi's debut.", matched[11]);
        Assert.IsFalse(matched.ContainsKey(12)); // no IGDB entry → no description
    }
}
