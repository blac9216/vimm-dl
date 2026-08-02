using System.Net;
using Module.Catalog;
using Module.Core;

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

    /// <summary>
    /// Delete-cap defence in depth (#297): the merge above is authoritative, so even a run that passes
    /// every per-fetch guard can still gut a console if the *listing itself* was implausibly short for
    /// a reason no per-page check can see (a markup change, a bug upstream, a partial CDN response that
    /// still parses as a valid — but truncated — list page). Refuse to merge if doing so would drop more
    /// than this fraction of the rows this origin currently sources for the console — an exact set
    /// difference over the merge's own canonical-key survival rule, see
    /// <see cref="CatalogRepository.CountOriginMergeImpactAsync"/>.
    ///
    /// <para>20% is deliberately generous: an ordinary Vimm run of takedowns/consolidations is a handful
    /// of titles out of hundreds, nowhere near this, while a run that would gut most of a console in one
    /// merge — the failure mode this guard exists for — trips it immediately. First-seed (nothing exists
    /// yet for this origin) is exempt: there is nothing to compare against, so any size scrape is
    /// accepted, or a console could never be seeded the first time. Settable for tests.</para>
    /// </summary>
    internal double MaxOriginDeleteFraction { get; set; } = 0.20;

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

    private async Task<Result<bool>> SeedConsoleAsync(VimmSourceSystemInfo sys, Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("vimms");
        var scraped = new List<Scraped>();
        int listed = 0, unreadable = 0, failedSections = 0;

        for (var i = 0; i < Sections.Length; i++)
        {
            var section = Sections[i];
            ct.ThrowIfCancellationRequested();
            report?.Invoke($"{sys.Console}: {section}", i + 1, Sections.Length);

            var listing = await FetchAsync(http,
                $"https://vimm.net/vault/?p=list&system={sys.VimmCode}&section={section}", ct);

            // Vimm has exactly two legitimate section shapes (verified live, #297):
            //   • the section HAS games  → HTTP 200 carrying the <table><caption> list wrapper + rows
            //   • the section has NONE   → HTTP 404 whose body is the "No matches found." page
            // Anything else — a transport failure, a bare 404, or a 200 whose body isn't a list page at
            // all (a rate-limit interstitial, a WAF challenge) — is unusable, and must not be read as
            // "this section legitimately has zero games", or the merge below deletes that whole letter.
            IReadOnlyList<VimmListEntry> rows;
            if (listing.Status == HttpStatusCode.OK && listing.Body is { } ok && VimmVaultParser.IsListPage(ok))
                rows = VimmVaultParser.ParseList(ok);
            else if (listing.Status == HttpStatusCode.NotFound && listing.Body is { } nf
                     && VimmVaultParser.IsEmptySectionPage(nf))
                rows = [];                                  // legitimately empty section, not a failure
            else { failedSections++; continue; }

            foreach (var entry in rows)
            {
                ct.ThrowIfCancellationRequested();
                listed++;
                var (outcome, item) = await ScrapeTitleAsync(http, entry, ct);
                if (item is not null) scraped.Add(item);
                else if (outcome == ScrapeOutcome.Unreadable) unreadable++;
                if (PoliteDelayMs > 0) await Task.Delay(PoliteDelayMs, ct);
            }
        }

        // The merge is authoritative for this origin: anything absent from what we hand it is deleted as
        // though Vimm had delisted it. So every guard below asks the same question — did we actually see
        // the whole console? — and refuses to merge when the answer is no. Each refusal is reported (not
        // just logged) so an aborted seed is visible in the Jobs API, not only in the log (#297).

        // A section whose list never loaded (or didn't parse as a list page at all) is the worst case,
        // because its titles are unknown: they were never counted, so no ratio can detect them, and
        // merging would silently delete that whole letter.
        if (failedSections > 0)
        {
            var msg = $"Vimm seed {sys.Console}: {failedSections}/{Sections.Length} section listings failed — " +
                "the console could not be fully enumerated, so the existing catalog is left untouched";
            log.LogWarning("{Message}", msg);
            report?.Invoke(msg, null, null);
            return Result.Fail(msg);
        }

        if (scraped.Count == 0)
        {
            var msg = $"Vimm seed {sys.Console}: scraped 0 titles, leaving the existing catalog untouched";
            log.LogWarning("{Message}", msg);
            report?.Invoke(msg, null, null);
            return Result.Fail(msg);
        }

        // Listing a title but failing to read its page is a transport failure, not a delisting. Measured
        // on unreadable pages specifically — a title read cleanly that simply publishes no hashes is a
        // legitimate skip and must not count against the run.
        if (unreadable > listed * (1 - MinScrapeCompletion))
        {
            var msg = $"Vimm seed {sys.Console}: {unreadable}/{listed} listed titles could not be read — " +
                "treating as a failed run and leaving the existing catalog untouched";
            log.LogWarning("{Message}", msg);
            report?.Invoke(msg, null, null);
            return Result.Fail(msg);
        }

        var systemId = await catalog.UpsertSystemAsync(sys.DatName, sys.Console, VimmSourceSystems.Origin, ct);

        // Delete-cap defence in depth (#297): even a run that reads everything it listed can still be
        // an implausibly short list (see MaxOriginDeleteFraction). Exempt when nothing exists yet for
        // this origin, or a console could never be seeded the first time.
        // Measured as a set difference on the merge's own survival rule (canonical content key), not as
        // a count difference: a scrape returning the same NUMBER of different titles drops every old row
        // and must trip this, and dedup collapsing scraped rows must not flatter the estimate.
        var incomingKeys = scraped.Select(s => CanonicalKey.Compute(s.Game.Roms)).OfType<string>().ToList();
        var (existingCount, staleCount) =
            await catalog.CountOriginMergeImpactAsync(systemId, VimmSourceSystems.Origin, incomingKeys, ct);
        if (existingCount > 0)
        {
            var deletedFraction = (double)staleCount / existingCount;
            if (deletedFraction > MaxOriginDeleteFraction)
            {
                var msg = $"Vimm seed {sys.Console}: scrape produced {scraped.Count} titles vs {existingCount} " +
                    $"already sourced from vimm — that would delete {staleCount} of them ({deletedFraction:P0} " +
                    $"of the origin's rows, cap {MaxOriginDeleteFraction:P0}), refusing to merge";
                log.LogWarning("{Message}", msg);
                report?.Invoke(msg, null, null);
                return Result.Fail(msg);
            }
        }

        var version = UtcNow().ToString("yyyy-MM-dd");
        await catalog.MergeSystemGamesAsync(systemId, VimmSourceSystems.Origin,
            [.. scraped.Select(s => s.Game)], version, ct);

        var bound = await BindScrapedAsync(sys, scraped, ct);
        log.LogInformation("Vimm seed {Console}: {Count} titles from Vimm, {Bound} bound to a vault entry",
            sys.Console, scraped.Count, bound);
        return Result.Ok();
    }

    /// <summary>Why a title produced no catalog game — a transport failure, or nothing to store.</summary>
    private enum ScrapeOutcome
    {
        /// <summary>Converted successfully.</summary>
        Ok,
        /// <summary>A fetch failed. Counts against the run: the title is presumed still listed.</summary>
        Unreadable,
        /// <summary>Read cleanly but publishes no usable hashes. A legitimate skip, not a failure.</summary>
        NoHashes,
    }

    /// <summary>
    /// Fetch one vault page and convert it into a catalog game. The canonical <c>GoodTitle</c> is the
    /// DAT-style filename ("M&amp;M's Adventure (USA).iso"), so the game name is that minus the
    /// extension and the rom keeps it verbatim — exactly the shape a DAT would have produced.
    ///
    /// <para>The outcome distinguishes "could not read" from "nothing to read", because only the former
    /// means the title may still exist on Vimm and must not be treated as delisted by the merge.</para>
    /// </summary>
    private async Task<(ScrapeOutcome Outcome, Scraped? Item)> ScrapeTitleAsync(
        HttpClient http, VimmListEntry entry, CancellationToken ct)
    {
        var pageHtml = await GetStringOrNull(http, $"https://vimm.net/vault/{entry.VaultId}", ct);
        if (pageHtml is null) return (ScrapeOutcome.Unreadable, null);

        // A 200 whose body isn't structurally a vault page (a rate-limit interstitial, a WAF challenge,
        // …) must not be read as "this title legitimately publishes no hashes" — that silently drops it,
        // and the merge then deletes it as though Vimm had delisted it (#297). Only a page that IS a
        // vault page gets to claim NoHashes below.
        if (!VimmVaultParser.IsVaultPage(pageHtml)) return (ScrapeOutcome.Unreadable, null);

        var media = VimmVaultParser.ParseMedia(pageHtml);
        if (media.Count == 0) return (ScrapeOutcome.NoHashes, null);

        var roms = new List<DatRom>();
        var fetchFailed = false;
        foreach (var m in media)
        {
            if (m.Crc is not null || m.Md5 is not null || m.Sha1 is not null)
            {
                roms.Add(new DatRom(m.Name ?? entry.Title, 0, m.Crc, m.Md5, m.Sha1, m.Serial));
                continue;
            }

            // Multi-disc titles omit the inline hashes — they live behind hashes2.php, keyed by media id.
            var frag = await GetStringOrNull(http, $"https://vimm.net/vault/ajax/hashes2.php?id={m.Id}", ct);
            if (frag is null) { fetchFailed = true; continue; }
            foreach (var f in VimmVaultParser.ParseHashes2(frag))
                roms.Add(new DatRom(f.FileName, 0, f.Crc, f.Md5, f.Sha1, m.Serial));
        }

        // A title left with nothing because a hashes2 fetch died is unreadable, not hash-less.
        if (roms.Count == 0)
            return (fetchFailed ? ScrapeOutcome.Unreadable : ScrapeOutcome.NoHashes, null);

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

        return (ScrapeOutcome.Ok, new Scraped(game, entry.VaultId, VimmFormats.Build(pageHtml, media)));
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

    /// <summary>
    /// One fetch's outcome: the HTTP status (null when the request never produced a response at all —
    /// DNS, connect, timeout) and the body, which is read <b>whatever the status</b>. The body of a
    /// non-success response is not noise here: Vimm answers an empty list section with a 404 whose body
    /// is the only thing distinguishing it from a real failure (#297), so the section classifier needs
    /// status and body together.
    /// </summary>
    private readonly record struct FetchResult(HttpStatusCode? Status, string? Body)
    {
        /// <summary>A 2xx — the same bar <c>GetStringAsync</c> used to apply before it threw.</summary>
        public bool IsSuccess => Status is { } s && (int)s is >= 200 and <= 299;
    }

    private async Task<FetchResult> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(url, ct);
            return new FetchResult(res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning("Vimm fetch failed {Url}: {Error}", url, ex.Message);
            return default;
        }
    }

    /// <summary>
    /// The body of a successful fetch, or null for anything else — what every caller that only cares
    /// about a page it can parse wants (title pages, hashes2 fragments). The section listing is the one
    /// caller that needs the status too, and uses <see cref="FetchAsync"/> directly.
    /// </summary>
    private async Task<string?> GetStringOrNull(HttpClient http, string url, CancellationToken ct)
    {
        var fetch = await FetchAsync(http, url, ct);
        return fetch.IsSuccess ? fetch.Body : null;
    }
}
