using Module.Catalog;
using Module.Download.Sources;

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
