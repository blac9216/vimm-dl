using System.Net;
using System.Text.RegularExpressions;

namespace Module.Catalog;

/// <summary>One game row from a Vimm vault list page: its numeric vault id and decoded title.</summary>
public sealed record VimmListEntry(long VaultId, string Title);

/// <summary>
/// Pure, AOT-friendly parsers for Vimm's Lair vault pages, used by the catalog↔Vimm binding. This
/// slice covers the list view only (enumerating a console's titles); per-title hash/format parsing
/// arrives in a later slice. Web-free and side-effect-free so it is fully unit-testable.
/// </summary>
public static partial class VimmVaultParser
{
    // Real list rows look like:  <a href= "/vault/24612">ABC Wipeout 2</a>
    // Each row also carries a placeholder anchor  <a href="/vault/999999"></a>  (empty text) which
    // this regex skips naturally — it requires at least one non-'<' char as the title. Note the
    // optional whitespace around '=' (the real link has a space after it).
    [GeneratedRegex("""<a\s+href\s*=\s*"/vault/(\d+)"\s*>([^<]+)</a>""", RegexOptions.IgnoreCase)]
    private static partial Regex VaultLinkRegex();

    /// <summary>
    /// Extract the game rows from a vault list page (<c>vault/?p=list&amp;system=&lt;code&gt;&amp;section=&lt;x&gt;</c>).
    /// Titles are HTML-decoded; the <c>/vault/999999</c> placeholder rows are excluded.
    /// </summary>
    public static IReadOnlyList<VimmListEntry> ParseList(string html)
    {
        var entries = new List<VimmListEntry>();
        foreach (Match m in VaultLinkRegex().Matches(html))
        {
            if (!long.TryParse(m.Groups[1].Value, out var id) || id == 999999) continue;
            var title = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (title.Length == 0) continue;
            entries.Add(new VimmListEntry(id, title));
        }
        return entries;
    }

    /// <summary>
    /// The vault list section a game name falls under: an uppercase letter A–Z, or <c>number</c> for
    /// names that don't start with a letter (digits/symbols) — matching Vimm's own section scheme.
    /// </summary>
    public static string SectionFor(string name)
    {
        var trimmed = name.AsSpan().TrimStart();
        if (trimmed.IsEmpty) return "number";
        var c = char.ToUpperInvariant(trimmed[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : "number";
    }
}
