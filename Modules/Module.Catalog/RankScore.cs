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

    /// <summary>How much the IGDB quality signal counts vs. RetroAchievements popularity when blending (R2).</summary>
    internal const double QualityWeight = 0.7;

    /// <summary>Log10 multiplier mapping a player count onto the 0-100 scale (≈100 at ~100k players).</summary>
    internal const double PopularityScale = 20;

    /// <summary>
    /// RetroAchievements popularity (NumDistinctPlayers) normalized onto the same 0-100 scale as the
    /// quality score, log-compressed so the long tail of huge player counts doesn't dominate:
    /// <c>min(100, 20 * log10(1 + players))</c> (≈40 at 100 players, ≈80 at 10k, capped at 100).
    /// </summary>
    public static double Popularity(int players) =>
        players <= 0 ? 0 : Math.Min(100, PopularityScale * Math.Log10(1 + players));

    /// <summary>
    /// Blend the IGDB quality signal with RetroAchievements popularity into one rank_score (epic #123 /
    /// R2). When the game has an IGDB rating, it's <c>0.7 * Bayesian(rating, count) + 0.3 * Popularity</c>;
    /// when it has no IGDB rating (a cart-only title IGDB doesn't cover well), it falls back to the
    /// popularity score alone — so a hash-matched RA game still ranks above the unranked tail.
    /// </summary>
    public static double Blend(double? igdbRating, int? igdbCount, int raPlayers)
    {
        var pop = Popularity(raPlayers);
        if (igdbRating is { } rating)
            return QualityWeight * Bayesian(rating, igdbCount ?? 0) + (1 - QualityWeight) * pop;
        return pop;
    }
}
