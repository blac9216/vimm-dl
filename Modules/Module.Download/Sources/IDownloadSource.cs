using Module.Core;

namespace Module.Download.Sources;

/// <summary>
/// A pluggable download source — "where the bytes come from". The Vimm's Lair
/// vault is one source (<see cref="VimmSource"/>); future sources (Internet
/// Archive, Myrient, lolroms, Wii U NUS) implement this same contract so the
/// generic streaming core in <see cref="DownloadService"/> stays source-agnostic.
/// </summary>
public interface IDownloadSource
{
    /// <summary>Stable identifier persisted with each queue item, e.g. "vimm".</summary>
    string Id { get; }

    /// <summary>Human-friendly name for the UI source picker.</summary>
    string DisplayName { get; }

    /// <summary>Named <see cref="IHttpClientFactory"/> client this source uses.</summary>
    string HttpClientName { get; }

    /// <summary>
    /// Resolve a source-specific item id (for Vimm: the vault URL) into a concrete,
    /// streamable download. Returns a failed <see cref="Result{T}"/> when the item
    /// can't be resolved (page missing, no media id, etc.).
    /// </summary>
    Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct);
}

/// <summary>
/// Everything <see cref="DownloadService"/> needs to stream a file, with no
/// source-specific assumptions. <see cref="RequestHeaders"/> carries any
/// per-source headers (e.g. Vimm's Referer / Sec-Fetch-Site) applied to each request.
/// </summary>
public record ResolvedDownload(
    string DownloadUrl,
    string Title,
    string? Platform,
    string? SuggestedFilename,
    IReadOnlyList<(string Name, string Value)>? RequestHeaders,
    int ResolvedFormat,
    string? FormatNote);
