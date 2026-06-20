using System.Text;

namespace Module.Catalog;

/// <summary>
/// 1G1R (one game, one ROM) helpers: a title key that groups a game's regional/revision variants,
/// and selection of the single preferred variant per group. Pure and testable — heuristic, mirroring
/// the common region/revision priority (not a substitute for DAT parent/clone metadata).
/// </summary>
public static class Dedup
{
    // Parenthetical tags that mark a non-final dump; these never win unless they're the only variant.
    private static readonly string[] BadTagPrefixes =
        ["(beta", "(proto", "(prototype", "(demo", "(sample", "(pirate", "(aftermarket", "(debug"];

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

    private static int RegionRank((string Name, string? Region) g)
    {
        var s = (string.IsNullOrEmpty(g.Region) ? g.Name : g.Region).ToLowerInvariant();
        if (s.Contains("usa") || s.Contains("world")) return 0;       // English, NTSC-U / global
        if (s.Contains("europe") || s.Contains("uk") || s.Contains("australia") || s.Contains("canada")) return 1;
        if (s.Contains("japan") || s.Contains("asia") || s.Contains("korea") || s.Contains("china")) return 2;
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
