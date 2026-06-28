/// <summary>
/// Guards the pure IGDB ranking logic (epic #123 / R1): JSON parsing of total_rating/total_rating_count,
/// the normalized-name index (collision + rating-less handling), the catalog join that lets "Chrono
/// Trigger (USA)" match IGDB's "Chrono Trigger", and the Bayesian <see cref="RankScore"/> that damps
/// low-vote noise. No network — the host service fetches.
/// </summary>
[TestClass]
public class IgdbRatingsTests
{
    [TestMethod]
    public void ParseGames_ReadsNameAndRating_SkipsNamelessAndRatingless()
    {
        const string json = """
            [
              {"id":1,"name":"Chrono Trigger","total_rating":94.5,"total_rating_count":1200},
              {"id":2,"name":"Rated, No Count","total_rating":80.0},
              {"id":3,"name":"No Rating Here"},
              {"id":4,"total_rating":99.0}
            ]
            """;
        var games = IgdbRatings.ParseGames(json);
        Assert.HasCount(2, games);                  // the rating-less + nameless entries are dropped
        Assert.AreEqual("Chrono Trigger", games[0].Name);
        Assert.AreEqual(94.5, games[0].TotalRating);
        Assert.AreEqual(1200, games[0].TotalRatingCount);
        Assert.AreEqual(80.0, games[1].TotalRating);
        Assert.AreEqual(0, games[1].TotalRatingCount); // missing count → 0
    }

    [TestMethod]
    public void ParseGames_NonArray_ReturnsEmpty()
    {
        Assert.IsEmpty(IgdbRatings.ParseGames("{}"));
        Assert.IsEmpty(IgdbRatings.ParseGames("[]"));
    }

    [TestMethod]
    public void BuildIndex_KeysByNormalizedName_FirstWins()
    {
        var games = new[]
        {
            new IgdbRating("Mega Man 2", 90, 100),
            new IgdbRating("mega man II", 50, 5),       // different normalized key, kept
            new IgdbRating("Mega Man 2", 10, 999),      // same key → first wins
        };
        var index = IgdbRatings.BuildIndex(games);

        Assert.AreEqual(90, index["mega man 2"].TotalRating);  // first entry wins, not the later 10
        Assert.AreEqual(50, index["mega man ii"].TotalRating);
    }

    [TestMethod]
    public void Match_JoinsCatalogNamesIgnoringRegionParentheticals()
    {
        var index = IgdbRatings.BuildIndex(new[]
        {
            new IgdbRating("Chrono Trigger", 94, 1200),
            new IgdbRating("Super Mario World", 89, 900),
        });
        var catalog = new[]
        {
            (10L, "Chrono Trigger (USA)"),
            (11L, "Super Mario World (USA)"),
            (12L, "Unmatched Game (Japan)"),
        };

        var matched = IgdbRatings.Match(index, catalog).ToDictionary(m => m.Id, m => (m.Rating, m.Count));

        Assert.HasCount(2, matched);
        Assert.AreEqual((94.0, 1200), matched[10]);
        Assert.AreEqual((89.0, 900), matched[11]);
        Assert.IsFalse(matched.ContainsKey(12)); // no IGDB entry → unranked
    }

    [TestMethod]
    public void RankScore_ManyVotes_ConvergesOnRating()
    {
        // With far more votes than the confidence threshold, the score is essentially the raw rating.
        Assert.AreEqual(92.0, RankScore.Bayesian(92, 5000), 0.5);
    }

    [TestMethod]
    public void RankScore_NoVotes_IsThePrior()
    {
        // No votes → "unknown, assume average" (the prior mean), never the unbacked raw rating.
        Assert.AreEqual(RankScore.PriorMean, RankScore.Bayesian(100, 0), 0.0001);
        Assert.AreEqual(RankScore.PriorMean, RankScore.Bayesian(100, -3), 0.0001);
    }

    [TestMethod]
    public void RankScore_DampsLowVoteOutliers_SoAcclaimedGamesWin()
    {
        // A 1-vote perfect score must NOT outrank a thousands-vote near-perfect game.
        var oneVote100 = RankScore.Bayesian(100, 1);
        var manyVote92 = RankScore.Bayesian(92, 5000);
        Assert.IsLessThan(manyVote92, oneVote100);   // the noisy outlier loses
        Assert.IsLessThan(85, oneVote100);           // and is pulled well down toward the prior
    }

    [TestMethod]
    public void RankScore_MoreVotesAtSameRating_RanksHigherWhenAbovePrior()
    {
        // For a rating above the prior, more votes = more confidence = a higher score.
        Assert.IsLessThan(RankScore.Bayesian(90, 1000), RankScore.Bayesian(90, 20));
    }
}
