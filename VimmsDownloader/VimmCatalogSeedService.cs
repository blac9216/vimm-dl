using Module.Catalog;

/// <summary>
/// Seeds the catalog <b>from</b> Vimm's Lair for the consoles in <see cref="VimmSourceSystems"/> — the
/// consoles that have no public DAT, where Vimm is the authoritative source rather than a download link
/// bound onto a DAT-sourced game.
///
/// <para>Vimm's vault pages publish the canonical Redump hash triple (see the remarks on
/// <see cref="VimmSourceSystems"/> for the cross-check that establishes this), so each scraped title
/// converts cleanly into a <see cref="DatGame"/> and goes through the <i>same</i> persistence path a
/// real DAT would: <see cref="CatalogRepository.MergeSystemGamesAsync"/>, which stores the hashes in
/// <c>catalog_rom</c>, derives the title/serial keys, dedups by canonical key, and records the origin.
/// Nothing downstream — owned detection, dedup, duplicate checks — needs a Vimm-shaped special case.</para>
///
/// <para>Two passes per console: scrape → merge the games, then bind each vault id + format list onto
/// the row it produced, matched by the hash we just stored. The bind pass reuses the normal hash index,
/// so it is self-checking: a title that fails to bind means our own insert disagreed with the page.</para>
///
/// <para>Like <see cref="VimmSyncService"/> this is a polite, throttled, cancellable background job —
/// hashes live only on the per-title vault page, so every title costs a fetch.</para>
/// </summary>
class VimmCatalogSeedService(CatalogRepository catalog, IHttpClientFactory httpFactory,
    ILogger<VimmCatalogSeedService> log)
{
    private static readonly string[] Sections =
        [.. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()), "number"];

    /// <summary>Delay between per-title vault-page fetches (politeness). Settable for tests.</summary>
    internal int PoliteDelayMs { get; set; } = 250;

    /// <summary>
    /// Fraction of the titles the list view advertised that must actually be read before the merge is
    /// allowed to speak for the whole console. Below this the run is treated as failed rather than as
    /// "Vimm delisted everything we couldn't fetch". Settable for tests.
    /// </summary>
    internal double MinScrapeCompletion { get; set; } = 0.9;

    /// <summary>Scrape date recorded as the system's "version". Settable for tests.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>A scraped title: the catalog game plus what is needed to bind its vault entry.</summary>
    private sealed record Scraped(DatGame Game, long VaultId, IReadOnlyList<CatalogRepository.VimmFormatRow> Formats);

    /// <summary>
    /// Seed one Vimm-authoritative console, or all of them when <paramref name="console"/> is null.
    /// <paramref name="report"/> is called once per section fetch — the Jobs API progress checkpoint.
    /// </summary>
    public async Task SeedAsync(string? console, Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var targets = console is null
            ? VimmSourceSystems.All
            : VimmSourceSystems.All.Where(s => s.Console == console).ToList();

        if (targets.Count == 0 && console is not null)
        {
            log.LogInformation("Vimm seed: '{Console}' is not a Vimm-authoritative console, nothing to do", console);
            return;
        }

        foreach (var sys in targets)
            await SeedConsoleAsync(sys, report, ct);
    }

    public Task SeedAsync(string? console, CancellationToken ct) => SeedAsync(console, null, ct);

    private async Task SeedConsoleAsync(VimmSourceSystemInfo sys, Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("vimms");
        var scraped = new List<Scraped>();
        var listed = 0;

        for (var i = 0; i < Sections.Length; i++)
        {
            var section = Sections[i];
            ct.ThrowIfCancellationRequested();
            report?.Invoke($"{sys.Console}: {section}", i + 1, Sections.Length);

            var listHtml = await GetStringOrNull(http,
                $"https://vimm.net/vault/?p=list&system={sys.VimmCode}&section={section}", ct);
            if (listHtml is null) continue;

            foreach (var entry in VimmVaultParser.ParseList(listHtml))
            {
                ct.ThrowIfCancellationRequested();
                listed++;
                if (await ScrapeTitleAsync(http, entry, ct) is { } s) scraped.Add(s);
                if (PoliteDelayMs > 0) await Task.Delay(PoliteDelayMs, ct);
            }
        }

        if (scraped.Count == 0)
        {
            // Never wipe a populated system on a failed scrape: merging an empty set would drop the
            // 'vimm' origin from every game and delete the ones left with no other origin.
            log.LogWarning("Vimm seed {Console}: scraped 0 titles, leaving the existing catalog untouched", sys.Console);
            return;
        }

        // Same failure mode, partially: the merge is authoritative for this origin, so titles missing
        // from a half-finished scrape would be deleted as though Vimm had dropped them. Listing a title
        // but failing to read its page is a transport failure, not a delisting — so require most of what
        // the list view advertised before letting the merge speak for the whole console.
        if (scraped.Count < listed * MinScrapeCompletion)
        {
            log.LogWarning(
                "Vimm seed {Console}: only {Scraped}/{Listed} listed titles could be read — treating as a " +
                "failed run and leaving the existing catalog untouched", sys.Console, scraped.Count, listed);
            return;
        }

        var systemId = await catalog.UpsertSystemAsync(sys.DatName, sys.Console, VimmSourceSystems.Origin, ct);
        var version = UtcNow().ToString("yyyy-MM-dd");
        await catalog.MergeSystemGamesAsync(systemId, VimmSourceSystems.Origin,
            [.. scraped.Select(s => s.Game)], version, ct);

        var bound = await BindScrapedAsync(sys, scraped, ct);
        log.LogInformation("Vimm seed {Console}: {Count} titles from Vimm, {Bound} bound to a vault entry",
            sys.Console, scraped.Count, bound);
    }

    /// <summary>
    /// Fetch one vault page and convert it into a catalog game. The canonical <c>GoodTitle</c> is the
    /// DAT-style filename ("M&amp;M's Adventure (USA).iso"), so the game name is that minus the
    /// extension and the rom keeps it verbatim — exactly the shape a DAT would have produced.
    /// </summary>
    private async Task<Scraped?> ScrapeTitleAsync(HttpClient http, VimmListEntry entry, CancellationToken ct)
    {
        var pageHtml = await GetStringOrNull(http, $"https://vimm.net/vault/{entry.VaultId}", ct);
        if (pageHtml is null) return null;

        var media = VimmVaultParser.ParseMedia(pageHtml);
        if (media.Count == 0) return null;

        var roms = new List<DatRom>();
        foreach (var m in media)
        {
            if (m.Crc is not null || m.Md5 is not null || m.Sha1 is not null)
            {
                roms.Add(new DatRom(m.Name ?? entry.Title, 0, m.Crc, m.Md5, m.Sha1, m.Serial));
                continue;
            }

            // Multi-disc titles omit the inline hashes — they live behind hashes2.php, keyed by media id.
            var frag = await GetStringOrNull(http, $"https://vimm.net/vault/ajax/hashes2.php?id={m.Id}", ct);
            if (frag is null) continue;
            foreach (var f in VimmVaultParser.ParseHashes2(frag))
                roms.Add(new DatRom(f.FileName, 0, f.Crc, f.Md5, f.Sha1, m.Serial));
        }

        if (roms.Count == 0) return null;

        var name = GameNameFrom(media[0].Name, entry.Title);
        var game = new DatGame(
            Name: name,
            // Vimm exposes region as its own page field, but the canonical name already carries the
            // "(USA)" tag that Dedup's region logic reads — so leave this null exactly as the XML DAT
            // path does, rather than introducing a second, drifting region parser.
            Region: null,
            Serial: media[0].Serial,
            Roms: roms,
            Languages: ClrMameProParser.ParseLanguages(name));

        return new Scraped(game, entry.VaultId, VimmFormats.Build(pageHtml, media));
    }

    /// <summary>
    /// Bind each scraped title's vault id + formats onto the game its hashes produced. Matching through
    /// the ordinary hash index (rather than trusting insert order) keeps this honest: anything that
    /// fails to bind is a real disagreement between the page and what we stored.
    /// </summary>
    private async Task<int> BindScrapedAsync(VimmSourceSystemInfo sys, List<Scraped> scraped, CancellationToken ct)
    {
        var index = await catalog.GetVimmHashIndexAsync(sys.Console, ct);
        var bound = 0;

        foreach (var s in scraped)
        {
            ct.ThrowIfCancellationRequested();
            var hit = MatchByHash(index, s.Game.Roms);
            if (hit is not { } m)
            {
                log.LogWarning("Vimm seed {Console}: '{Name}' did not bind back to its own stored hashes",
                    sys.Console, s.Game.Name);
                continue;
            }
            await catalog.BindVimmAsync(m.GameId, s.VaultId, m.Kind, s.Formats, ct);
            bound++;
        }
        return bound;
    }

    /// <summary>First rom whose hash hits the index, SHA1 → MD5 → CRC (the catalog's usual priority).</summary>
    private static (long GameId, string Kind)? MatchByHash(CatalogRepository.VimmHashIndex index, IReadOnlyList<DatRom> roms)
    {
        foreach (var r in roms)
        {
            if (r.Sha1 is { Length: > 0 } sha1 && index.BySha1.TryGetValue(sha1.ToLowerInvariant(), out var gs)) return (gs, "sha1");
            if (r.Md5 is { Length: > 0 } md5 && index.ByMd5.TryGetValue(md5.ToLowerInvariant(), out var gm)) return (gm, "md5");
            if (r.Crc is { Length: > 0 } crc && index.ByCrc.TryGetValue(crc.ToLowerInvariant(), out var gc)) return (gc, "crc");
        }
        return null;
    }

    /// <summary>The DAT-style game name: the canonical filename without its extension.</summary>
    internal static string GameNameFrom(string? goodTitle, string fallback)
    {
        if (string.IsNullOrWhiteSpace(goodTitle)) return fallback;
        var withoutExt = Path.GetFileNameWithoutExtension(goodTitle);
        return string.IsNullOrWhiteSpace(withoutExt) ? goodTitle : withoutExt;
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
