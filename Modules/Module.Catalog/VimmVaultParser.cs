using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Module.Catalog;

/// <summary>One game row from a Vimm vault list page: its numeric vault id and decoded title.</summary>
public sealed record VimmListEntry(long VaultId, string Title);

/// <summary>
/// A media entry from a vault page's inline JSON: the Redump/No-Intro hash triple (present inline for
/// single-file titles, absent for multi-disc — use <see cref="VimmVaultParser.ParseHashes2"/> there)
/// plus the canonical name and per-format compressed sizes. Hashes are lowercased.
/// </summary>
public sealed record VimmMedia(
    long Id, string? Serial, string? Name,
    string? Crc, string? Md5, string? Sha1,
    IReadOnlyList<VimmMediaSize> Sizes);

/// <summary>A downloadable format's compressed size: alt index (0/1/2), bytes, and human text.</summary>
public sealed record VimmMediaSize(int Alt, long Bytes, string Text);

/// <summary>A download format from a vault page's dl_format select: alt index + short label.</summary>
public sealed record VimmFormat(int Alt, string Label);

/// <summary>Per-file Redump hashes from the hashes2.php fragment (multi-disc titles). Lowercased.</summary>
public sealed record VimmFileHash(string FileName, string Crc, string Md5, string Sha1);

/// <summary>
/// Pure, AOT-friendly parsers for Vimm's Lair vault pages, used by the catalog↔Vimm binding: the list
/// view (enumerating a console's titles), the inline <c>media</c> JSON (hashes + sizes), the
/// <c>dl_format</c> select (download formats), and the <c>hashes2.php</c> fragment (multi-disc
/// hashes). Web-free and side-effect-free so it is fully unit-testable.
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

    [GeneratedRegex(@"media\s*=\s*\[", RegexOptions.IgnoreCase)]
    private static partial Regex MediaArrayStartRegex();

    /// <summary>
    /// Parse the inline <c>media</c> JSON array. Single-file titles carry the hash triple inline
    /// (GoodHash=CRC32, GoodMd5, GoodSha1); multi-disc titles omit it but still expose per-format
    /// sizes (zero-size formats are dropped). Returns [] if the array is absent or malformed.
    /// </summary>
    public static IReadOnlyList<VimmMedia> ParseMedia(string html)
    {
        var json = ExtractMediaArray(html);
        if (json is null) return [];
        var result = new List<VimmMedia>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var sizes = new List<VimmMediaSize>();
                AddSize(sizes, 0, el, "Zipped", "ZippedText");
                AddSize(sizes, 1, el, "AltZipped", "AltZippedText");
                AddSize(sizes, 2, el, "AltZipped2", "AltZipped2Text");
                result.Add(new VimmMedia(
                    Id: GetLong(el, "ID"),
                    Serial: NullIfEmpty(GetString(el, "Serial")),
                    Name: DecodeBase64(GetString(el, "GoodTitle")),
                    Crc: LowerOrNull(GetString(el, "GoodHash")),
                    Md5: LowerOrNull(GetString(el, "GoodMd5")),
                    Sha1: LowerOrNull(GetString(el, "GoodSha1")),
                    Sizes: sizes));
            }
        }
        catch (JsonException) { return []; }
        return result;
    }

    [GeneratedRegex("""<select\b[^>]*\bid="dl_format".*?</select>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DlFormatSelectRegex();

    [GeneratedRegex("""<option\s+value="(\d+)"\s+title="[^"]*"\s*>([^<]+)</option>""", RegexOptions.IgnoreCase)]
    private static partial Regex FormatOptionRegex();

    /// <summary>
    /// The download formats a vault page offers, from its <c>dl_format</c> select (alt index + the
    /// short label like "JB Folder"/".dec.iso"). Single-file titles have no such select → returns []
    /// and the caller treats it as the implicit format 0.
    /// </summary>
    public static IReadOnlyList<VimmFormat> ParseFormats(string html)
    {
        var sel = DlFormatSelectRegex().Match(html);
        if (!sel.Success) return [];
        var list = new List<VimmFormat>();
        foreach (Match m in FormatOptionRegex().Matches(sel.Value))
            if (int.TryParse(m.Groups[1].Value, out var alt))
                list.Add(new VimmFormat(alt, WebUtility.HtmlDecode(m.Groups[2].Value).Trim()));
        return list;
    }

    [GeneratedRegex("""<div\s+style="grid-column:span 2">(?:<br\s*/?>)?([^<]+)</div>\s*<div>Crc</div>\s*<div>([0-9a-fA-F]+)</div>\s*<div>Md5</div>\s*<div>([0-9a-fA-F]+)</div>\s*<div>Sha1</div>\s*<div>([0-9a-fA-F]+)</div>""", RegexOptions.IgnoreCase)]
    private static partial Regex Hashes2Regex();

    /// <summary>
    /// Parse the <c>vault/ajax/hashes2.php</c> fragment ("Redump File Hashes") for multi-disc /
    /// multi-file titles: one entry per file (e.g. <c>.bin</c>/<c>.cue</c>) with CRC32/MD5/SHA1.
    /// </summary>
    public static IReadOnlyList<VimmFileHash> ParseHashes2(string html)
    {
        var list = new List<VimmFileHash>();
        foreach (Match m in Hashes2Regex().Matches(html))
            list.Add(new VimmFileHash(
                WebUtility.HtmlDecode(m.Groups[1].Value).Trim(),
                m.Groups[2].Value.ToLowerInvariant(),
                m.Groups[3].Value.ToLowerInvariant(),
                m.Groups[4].Value.ToLowerInvariant()));
        return list;
    }

    /// <summary>Extract the <c>media=[…]</c> JSON array text, bracket-balanced and string-aware.</summary>
    private static string? ExtractMediaArray(string html)
    {
        var m = MediaArrayStartRegex().Match(html);
        if (!m.Success) return null;
        int start = html.IndexOf('[', m.Index);
        if (start < 0) return null;
        int depth = 0;
        bool inStr = false, esc = false;
        for (int i = start; i < html.Length; i++)
        {
            char c = html[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') inStr = false;
            }
            else if (c == '"') inStr = true;
            else if (c == '[') depth++;
            else if (c == ']' && --depth == 0) return html.Substring(start, i - start + 1);
        }
        return null;
    }

    private static void AddSize(List<VimmMediaSize> sizes, int alt, JsonElement el, string sizeKey, string textKey)
    {
        var raw = GetString(el, sizeKey);
        if (raw is null || !long.TryParse(raw, out var bytes) || bytes <= 0) return;
        sizes.Add(new VimmMediaSize(alt, bytes, GetString(el, textKey)?.Trim() ?? ""));
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : 0;

    private static string? LowerOrNull(string? s) => string.IsNullOrEmpty(s) ? null : s.ToLowerInvariant();

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? DecodeBase64(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return null; }
    }
}
