using Module.Catalog;
using Module.Download.Sources;

/// <summary>
/// Indexes every configured download set's archive.org listing up front (RomGoGetter-style, #289) so
/// the Library can show per-game archive availability — and <see cref="CatalogResolveService"/> can
/// build a download URL directly — without a live <c>ListFilesAsync</c> call per queue request. Mirrors
/// <see cref="VimmSyncService"/>'s shape: a throttle-free, per-console, cancellable background job that
/// matches HASH FIRST (SHA1 → MD5 → CRC, the project's identity-by-hash convention, same priority as the
/// Vimm binding) with a name-stem fallback (<see cref="CatalogMatcher"/>, the same rule
/// <c>CatalogScanService</c> uses for on-disk ownership) when the listing carries no hash for a file.
///
/// Non-archive links (lolroms/Minerva — <see cref="CatalogResolveService.ArchiveIdentifier"/> returns
/// null for them) are skipped, same as the live resolve path (#116 tracks other sources). A listing
/// failure for one link leaves that link's previously-indexed rows untouched rather than wiping them —
/// a transient archive.org hiccup shouldn't erase a working index.
/// </summary>
class SetIndexService(CatalogRepository catalog, ISourceRegistry sources, IHttpClientFactory httpFactory,
    ILogger<SetIndexService> log)
{
    /// <summary>Hash-kind priority — lower wins when a game's files carry more than one kind of match
    /// across a console's set links (mirrors the SHA1 → MD5 → CRC order everywhere else).</summary>
    private static int Rank(string kind) => kind switch { "sha1" => 0, "md5" => 1, "crc" => 2, _ => 3 };

    /// <summary>
    /// Index one console's configured sets, or every console with at least one set when
    /// <paramref name="console"/> is null. <paramref name="report"/>, when given, is called once per set
    /// link (message = "{console}: link {n} of {total}") — the Jobs API progress checkpoint.
    /// </summary>
    public async Task IndexAsync(string? console, Action<string?, int?, int?>? report, CancellationToken ct)
    {
        if (sources.Get("archive") is not ICatalogSource cat)
        {
            log.LogWarning("Set index: no archive catalog source registered, nothing to index");
            return;
        }
        var http = httpFactory.CreateClient(((IDownloadSource)cat).HttpClientName);

        var consoles = console is null
            ? await catalog.GetIndexableConsolesAsync(ct)
            : [console];

        foreach (var c in consoles)
            await IndexConsoleAsync(c, cat, http, report, ct);
    }

    private async Task IndexConsoleAsync(string console, ICatalogSource cat, HttpClient http,
        Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var links = await catalog.GetLinksForConsoleAsync(console, ct);
        if (links.Count == 0) return;

        // Built once per console and reused across every link: the per-console rom-hash index (same
        // shape as the Vimm binding's) plus a name-stem index for the fallback match.
        var hashIndex = await catalog.GetRomHashIndexAsync(console, ct);
        var nameIndex = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (id, name) in await catalog.GetGamesForConsoleAsync(console))
            nameIndex[CatalogMatcher.Normalize(name)] = id; // last wins on a duplicate name (same as CatalogMatcher.Match)

        // The best (lowest-rank) match kind seen for each game across every link of this console —
        // applied once at the end so a game that appears in more than one set keeps its strongest match.
        var best = new Dictionary<long, string>();

        for (var i = 0; i < links.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var link = links[i];
            report?.Invoke($"{console}: link {i + 1} of {links.Count}", i + 1, links.Count);

            var identifier = CatalogResolveService.ArchiveIdentifier(link.Url);
            if (identifier is null) continue; // non-archive link (lolroms/Minerva) — skip, as resolve does

            var filesResult = await cat.ListFilesAsync(identifier, null, http, ct);
            if (!filesResult.IsOk)
            {
                log.LogWarning("Set index: listing '{Id}' failed — {Error}", identifier, filesResult.Error);
                continue; // leave this link's prior rows (if any) untouched
            }

            var rows = new List<CatalogRepository.SetFileRow>();
            foreach (var f in filesResult.Value!)
            {
                var (gameId, kind) = MatchFile(f, hashIndex, nameIndex);
                rows.Add(new CatalogRepository.SetFileRow(f.Name, f.Size, f.Crc32, f.Md5, f.Sha1, gameId, kind));
                if (gameId is not null && (!best.TryGetValue(gameId.Value, out var cur) || Rank(kind!) < Rank(cur)))
                    best[gameId.Value] = kind!;
            }

            await catalog.ReplaceSetLinkFilesAsync(link.Id, rows, ct);
        }

        await catalog.ApplyArchiveMatchesAsync(console, best.Select(kv => (kv.Key, kv.Value)).ToList(), ct);
    }

    /// <summary>Hash-first (SHA1 → MD5 → CRC), then a name-stem fallback against the console's games.</summary>
    private static (long? GameId, string? Kind) MatchFile(CatalogFile f,
        CatalogRepository.VimmHashIndex hashIndex, IReadOnlyDictionary<string, long> nameIndex)
    {
        if (f.Sha1 is not null && hashIndex.BySha1.TryGetValue(f.Sha1, out var bySha1)) return (bySha1, "sha1");
        if (f.Md5 is not null && hashIndex.ByMd5.TryGetValue(f.Md5, out var byMd5)) return (byMd5, "md5");
        if (f.Crc32 is not null && hashIndex.ByCrc.TryGetValue(f.Crc32, out var byCrc)) return (byCrc, "crc");

        var stem = CatalogMatcher.Normalize(System.IO.Path.GetFileNameWithoutExtension(f.Name));
        return nameIndex.TryGetValue(stem, out var byName) ? (byName, "name") : (null, null);
    }
}
