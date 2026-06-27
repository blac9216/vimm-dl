namespace Module.Catalog;

/// <summary>
/// Maps a catalog game to its libretro-thumbnails CDN URL(s). The thumbnails project is keyed by the
/// exact No-Intro/Redump game name — which is precisely what <c>catalog_game.name</c> stores (region
/// and disc parentheticals included) — under a per-system folder that matches the DAT name, with a
/// fixed character-substitution rule for filesystem-unsafe characters.
///
/// Pure + web-free so the naming rules are unit-tested without the network; the host
/// <c>MediaService</c> performs the fetch + on-disk cache. CDN shape:
/// <c>https://thumbnails.libretro.com/{System}/{Named_Boxarts|Named_Titles}/{Name}.png</c>.
/// </summary>
public static class LibretroThumbnails
{
    public const string BaseUrl = "https://thumbnails.libretro.com";

    /// <summary>
    /// The image kinds we serve → the libretro CDN type folder. <c>boxart</c> = cover art,
    /// <c>title</c> = title screen. (libretro also exposes <c>Named_Snaps</c> for in-game shots —
    /// not surfaced yet.)
    /// </summary>
    private static readonly Dictionary<string, string> TypeFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["boxart"] = "Named_Boxarts",
        ["title"] = "Named_Titles",
    };

    /// <summary>
    /// DAT names whose libretro-thumbnails system folder differs from the DAT name. The thumbnails
    /// repos use the same No-Intro/Redump system naming, so nearly every DAT name is its own thumbnail
    /// folder 1:1 and only the exceptions are listed here. Systems with no thumbnail coverage at all
    /// are deliberately NOT special-cased — their fetch simply 404s and is negative-cached.
    /// </summary>
    private static readonly Dictionary<string, string> SystemOverrides = new(StringComparer.Ordinal)
    {
        ["Philips - Videopac+"] = "Philips - Videopac",
        ["Sinclair - ZX Spectrum +3"] = "Sinclair - ZX Spectrum",
    };

    /// <summary>True if <paramref name="type"/> is an image kind we serve (<c>boxart</c> | <c>title</c>).</summary>
    public static bool IsKnownType(string type) => TypeFolders.ContainsKey(type);

    /// <summary>The libretro CDN type folder for a known image kind (call <see cref="IsKnownType"/> first).</summary>
    public static string TypeFolder(string type) => TypeFolders[type];

    /// <summary>The CDN system folder for a DAT name (the DAT name itself unless overridden).</summary>
    public static string SystemFolder(string datName) => SystemOverrides.GetValueOrDefault(datName, datName);

    /// <summary>
    /// Apply the libretro filename substitution rule: each of <c>&amp; * / : ` &lt; &gt; ? \ | "</c>
    /// becomes <c>_</c>. Everything else — including spaces and parentheses — is preserved, since the
    /// CDN filenames keep the No-Intro spacing and the region/disc parentheticals.
    /// </summary>
    public static string Sanitize(string name)
    {
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            chars[i] = chars[i] switch
            {
                '&' or '*' or '/' or ':' or '`' or '<' or '>' or '?' or '\\' or '|' or '"' => '_',
                _ => chars[i],
            };
        return new string(chars);
    }

    /// <summary>
    /// The name lookups to try, in order: the exact game name, then the name truncated before its first
    /// <c>(</c> (covers thumbnails stored without the region/disc parenthetical). Each is sanitized and
    /// the list is de-duplicated (a name with no <c>(</c> yields a single candidate).
    /// </summary>
    public static IReadOnlyList<string> NameCandidates(string gameName)
    {
        var list = new List<string>(2);
        void Add(string s)
        {
            s = Sanitize(s.Trim());
            if (s.Length > 0 && !list.Contains(s)) list.Add(s);
        }
        Add(gameName);
        int paren = gameName.IndexOf('(');
        if (paren > 0) Add(gameName[..paren]);
        return list;
    }

    /// <summary>Build the CDN URL for a system folder, type folder, and already-sanitized name.</summary>
    public static string Url(string systemFolder, string typeFolder, string sanitizedName) =>
        $"{BaseUrl}/{Uri.EscapeDataString(systemFolder)}/{typeFolder}/{Uri.EscapeDataString(sanitizedName)}.png";

    /// <summary>
    /// The full ordered list of CDN URLs to try for a game's image: one per name candidate
    /// (exact name, then truncated-before-<c>(</c>). The first that returns 200 wins.
    /// </summary>
    public static IEnumerable<string> Urls(string datName, string type, string gameName)
    {
        var system = SystemFolder(datName);
        var folder = TypeFolder(type);
        foreach (var name in NameCandidates(gameName))
            yield return Url(system, folder, name);
    }
}
