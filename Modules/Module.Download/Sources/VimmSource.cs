using Module.Core;

namespace Module.Download.Sources;

/// <summary>
/// Vimm's Lair source: fetches the vault page, resolves the download via the
/// existing <see cref="VaultPageParser"/>, and supplies Vimm's anti-bot request
/// headers (Referer = the vault URL, Sec-Fetch-Site = cross-site). This is the
/// behavior that previously lived inline in <see cref="DownloadService"/>.
/// </summary>
public sealed class VimmSource : IDownloadSource
{
    public string Id => "vimm";
    public string DisplayName => "Vimm's Lair";
    public string HttpClientName => "vimms";

    public async Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
    {
        // Let HTTP/network exceptions propagate. DownloadService relies on them for its
        // existing rate-limit handling (HTTP 429 → 60s backoff + retry, item retained) and
        // its queue-halt-on-error semantics. Swallowing them into a Result.Fail would make
        // the caller drop the queued item — a behavior change. Only a parse miss (no mediaId)
        // is an "expected" failure that drops the item, matching the pre-refactor flow exactly.
        var pageHtml = await http.GetStringAsync(sourceId, ct);

        var parsed = VaultPageParser.Parse(pageHtml, sourceId, format);
        if (parsed == null)
            return Result<ResolvedDownload>.Fail($"Could not find mediaId for {sourceId}");

        // Vimm requires the vault URL as Referer plus a cross-site fetch hint.
        var headers = new (string, string)[]
        {
            ("Referer", sourceId),
            ("Sec-Fetch-Site", "cross-site"),
        };

        return Result<ResolvedDownload>.Ok(new ResolvedDownload(
            parsed.DownloadUrl, parsed.Title, parsed.Platform,
            SuggestedFilename: null,
            RequestHeaders: headers,
            parsed.ResolvedFormat, parsed.FormatNote));
    }
}
