/// <summary>
/// Guards the pure RetroAchievements logic (epic #123 / R2): parsing the system game list (id + ROM MD5
/// hashes, lowercased), the MD5→RA-id index, the NumDistinctPlayers extract, and the
/// <see cref="RankScore"/> popularity normalization + IGDB/RA blend. No network — the host service fetches.
/// </summary>
[TestClass]
public class RaGameListTests
{
    [TestMethod]
    public void ParseGameList_ReadsIdAndLowercasedHashes_SkipsIdless()
    {
        const string json = """
            [
              {"ID":1,"Title":"Sonic","Hashes":["ABCDEF0123456789ABCDEF0123456789","1111222233334444AAAABBBBCCCCDDDD"]},
              {"ID":2,"Title":"No Hashes"},
              {"Title":"No Id","Hashes":["deadbeef"]}
            ]
            """;
        var games = RaGameList.ParseGameList(json);
        Assert.HasCount(2, games);                 // the id-less entry is dropped
        Assert.AreEqual(1, games[0].Id);
        Assert.AreEqual("abcdef0123456789abcdef0123456789", games[0].Hashes[0]); // lowercased
        Assert.HasCount(2, games[0].Hashes);
        Assert.IsEmpty(games[1].Hashes);           // a game can list no hashes
    }

    [TestMethod]
    public void ParseGameList_NonArray_ReturnsEmpty()
    {
        Assert.IsEmpty(RaGameList.ParseGameList("{}"));
        Assert.IsEmpty(RaGameList.ParseGameList("[]"));
    }

    [TestMethod]
    public void BuildHashIndex_MapsEachHashToItsRaId_FirstWins()
    {
        var games = new[]
        {
            new RaGame(10, ["aaaa", "bbbb"]),
            new RaGame(11, ["cccc"]),
            new RaGame(12, ["aaaa"]), // duplicate hash → first (10) wins
        };
        var index = RaGameList.BuildHashIndex(games);
        Assert.AreEqual(10, index["aaaa"]);
        Assert.AreEqual(10, index["bbbb"]);
        Assert.AreEqual(11, index["cccc"]);
    }

    [TestMethod]
    public void ParsePlayers_ReadsNumDistinctPlayers_OrNull()
    {
        Assert.AreEqual(27080, RaGameList.ParsePlayers("""{"ID":1,"Title":"Sonic","NumDistinctPlayers":27080}"""));
        Assert.IsNull(RaGameList.ParsePlayers("""{"ID":1,"Title":"Sonic"}"""));   // field absent
        Assert.IsNull(RaGameList.ParsePlayers("[]"));                              // not an object
    }

    [TestMethod]
    public void Popularity_IsLogScaled_MonotonicAndCapped()
    {
        Assert.AreEqual(0, RankScore.Popularity(0));
        Assert.AreEqual(0, RankScore.Popularity(-5));
        Assert.AreEqual(100, RankScore.Popularity(1_000_000), 0.001);                 // capped at 100
        Assert.IsLessThan(RankScore.Popularity(1000), RankScore.Popularity(100));     // more players → higher
        Assert.IsLessThan(RankScore.Popularity(100_000), RankScore.Popularity(1000));
    }

    [TestMethod]
    public void Blend_WithIgdb_MixesQualityAndPopularity_BetweenTheTwo()
    {
        var quality = RankScore.Bayesian(90, 1000);   // ~89.4
        var pop = RankScore.Popularity(50);           // ~34
        var blend = RankScore.Blend(90, 1000, 50);
        // 0.7*quality + 0.3*pop sits strictly between the (higher) quality and the (lower) popularity.
        Assert.AreEqual(0.7 * quality + 0.3 * pop, blend, 0.0001);
        Assert.IsLessThan(quality, blend);
        Assert.IsLessThan(blend, pop);
    }

    [TestMethod]
    public void Blend_NoIgdbRating_IsPopularityOnly()
    {
        Assert.AreEqual(RankScore.Popularity(5000), RankScore.Blend(null, null, 5000), 0.0001);
    }

    [TestMethod]
    public void Blend_MorePlayersAtSameQuality_RanksHigher()
    {
        Assert.IsLessThan(RankScore.Blend(80, 500, 100_000), RankScore.Blend(80, 500, 100));
    }
}
