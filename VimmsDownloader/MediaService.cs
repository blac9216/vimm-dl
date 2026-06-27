using System.Net;
using Module.Catalog;

/// <summary>
/// Lazily fetches and on-disk caches catalog game images (box art / title screens) from
/// libretro-thumbnails, backing <c>GET /api/catalog/games/{id}/image</c>. On a cache miss it walks the
/// game's candidate CDN URLs (exact No-Intro name, then truncated-before-<c>(</c>); the first 200 is
/// written under <c>data/media/</c> and recorded in <c>catalog_media</c>. A definitive 404 across all
/// candidates is negative-cached so it isn't retried on every view; transient/network errors are NOT
/// cached, so they retry on the next request. Backend-is-king: the frontend just points an
/// <c>&lt;img&gt;</c> at the endpoint.
/// </summary>
class MediaService(CatalogRepository catalog, IHttpClientFactory httpFactory, ILogger<MediaService> log)
{
    private const string Source = "libretro";
    private string _mediaRoot = "media";

    /// <summary>
    /// Set the cache root (<c>data/media</c>), wired from the data dir at startup. Resolved to an
    /// absolute path so the cached file paths handed to <c>Results.File</c> are rooted even when the
    /// data dir is relative (bare-metal dev runs the DB out of the working directory).
    /// </summary>
    public void Configure(string mediaRoot) => _mediaRoot = Path.GetFullPath(mediaRoot);

    /// <summary>
    /// The on-disk path of a game's cached image, fetching + caching it on first request. Returns null
    /// when the game is unknown, the image is missing (404, negative-cached), or a transient error
    /// prevented the fetch. <paramref name="type"/> must already be a known kind (boxart | title).
    /// </summary>
    public async Task<string?> GetImageAsync(int gameId, string type, CancellationToken ct)
    {
        // Cached record wins: serve the file if it's still on disk, honour a negative-cache miss.
        var rec = await catalog.GetMediaAsync(gameId, type);
        if (rec is { } cached)
        {
            if (cached.Status == "ok" && cached.Path is { Length: > 0 } p && File.Exists(p)) return p;
            if (cached.Status == "missing") return null;
            // 'ok' but the file vanished (cache cleared / user deleted) → fall through and re-fetch.
        }

        var key = await catalog.GetGameMediaKeyAsync(gameId);
        if (key is null) return null; // unknown game id
        var (datName, console, name) = key.Value;

        var http = httpFactory.CreateClient("thumbnails");
        foreach (var url in LibretroThumbnails.Urls(datName, type, name))
        {
            HttpResponseMessage resp;
            try
            {
                resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Network/DNS/TLS failure — transient. Don't poison the negative cache; retry next view.
                log.LogWarning("thumbnail fetch error for {Url}: {Error}", url, ex.Message);
                return null;
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound) continue; // try the next name candidate
                if (!resp.IsSuccessStatusCode)
                {
                    // 5xx / throttling / unexpected — transient, not a definitive miss. Don't cache.
                    log.LogWarning("thumbnail fetch {Status} for {Url}", (int)resp.StatusCode, url);
                    return null;
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                var path = CachePath(console, type, gameId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Write to a unique temp file then move, so a crash mid-write can't leave a torn image
                // that gets served + cached as 'ok'.
                var tmp = path + "." + Path.GetRandomFileName();
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                File.Move(tmp, path, overwrite: true);
                await catalog.UpsertMediaAsync(gameId, type, Source, "ok", path);
                return path;
            }
        }

        // Every candidate returned 404 → the image genuinely doesn't exist for this game. Negative-cache
        // it so the next view is a cheap DB hit, not another doomed round-trip.
        await catalog.UpsertMediaAsync(gameId, type, Source, "missing", null);
        return null;
    }

    /// <summary><c>{root}/libretro/{console}/{type}/{gameId}.png</c> (console grouped, like completed/).</summary>
    private string CachePath(string console, string type, int gameId)
    {
        var consoleDir = string.IsNullOrWhiteSpace(console) ? "_" : console;
        return Path.Combine(_mediaRoot, Source, consoleDir, type, $"{gameId}.png");
    }
}
