namespace Module.Catalog;

/// <summary>
/// Turns a raw rating + vote count into a single sortable "best games" score, damping the noise of tiny
/// vote counts (the IMDb-style Bayesian weighted rating): a game rated 100 by one person shouldn't
/// outrank one rated 92 by thousands.
///
///   score = (v / (v + m)) * R + (m / (v + m)) * C
///
/// where R = the rating, v = the vote count, m = the confidence threshold (votes needed before a game's
/// own rating outweighs the prior), and C = the prior mean a sparsely-rated game regresses toward. With
/// few votes the score sits near C; with many it converges on R. Pure + web-free so it's unit-testable
/// and shared by the IGDB ranking ingest (epic #123 / R1). R2 blends RetroAchievements popularity in.
/// </summary>
public static class RankScore
{
    /// <summary>Votes needed before a game's own rating outweighs the prior; below this it's pulled toward <see cref="PriorMean"/>.</summary>
    internal const double MinVotes = 30;

    /// <summary>The prior mean (on IGDB's 0-100 scale) a sparsely- or un-voted game regresses toward.</summary>
    internal const double PriorMean = 70;

    /// <summary>
    /// The Bayesian-weighted score for a rating with <paramref name="count"/> votes. A non-positive
    /// count (no votes) yields the prior mean — "unknown, assume average" — rather than the raw rating.
    /// </summary>
    public static double Bayesian(double rating, int count)
    {
        var v = count > 0 ? count : 0;
        return v / (v + MinVotes) * rating + MinVotes / (v + MinVotes) * PriorMean;
    }
}
