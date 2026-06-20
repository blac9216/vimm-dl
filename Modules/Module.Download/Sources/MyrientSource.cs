using Module.Core;

namespace Module.Download.Sources;

/// <summary>
/// Myrient (myrient.erista.me) source. The user pastes a direct file URL; Myrient
/// serves plain HTTPS files with no auth or anti-bot headers, so resolution is pure
/// URL parsing — the pasted URL *is* the download URL. The filename comes from the
/// last path segment and the platform from the No-Intro/Redump "system" path segment.
/// </summary>
public sealed class MyrientSource : IDownloadSource
{
    public string Id => "myrient";
    public string DisplayName => "Myrient";
    public string HttpClientName => "myrient";

    public Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
    {
        if (!Uri.TryCreate(sourceId, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Task.FromResult(Result<ResolvedDownload>.Fail($"Not a valid http(s) URL: {sourceId}"));

        var filename = ExtractFilename(uri);
        if (string.IsNullOrEmpty(filename))
            return Task.FromResult(Result<ResolvedDownload>.Fail($"Could not determine a filename from {sourceId}"));

        return Task.FromResult(Result<ResolvedDownload>.Ok(new ResolvedDownload(
            DownloadUrl: sourceId,
            Title: StripExtension(filename),
            Platform: ExtractPlatform(uri),
            SuggestedFilename: filename,
            RequestHeaders: null,
            ResolvedFormat: 0,
            FormatNote: null)));
    }

    /// <summary>Last path segment, URL-decoded (e.g. "Some Game (USA).zip").</summary>
    internal static string ExtractFilename(Uri uri)
    {
        var seg = uri.Segments.Length > 0 ? uri.Segments[^1] : "";
        return Uri.UnescapeDataString(seg).TrimEnd('/');
    }

    /// <summary>
    /// Platform from a Myrient path like <c>/files/&lt;collection&gt;/&lt;system&gt;/&lt;file&gt;</c>.
    /// The system segment uses No-Intro/Redump naming —
    /// "Manufacturer - Console[ - format qualifier]" (e.g. "Sony - PlayStation 2",
    /// "Nintendo - GameCube - NKit RVZ", "Sega - Mega Drive - Genesis"). The console is the
    /// segment after the manufacturer, which <see cref="ConsoleDirectories"/> can map.
    /// </summary>
    internal static string? ExtractPlatform(Uri uri)
    {
        var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 2) return null;
        var system = Uri.UnescapeDataString(segs[^2]);
        var parts = system.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => parts[1],   // "Manufacturer - Console - …" → Console
            1 => parts[0],
            _ => null,
        };
    }

    internal static string StripExtension(string filename)
    {
        var dot = filename.LastIndexOf('.');
        return dot > 0 ? filename[..dot] : filename;
    }
}
