using Module.Catalog;
using Module.Download.Sources;
using Module.WiiUSource;

/// <summary>
/// Resolves a catalog game to a concrete download URL via the console's configured sets: a set is
/// a named list of archive.org links, so we list every link's files, concatenate, and match the
/// game by normalized filename stem (same rule as ownership). archive.org only for now — Vimm has
/// no No-Intro/Redump-name → vault-URL mapping yet (that arrives with the Phase B sync binding).
/// </summary>
class CatalogResolveService(
    CatalogRepository catalog, ISourceRegistry sources, IHttpClientFactory httpFactory,
    ILogger<CatalogResolveService> log)
{
    /// <summary>
    /// Resolve a catalog game to a queueable download, preferring archive.org sets and falling back
    /// to the game's pre-bound Vimm vault URL (hash-matched at sync). Returns the URL, the source it
    /// came from, and the format to queue (for Vimm, the requested format if offered, else the first
    /// available). Null when neither archive nor Vimm provides it.
    /// </summary>
    public async Task<(string Url, string Source, int Format, string SourceId)?> ResolveForQueueAsync(
        int gameId, string console, string name, int? requestedFormat, CancellationToken ct)
    {
        // Fast path (#289): the set index already knows which file (and which set link) carries this
        // game, so the download URL is built directly — no live ListFilesAsync walk. Falls through to
        // the live listing below when the game isn't indexed/matched, or its link no longer parses as
        // an archive.org item (index gone stale between an index run and a set edit).
        if (await catalog.GetIndexedArchiveFileAsync(gameId, ct) is { } indexed)
        {
            var indexedId = ArchiveIdentifier(indexed.LinkUrl);
            if (indexedId is not null)
            {
                var indexedUrl = ArchiveSource.BuildDownloadUrl(indexedId, indexed.Name);
                return (indexedUrl, "archive", 0, indexedUrl);
            }
        }

        var archive = await ResolveAsync(console, name, ct);
        if (archive is not null) return (archive, "archive", 0, archive);

        var binding = await catalog.GetVaultBindingAsync(gameId);
        if (binding is not null)
        {
            var alts = binding.Value.Formats.Select(f => f.Alt).ToList();
            var format = requestedFormat is int rf && alts.Contains(rf) ? rf
                : alts.Count > 0 ? alts[0] : 0;
            var vaultUrl = $"https://vimm.net/vault/{binding.Value.VaultId}";
            return (vaultUrl, "vimm", format, vaultUrl);
        }

        // Wii U digital (#266): titles from the CDN DAT carry the 16-hex title id as their serial and
        // have no archive set or vault binding — they download straight from NUS, keyed by that id.
        // Checked last so a disc release bound to Vimm still wins.
        return await ResolveNusAsync(gameId, console);
    }

    /// <summary>
    /// Resolve a Wii U digital title to its NUS download, or null when the game isn't one. The serial
    /// must pass the very rule <see cref="WiiUNusSource"/> will apply to it, so a game that resolves
    /// here is one that source can actually fetch.
    /// </summary>
    private async Task<(string Url, string Source, int Format, string SourceId)?> ResolveNusAsync(int gameId, string console)
    {
        if (!string.Equals(console, "wiiu", StringComparison.OrdinalIgnoreCase)) return null;

        var titleId = WiiUNusSource.NormalizeTitleId(await catalog.GetGameSerialAsync(gameId));
        if (titleId is null) return null;

        // A real, unique per-title URL so the queue's duplicate check and history stay meaningful;
        // WiiUNusSource itself resolves the concrete file list from the source id.
        return ($"{WiiUNusSource.DefaultBaseUrl}/{titleId.ToLowerInvariant()}", "wiiu", 0, titleId);
    }

    public async Task<string?> ResolveAsync(string console, string name, CancellationToken ct)
    {
        var sets = await catalog.GetSetsByConsoleAsync(console);
        if (sets.Count == 0) return null;
        if (sources.Get("archive") is not ICatalogSource cat) return null;
        var http = httpFactory.CreateClient(((IDownloadSource)cat).HttpClientName);

        foreach (var set in sets)
        {
            foreach (var link in set.Links)
            {
                var identifier = ArchiveIdentifier(link);
                if (identifier is null) continue; // non-archive link (lolroms/Minerva) — skip
                var files = await cat.ListFilesAsync(identifier, name, http, ct);
                if (!files.IsOk)
                {
                    log.LogWarning("Resolve: listing '{Id}' failed — {Error}", identifier, files.Error);
                    continue;
                }
                var url = CatalogMatcher.FindFile(files.Value!.Select(f => (f.Name, f.DownloadUrl)), name);
                if (url != null) return url;
            }
        }
        return null;
    }

    /// <summary>
    /// archive.org item identifier from a set link — a <c>/download/&lt;id&gt;</c>, <c>/details/&lt;id&gt;</c>
    /// or <c>/metadata/&lt;id&gt;</c> URL, or a bare identifier. Non-archive links return null (skipped).
    /// </summary>
    internal static string? ArchiveIdentifier(string link)
    {
        var t = link.Trim();
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("archive.org", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase)) return null;
            var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 2 && (segs[0].Equals("download", StringComparison.OrdinalIgnoreCase)
                                     || segs[0].Equals("details", StringComparison.OrdinalIgnoreCase)
                                     || segs[0].Equals("metadata", StringComparison.OrdinalIgnoreCase)))
                return Uri.UnescapeDataString(segs[1]);
            return null;
        }
        // bare identifier (no scheme, no path separators)
        return t.Length > 0 && !t.Contains('/') ? t : null;
    }
}
