using System.Text.Json;
using Microsoft.Extensions.Logging;
using Module.Core;

namespace Module.Download.Sources;

/// <summary>
/// Internet Archive (archive.org) source. The user (or the Browse catalog) supplies a
/// direct file URL — <c>https://archive.org/download/&lt;identifier&gt;/&lt;file&gt;</c> — which is
/// also the download URL. archive.org URLs don't name the console, so it's derived
/// best-effort from the item's metadata API (title / subject); on any failure the file
/// still downloads and lands in <c>completed/</c> root.
/// </summary>
public sealed class ArchiveSource : IDownloadSource, ICatalogSource
{
    private readonly ILogger<ArchiveSource> _log;

    public ArchiveSource(ILogger<ArchiveSource> log) => _log = log;

    public string Id => "archive";
    public string DisplayName => "Internet Archive";
    public string HttpClientName => "archive";

    public async Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
    {
        if (!TryParse(sourceId, out var identifier, out var filename))
            return Result<ResolvedDownload>.Fail($"Not an archive.org download URL: {sourceId}");

        var platform = await TryResolvePlatformAsync(identifier, http, ct);

        return Result<ResolvedDownload>.Ok(new ResolvedDownload(
            DownloadUrl: sourceId,
            Title: StripExtension(filename),
            Platform: platform,
            SuggestedFilename: filename,
            RequestHeaders: null,
            ResolvedFormat: 0,
            FormatNote: null));
    }

    // --- ICatalogSource: in-app browse ---

    public async Task<Result<IReadOnlyList<CatalogSet>>> SearchSetsAsync(string query, HttpClient http, CancellationToken ct)
    {
        // Bias toward ROM "sets" (mediatype:software), most-downloaded first; the user picks.
        var q = string.IsNullOrWhiteSpace(query) ? "mediatype:software" : $"({query}) AND mediatype:software";
        var url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(q)
            + "&fl[]=identifier&fl[]=title&fl[]=subject&sort[]=" + Uri.EscapeDataString("downloads desc")
            + "&rows=40&output=json";
        try
        {
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var sets = new List<CatalogSet>();
            if (doc.RootElement.TryGetProperty("response", out var resp)
                && resp.TryGetProperty("docs", out var docs) && docs.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in docs.EnumerateArray())
                {
                    if (!d.TryGetProperty("identifier", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                    var id = idEl.GetString()!;
                    var title = d.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString()! : id;
                    string? platform = null;
                    foreach (var cand in PlatformCandidates(d))
                        if (ConsoleDirectories.Resolve(cand) != null) { platform = cand; break; }
                    sets.Add(new CatalogSet(id, title, platform));
                }
            }
            return Result<IReadOnlyList<CatalogSet>>.Ok(sets);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("archive.org search failed: {Error}", ex.Message);
            return Result<IReadOnlyList<CatalogSet>>.Fail($"archive.org search failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<CatalogFile>>> ListFilesAsync(string setId, string? filter, HttpClient http, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync($"https://archive.org/metadata/{Uri.EscapeDataString(setId)}", ct);
            using var doc = JsonDocument.Parse(json);
            var files = new List<CatalogFile>();
            if (doc.RootElement.TryGetProperty("files", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in arr.EnumerateArray())
                {
                    if (!f.TryGetProperty("name", out var nEl) || nEl.ValueKind != JsonValueKind.String) continue;
                    var name = nEl.GetString()!;
                    if (IsMetadataFile(name)) continue;
                    if (!string.IsNullOrWhiteSpace(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    long size = 0;
                    if (f.TryGetProperty("size", out var sEl))
                    {
                        if (sEl.ValueKind == JsonValueKind.String) long.TryParse(sEl.GetString(), out size);
                        else if (sEl.ValueKind == JsonValueKind.Number) sEl.TryGetInt64(out size);
                    }

                    var dl = $"https://archive.org/download/{Uri.EscapeDataString(setId)}/{EncodePath(name)}";
                    files.Add(new CatalogFile(name, size, dl));
                    if (files.Count >= 200) break; // cap payload; user narrows with the filter
                }
            }
            return Result<IReadOnlyList<CatalogFile>>.Ok(files);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("archive.org metadata for {Id} failed: {Error}", setId, ex.Message);
            return Result<IReadOnlyList<CatalogFile>>.Fail($"archive.org metadata failed: {ex.Message}");
        }
    }

    /// <summary>archive.org auto-generated/sidecar files that aren't ROMs.</summary>
    internal static bool IsMetadataFile(string name)
    {
        var l = name.ToLowerInvariant();
        return l.EndsWith(".xml") || l.EndsWith(".sqlite") || l.EndsWith(".torrent")
            || l.Contains("__ia_thumb") || l.EndsWith("_meta.txt") || l.EndsWith("_reviews.xml");
    }

    private static string EncodePath(string name)
        => string.Join("/", name.Split('/').Select(Uri.EscapeDataString));

    /// <summary>Parse an archive.org /download/&lt;identifier&gt;/&lt;file&gt; URL.</summary>
    internal static bool TryParse(string url, out string identifier, out string filename)
    {
        identifier = "";
        filename = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        // Exact archive.org or a subdomain (e.g. dl.archive.org) — not "fooarchive.org".
        if (!uri.Host.Equals("archive.org", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase)) return false;

        var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Expect: download / <identifier> / <file...>
        if (segs.Length < 3 || !segs[0].Equals("download", StringComparison.OrdinalIgnoreCase)) return false;

        identifier = Uri.UnescapeDataString(segs[1]);
        filename = Uri.UnescapeDataString(segs[^1]);
        return identifier.Length > 0 && filename.Length > 0;
    }

    /// <summary>
    /// Best-effort console detection from the archive.org metadata API. Never throws —
    /// returns null if the item can't be fetched or no known console is found.
    /// </summary>
    private async Task<string?> TryResolvePlatformAsync(string identifier, HttpClient http, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync($"https://archive.org/metadata/{Uri.EscapeDataString(identifier)}", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("metadata", out var meta)) return null;

            foreach (var candidate in PlatformCandidates(meta))
            {
                var dir = ConsoleDirectories.Resolve(candidate);
                if (dir != null) return candidate;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug("archive.org metadata lookup failed for {Id}: {Error}", identifier, ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Console-name candidates from item metadata: the title's "Manufacturer - Console"
    /// segment (sets are named e.g. "Nintendo - Game Boy Advance (No-Intro …)") plus each
    /// subject tag (e.g. "GBA", "Gameboy advance"). Ordered most-specific first.
    /// </summary>
    internal static IEnumerable<string> PlatformCandidates(JsonElement meta)
    {
        if (meta.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
        {
            var title = t.GetString()!;
            var paren = title.IndexOf('(');                 // drop "(No-Intro 2024-…)" suffix
            if (paren > 0) title = title[..paren];
            var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) yield return parts[1];   // "Manufacturer - Console" → Console
            yield return title.Trim();
        }

        if (meta.TryGetProperty("subject", out var subj))
        {
            if (subj.ValueKind == JsonValueKind.String)
                yield return subj.GetString()!;
            else if (subj.ValueKind == JsonValueKind.Array)
                foreach (var s in subj.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String)
                        yield return s.GetString()!;
        }
    }

    internal static string StripExtension(string filename)
    {
        var dot = filename.LastIndexOf('.');
        return dot > 0 ? filename[..dot] : filename;
    }
}
