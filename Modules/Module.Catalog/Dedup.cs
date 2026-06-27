using System.Text;

namespace Module.Catalog;

/// <summary>
/// 1G1R (one game, one ROM) helpers: a title key that groups a game's regional/revision variants,
/// and selection of the single preferred variant per group. Pure and testable — heuristic, mirroring
/// the common region/revision priority (not a substitute for DAT parent/clone metadata).
/// </summary>
public static class Dedup
{
    // Parenthetical category tags that mark a non-final / non-retail dump. These are the variants the
    // Library's "hide demos/protos" filter (E3a) excludes, and they also never win 1G1R parent
    // selection unless they're the only variant. "(proto" already covers "(prototype)".
    public static readonly string[] ExcludedCategoryTags =
        ["(beta", "(proto", "(demo", "(sample", "(kiosk"];

    // Parent selection additionally avoids these never-retail tags (a superset of the category tags).
    private static readonly string[] BadTagPrefixes =
        [.. ExcludedCategoryTags, "(pirate", "(aftermarket", "(debug"];

    // Region tokens (lowercased) by rank bucket, shared by RegionRank (1G1R parent selection) and the
    // English-only Library filter so the two can't drift (#201). Rank 0 = NTSC-U / global English;
    // rank 1 = other Western/English regions — both count as English. Rank 2 = Japan / Asia.
    private static readonly string[] GlobalEnglishRegions = ["usa", "world"];
    private static readonly string[] WesternRegions =
        ["europe", "uk", "australia", "canada", "new zealand", "ireland", "scandinavia"];
    private static readonly string[] AsianRegions = ["japan", "asia", "korea", "china"];

    /// <summary>
    /// Region tokens that mark an English/Western release, for the English-only Library filter (E3a) —
    /// exactly RegionRank's rank-0 and rank-1 buckets. The SQL mirror in GetGamesAsync derives from this.
    /// </summary>
    public static readonly string[] EnglishRegionTokens = [.. GlobalEnglishRegions, .. WesternRegions];

    /// <summary>Strip all (…) and […] tags from a name and normalize, so all variants share a key.</summary>
    public static string TitleKey(string name)
    {
        var sb = new StringBuilder(name.Length);
        int depth = 0;
        foreach (var c in name)
        {
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') { if (depth > 0) depth--; }
            else if (depth == 0) sb.Append(c);
        }
        return CatalogMatcher.Normalize(sb.ToString());
    }

    /// <summary>Index of the preferred (1G1R parent) variant among a title's games.</summary>
    public static int SelectParent(IReadOnlyList<(string Name, string? Region)> games)
    {
        int best = 0;
        for (int i = 1; i < games.Count; i++)
            if (IsBetter(games[i], games[best])) best = i;
        return best;
    }

    private static bool IsBetter((string Name, string? Region) a, (string Name, string? Region) b)
    {
        bool aBad = IsBadDump(a.Name), bBad = IsBadDump(b.Name);
        if (aBad != bBad) return !aBad;                 // (1) a real release beats a beta/proto/demo

        int ar = RegionRank(a), br = RegionRank(b);
        if (ar != br) return ar < br;                   // (2) better region

        return Revision(a.Name) > Revision(b.Name);     // (3) newer revision
    }

    private static bool IsBadDump(string name)
    {
        var l = name.ToLowerInvariant();
        foreach (var tag in BadTagPrefixes)
            if (l.Contains(tag)) return true;
        return false;
    }

    /// <summary>
    /// True when the No-Intro/Redump name carries a non-final category tag (demo/beta/proto/kiosk/sample).
    /// Backs the Library "hide demos/protos" filter; the SQL filter in GetGamesAsync is built from the
    /// same <see cref="ExcludedCategoryTags"/> list so the two never drift.
    /// </summary>
    public static bool IsExcludedVariant(string name)
    {
        var l = name.ToLowerInvariant();
        foreach (var tag in ExcludedCategoryTags)
            if (l.Contains(tag)) return true;
        return false;
    }

    /// <summary>
    /// True when the release looks English/Western: its language list contains "En", or (failing that)
    /// its region — or the region tag embedded in its name when the region column is empty — is a
    /// Western/English one. Backs the English-only Library filter; the SQL mirror in GetGamesAsync
    /// derives from the same <see cref="EnglishRegionTokens"/>.
    /// </summary>
    public static bool IsEnglish(string? region, string? languages, string name)
    {
        if (!string.IsNullOrEmpty(languages) && languages.Contains("en", StringComparison.OrdinalIgnoreCase))
            return true;
        var s = (string.IsNullOrEmpty(region) ? name : region).ToLowerInvariant();
        return MatchesAny(s, EnglishRegionTokens);
    }

    /// <summary>
    /// Match a region token at a tag boundary rather than as a raw substring. Region tags in
    /// No-Intro/Redump names are parenthesised and comma-delimited ("(USA)", "(Japan, USA)"), so we
    /// replace those delimiters with spaces, pad, and look for the space-bounded token. This stops the
    /// 2-letter "uk" (and the like) from false-matching inside a word — e.g. "Sukeban", "Yuukyuu",
    /// "Duke" — when the region column is empty and we fall back to the name. The SQL filter in
    /// <c>CatalogRepository.GetGamesAsync</c> applies the same transform.
    /// </summary>
    private static bool HasRegionToken(string loweredHaystack, string token)
    {
        var padded = " " + loweredHaystack.Replace('(', ' ').Replace(')', ' ').Replace(',', ' ') + " ";
        return padded.Contains(" " + token + " ", StringComparison.Ordinal);
    }

    /// <summary>True if any of <paramref name="tokens"/> matches as a boundary-bounded region tag.</summary>
    private static bool MatchesAny(string lowered, string[] tokens)
    {
        foreach (var token in tokens)
            if (HasRegionToken(lowered, token)) return true;
        return false;
    }

    private static int RegionRank((string Name, string? Region) g)
    {
        var s = (string.IsNullOrEmpty(g.Region) ? g.Name : g.Region).ToLowerInvariant();
        if (MatchesAny(s, GlobalEnglishRegions)) return 0;   // English, NTSC-U / global
        if (MatchesAny(s, WesternRegions)) return 1;         // other Western/English (PAL, English-speaking)
        if (MatchesAny(s, AsianRegions)) return 2;           // Japan / Asia
        return 3;
    }

    /// <summary>Revision from a "(Rev N)"/"(Rev A)" tag; 0 if none. Higher = newer.</summary>
    private static int Revision(string name)
    {
        var l = name.ToLowerInvariant();
        int i = l.IndexOf("(rev ", StringComparison.Ordinal);
        if (i < 0) return 0;
        i += 5;
        int n = 0;
        bool anyDigit = false;
        while (i < l.Length && char.IsDigit(l[i])) { n = n * 10 + (l[i] - '0'); i++; anyDigit = true; }
        if (anyDigit) return n;
        if (i < l.Length && l[i] is >= 'a' and <= 'z') return l[i] - 'a' + 1; // Rev A=1, B=2…
        return 0;
    }
}
