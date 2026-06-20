using Module.Catalog;
using Module.Download.Sources;

/// <summary>
/// Resolves a catalog game to a concrete download URL via the console's configured sets:
/// list each set's files and match the game by normalized filename stem (same rule as ownership).
/// archive.org sets only for now — Vimm has no No-Intro/Redump-name → vault-URL mapping.
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

        foreach (var set in sets.Where(s => s.Source == "archive"))
        {
            var files = await cat.ListFilesAsync(set.Identifier, name, http, ct);
            if (!files.IsOk)
            {
                log.LogWarning("Resolve: listing set '{Id}' failed — {Error}", set.Identifier, files.Error);
                continue;
            }
            var url = CatalogMatcher.FindFile(files.Value!.Select(f => (f.Name, f.DownloadUrl)), name);
            if (url != null) return url;
        }
        return null;
    }
}
