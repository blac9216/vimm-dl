// WiiUNusSource.cs
//
// Clean-room Wii U NUS/CDN download source. One queue item (a title ID) fans out into the encrypted WUP
// set: the TMD, the ticket (cetk), and each content's .app (+ .h3 for hashed content). Implements the
// multi-file source seam (W4); the generic download loop streams + integrity-checks the files.
//
// Implemented solely from public specs: wiibrew "NUS" (CDN layout/paths, the `wii libnup/1.0` UA),
// wiiubrew "Title metadata" (TMD content enumeration, via Module.WiiUTools.Tmd). No GPL source was read,
// copied, or transliterated. Works with no keys present — it lands encrypted bytes; decryption is W6.
//
// NUS path layout (under the configured base):
//   <base>/<titleid>/tmd            the title metadata (signed)
//   <base>/<titleid>/cetk           the ticket (encrypted title key)
//   <base>/<titleid>/<cid>          a content (.app), 8-hex lowercase content id, no extension
//   <base>/<titleid>/<cid>.h3       the content's hash tree (only for hashed content)
//
// Note: NUS does NOT serve a separate certificate file. The cert chain is a public, keyless constant
// reconstructed at packaging time, so it is intentionally not part of this download (handled in W6).

using Module.Core;
using Module.Download.Sources;
using Module.WiiUTools;

namespace Module.WiiUSource;

/// <summary>Downloads a Wii U title's encrypted WUP set from the NUS CDN as a multi-file item.</summary>
public sealed class WiiUNusSource : IMultiFileSource
{
    /// <summary>Default Nintendo NUS/CCS content base (plain HTTP, as the console uses).</summary>
    public const string DefaultBaseUrl = "http://ccs.cdn.wup.shop.nintendo.net/ccs/download";

    private readonly string _baseUrl;

    public WiiUNusSource(string? baseUrl = null) => _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');

    public string Id => "wiiu";
    public string DisplayName => "Wii U (NUS)";
    public string HttpClientName => "wiiu";

    public async Task<Result<MultiFileResolution>> ResolveManyAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
    {
        var titleId = NormalizeTitleId(sourceId);
        if (titleId is null)
            return Result<MultiFileResolution>.Fail($"Not a valid 16-hex-digit Wii U title ID: '{sourceId}'.");

        var titleBase = $"{_baseUrl}/{titleId.ToLowerInvariant()}";

        byte[] tmdBytes;
        try
        {
            tmdBytes = await http.GetByteArrayAsync($"{titleBase}/tmd", ct);
        }
        catch (HttpRequestException ex)
        {
            return Result<MultiFileResolution>.Fail($"NUS: could not fetch TMD for {titleId}: {ex.Message}");
        }

        var tmdResult = Tmd.Parse(tmdBytes);
        if (!tmdResult.IsOk)
            return Result<MultiFileResolution>.Fail($"NUS: {tmdResult.Error}");
        var tmd = tmdResult.Value!;

        // tmd + cetk, then each content (+ its .h3). Local names follow the WUP convention.
        var files = new List<ResolvedDownload>
        {
            Meta($"{titleBase}/tmd", "title.tmd"),
            Meta($"{titleBase}/cetk", "title.tik"),
        };

        foreach (var content in tmd.Contents)
        {
            var idHex = content.ContentIdHex; // lowercase 8-hex
            var sha1 = Convert.ToHexString(content.Sha1Hash);

            // For non-hashed content the TMD SHA-1 covers the (encrypted) .app, so verify it on download.
            // For hashed content the TMD SHA-1 covers the .h3 (verified below); the .app is verified
            // block-wise against its hash tree during decryption (W6), so it isn't checked here.
            files.Add(new ResolvedDownload(
                DownloadUrl: $"{titleBase}/{idHex}",
                Title: idHex,
                Platform: Platforms.WiiU,
                SuggestedFilename: $"{idHex}.app",
                RequestHeaders: null,
                ResolvedFormat: 0,
                FormatNote: null,
                ExpectedSha1: content.IsHashed ? null : sha1));

            if (content.IsHashed)
            {
                files.Add(new ResolvedDownload(
                    DownloadUrl: $"{titleBase}/{idHex}.h3",
                    Title: $"{idHex}.h3",
                    Platform: Platforms.WiiU,
                    SuggestedFilename: $"{idHex}.h3",
                    RequestHeaders: null,
                    ResolvedFormat: 0,
                    FormatNote: null,
                    ExpectedSha1: sha1));
            }
        }

        return Result<MultiFileResolution>.Ok(new MultiFileResolution(
            Title: titleId,
            Platform: Platforms.WiiU,
            Files: files,
            SubFolder: titleId));
    }

    private ResolvedDownload Meta(string url, string filename) =>
        new(url, filename, Platforms.WiiU, filename, null, 0, null);

    /// <summary>Accept a 16-hex-digit title ID, tolerating spaces/dashes; returns uppercase, or null.</summary>
    internal static string? NormalizeTitleId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        Span<char> buf = stackalloc char[16];
        var n = 0;
        foreach (var c in raw)
        {
            if (c is ' ' or '-' or '_' or ':') continue;
            if (!Uri.IsHexDigit(c) || n >= 16) return null;
            buf[n++] = char.ToUpperInvariant(c);
        }
        return n == 16 ? new string(buf) : null;
    }
}
