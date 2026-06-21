using Module.Catalog;

/// <summary>
/// Scrapes Vimm's Lair per console and binds each catalog game to its Vimm vault entry by HASH —
/// authoritative (CRC32/MD5/SHA1 from Vimm vs <c>catalog_rom</c>), not name-matched — capturing the
/// available download formats while it's there. Hashes/formats aren't in the list view, so every
/// title needs a vault-page fetch (+1 for multi-disc <c>hashes2.php</c>); this is therefore a polite,
/// throttled, cancellable background job (per-console, incremental). Games on a scraped console that
/// don't match are flagged "no Vimm match" for manual fixup.
/// </summary>
class VimmSyncService(CatalogRepository catalog, IHttpClientFactory httpFactory, ILogger<VimmSyncService> log)
{
    private static readonly string[] Sections =
        [.. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()), "number"];

    /// <summary>Delay between per-title vault-page fetches (politeness). Settable for tests.</summary>
    internal int PoliteDelayMs { get; set; } = 250;

    /// <summary>Sync one console (by EmuDeck folder) or, when null, every Vimm-carried console.</summary>
    public async Task SyncAsync(string? console, CancellationToken ct)
    {
        var targets = console is null
            ? VimmSystems.All
            : VimmSystems.All.Where(s => s.Console == console).ToList();
        foreach (var sys in targets)
            await SyncConsoleAsync(sys, ct);
    }

    private async Task SyncConsoleAsync(VimmSystemInfo sys, CancellationToken ct)
    {
        var index = await catalog.GetVimmHashIndexAsync(sys.Console, ct);
        if (index.BySha1.Count == 0 && index.ByMd5.Count == 0 && index.ByCrc.Count == 0)
        {
            log.LogInformation("Vimm sync {Console}: catalog has no hashes for this console (sync it first), skipping", sys.Console);
            return;
        }

        var http = httpFactory.CreateClient("vimms");
        int matched = 0, scanned = 0;
        foreach (var section in Sections)
        {
            ct.ThrowIfCancellationRequested();
            var listHtml = await GetStringOrNull(http,
                $"https://vimm.net/vault/?p=list&system={sys.VimmCode}&section={section}", ct);
            if (listHtml is null) continue;
            foreach (var entry in VimmVaultParser.ParseList(listHtml))
            {
                ct.ThrowIfCancellationRequested();
                scanned++;
                if (await BindEntryAsync(http, entry, index, ct)) matched++;
                if (PoliteDelayMs > 0) await Task.Delay(PoliteDelayMs, ct);
            }
        }

        var unmatched = await catalog.MarkVimmUnmatchedAsync(sys.Console, ct);
        log.LogInformation("Vimm sync {Console}: matched {Matched}/{Scanned}, flagged {Unmatched} as no-match",
            sys.Console, matched, scanned, unmatched);
    }

    private async Task<bool> BindEntryAsync(HttpClient http, VimmListEntry entry,
        CatalogRepository.VimmHashIndex index, CancellationToken ct)
    {
        var pageHtml = await GetStringOrNull(http, $"https://vimm.net/vault/{entry.VaultId}", ct);
        if (pageHtml is null) return false;
        var media = VimmVaultParser.ParseMedia(pageHtml);
        if (media.Count == 0) return false;

        // Single-file titles carry the hash inline; multi-disc titles don't → fall back to the
        // per-media hashes2.php endpoint (keyed by each media entry's own ID, not the vault id).
        var (gameId, kind) = MatchInline(media, index);
        if (gameId is null)
        {
            foreach (var m in media)
            {
                if (m.Crc is not null || m.Md5 is not null || m.Sha1 is not null) continue; // had inline hash
                var frag = await GetStringOrNull(http, $"https://vimm.net/vault/ajax/hashes2.php?id={m.Id}", ct);
                if (frag is null) continue;
                (gameId, kind) = MatchHashes2(VimmVaultParser.ParseHashes2(frag), index);
                if (gameId is not null) break;
            }
        }
        if (gameId is null) return false;

        await catalog.BindVimmAsync(gameId.Value, entry.VaultId, kind!, BuildFormats(pageHtml, media), ct);
        return true;
    }

    /// <summary>First media entry whose inline hash hits the index (SHA1 → MD5 → CRC).</summary>
    private static (long? GameId, string? Kind) MatchInline(IReadOnlyList<VimmMedia> media, CatalogRepository.VimmHashIndex index)
    {
        foreach (var m in media)
        {
            if (m.Sha1 is not null && index.BySha1.TryGetValue(m.Sha1, out var g1)) return (g1, "sha1");
            if (m.Md5 is not null && index.ByMd5.TryGetValue(m.Md5, out var g2)) return (g2, "md5");
            if (m.Crc is not null && index.ByCrc.TryGetValue(m.Crc, out var g3)) return (g3, "crc");
        }
        return (null, null);
    }

    /// <summary>First hashes2 file whose hash hits the index (SHA1 → MD5 → CRC).</summary>
    private static (long? GameId, string? Kind) MatchHashes2(IReadOnlyList<VimmFileHash> files, CatalogRepository.VimmHashIndex index)
    {
        foreach (var f in files)
        {
            if (index.BySha1.TryGetValue(f.Sha1, out var g1)) return (g1, "sha1");
            if (index.ByMd5.TryGetValue(f.Md5, out var g2)) return (g2, "md5");
            if (index.ByCrc.TryGetValue(f.Crc, out var g3)) return (g3, "crc");
        }
        return (null, null);
    }

    /// <summary>
    /// The game's downloadable formats: the dl_format options (multi-format titles) paired with their
    /// sizes from the media JSON, or a single implicit format 0 (single-file titles) labelled by the
    /// ROM extension.
    /// </summary>
    private static IReadOnlyList<CatalogRepository.VimmFormatRow> BuildFormats(string pageHtml, IReadOnlyList<VimmMedia> media)
    {
        var sizes = media.Count > 0 ? media[0].Sizes : [];
        var sizeByAlt = sizes.ToDictionary(s => s.Alt);
        var labels = VimmVaultParser.ParseFormats(pageHtml);
        var rows = new List<CatalogRepository.VimmFormatRow>();
        if (labels.Count > 0)
        {
            foreach (var f in labels)
            {
                sizeByAlt.TryGetValue(f.Alt, out var sz);
                rows.Add(new(f.Alt, f.Label, sz?.Bytes ?? 0, sz?.Text));
            }
        }
        else
        {
            var sz = sizes.Count > 0 ? sizes[0] : null;
            var label = ExtensionOf(media.Count > 0 ? media[0].Name : null) ?? "Download";
            rows.Add(new(0, label, sz?.Bytes ?? 0, sz?.Text));
        }
        return rows;
    }

    private static string? ExtensionOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var ext = Path.GetExtension(name);
        return string.IsNullOrEmpty(ext) ? null : ext;
    }

    private async Task<string?> GetStringOrNull(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            return await http.GetStringAsync(url, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning("Vimm fetch failed {Url}: {Error}", url, ex.Message);
            return null;
        }
    }
}
