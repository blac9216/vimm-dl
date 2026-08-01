using Module.Catalog;

static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // Kick off a background sync of all configured systems (tens of MB across ~95 DATs),
        // so it never blocks the request thread. 409 if one is already running. The DAT source is
        // chosen by the catalog_dat_source setting: the fresher daily bundle, else the libretro mirror.
        app.MapPost("/api/catalog/sync", (CatalogSyncService sync, CatalogSyncState state, QueueRepository repo,
            LibretroDatSource libretro, DailyBundleDatSource bundle, ILogger<CatalogSyncService> log) =>
            state.Run(log, "Catalog sync", async ct =>
            {
                IDatSource source = await repo.GetSettingAsync(SettingsKeys.CatalogDatSource) == "daily-bundle"
                    ? bundle
                    : libretro;
                await sync.SyncAsync(CatalogSystems.All, source, state.Report, ct);
            }));

        // Per-console counts + versions, plus which background jobs are currently running. Shape is
        // frozen (L1 #255) — the Jobs tab reads progress from GET /api/jobs instead; IgdbSyncing folds
        // both the description and rank IGDB gates (split in L1) back into the one legacy flag.
        app.MapGet("/api/catalog/status", async (CatalogRepository repo, CatalogSyncState sync, CatalogScanState scan,
            CatalogCompatState compat, CatalogVerifyState verify, CatalogVimmState vimm, CatalogImportState import,
            CatalogIgdbDescState igdbDesc, CatalogIgdbRankState igdbRank, CatalogRaState ra) =>
        {
            var systems = await repo.GetSystemsAsync();
            return new CatalogStatusResponse(sync.IsRunning, scan.IsRunning, compat.IsRunning, verify.IsRunning,
                vimm.IsRunning, import.IsRunning, igdbDesc.IsRunning || igdbRank.IsRunning, ra.IsRunning,
                systems.Sum(s => s.GameCount), systems);
        });

        // Ingest the import drop folder: hash-match each file → place into completed/{console}/ or
        // set aside in rejected/, one per-file event each. Background, single-flight (202/409).
        app.MapPost("/api/catalog/import", (CatalogImportService svc, CatalogImportState state,
            ILogger<CatalogImportService> log) =>
            state.Run(log, "Import", async ct => { await svc.ImportAsync(state.Report, ct); }));

        // Scrape Vimm's Lair and bind each catalog game to its vault entry by hash (background,
        // single-flight). Optional ?console= to scrape one console; otherwise every Vimm-carried one.
        app.MapPost("/api/catalog/vimm-sync", (string? console, VimmSyncService svc, CatalogVimmState state,
            ILogger<VimmSyncService> log) =>
            state.Run(log, "Vimm sync", ct => svc.SyncAsync(console, state.Report, ct)));

        // Seed the catalog FROM Vimm for consoles with no public DAT (VimmSourceSystems), where Vimm is
        // the authoritative source rather than a link bound onto a DAT-sourced game (background,
        // single-flight). Optional ?console= to seed one; otherwise every Vimm-authoritative console.
        app.MapPost("/api/catalog/vimm-seed", (string? console, VimmCatalogSeedService svc,
            CatalogVimmSeedState state, ILogger<VimmCatalogSeedService> log) =>
            state.Run(log, "Vimm seed", ct => svc.SeedAsync(console, state.Report, ct)));

        // Verify owned files' CRC32 against the catalog (background, single-flight).
        app.MapPost("/api/catalog/verify", (CatalogVerifyService svc, CatalogVerifyState state,
            ILogger<CatalogVerifyService> log) =>
            state.Run(log, "Verify", ct => svc.VerifyAsync(state.Report, ct)));

        // Sync every registered emulator's compatibility list in the background (single-flight).
        app.MapPost("/api/catalog/compat/sync", (CompatSyncService svc, CatalogCompatState state,
            ILogger<CompatSyncService> log) =>
            state.Run(log, "Compatibility sync", ct => svc.SyncAsync(state.Report, ct)));

        // Sync game descriptions from IGDB (Twitch OAuth) in the background, single-flight. No-ops when
        // the user hasn't set Twitch creds (GET /api/settings → igdbClientId/igdbClientSecret).
        // Incremental by default; ?force=true re-pulls + re-stores every game's description.
        app.MapPost("/api/catalog/igdb-sync", (bool? force, IgdbSyncService svc, CatalogIgdbDescState state,
            ILogger<IgdbSyncService> log) =>
            state.Run(log, "IGDB sync", ct => svc.SyncAsync(force ?? false, state.Report, ct)));

        // Sync game RANKINGS from IGDB (total_rating → a per-game quality score the Library sorts by) in
        // the background, single-flight. Has its own gate (L1 #255 split it from the description sync's),
        // so the two IGDB jobs run/gate independently and report as distinct Jobs API kinds — they still
        // share one Twitch token + rate limit via IgdbClient. No-ops without Twitch creds. Incremental by
        // default; ?force=true re-pulls + re-ranks every game.
        app.MapPost("/api/catalog/igdb-rank-sync", (bool? force, IgdbRankSyncService svc, CatalogIgdbRankState state,
            ILogger<IgdbRankSyncService> log) =>
            state.Run(log, "IGDB rank sync", ct => svc.SyncAsync(force ?? false, state.Report, ct)));

        // Sync RetroAchievements popularity (NumDistinctPlayers, hash-joined for cartridge consoles) and
        // blend it into rank_score, in the background (single-flight). No-ops without an RA API key
        // (GET /api/settings → raApiKey). Incremental by default; ?force=true refetches every matched game.
        app.MapPost("/api/catalog/ra-sync", (bool? force, RetroAchievementsSyncService svc, CatalogRaState state,
            ILogger<RetroAchievementsSyncService> log) =>
            state.Run(log, "RetroAchievements sync", ct => svc.SyncAsync(force ?? false, state.Report, ct)));

        // Emulators with ingested compatibility — drives the Library emulator/status filter.
        app.MapGet("/api/catalog/emulators", () =>
            Emulators.All.Select(e => new EmulatorDto(e.Id, e.Name, e.Console, Emulators.Token(e.MatchKind))).ToList());

        // Scan completed/ and record which catalog games are present on disk (background, single-flight).
        app.MapPost("/api/catalog/scan", (CatalogScanService scanner, CatalogScanState state,
            ILogger<CatalogScanService> log) =>
            state.Run(log, "Catalog scan", ct => scanner.ScanAsync(state.Report, ct)));

        // Consoles with counts — for the Library filter.
        app.MapGet("/api/catalog/consoles", async (CatalogRepository repo) => await repo.GetConsolesAsync());

        // Paged game browse, filtered by console and/or name, plus 1G1R / English-only / hide-demos
        // curation. ?mode= selects the name match: substring (default) | glob | regex. ?emulator= (and
        // optional ?compat= status) narrows to games with that emulator's compatibility entry. ?sort=
        // chooses the order: name (default) | rank ("best games" by rank_score, unranked last).
        app.MapGet("/api/catalog/games", async (string? console, string? q, string? local, bool? dedupe,
            bool? english, bool? excludeCategories, string? mode, string? emulator, string? compat,
            string? sort, int? page, int? pageSize, CatalogRepository repo) =>
        {
            var ps = Math.Clamp(pageSize ?? 100, 1, 200);
            var p = Math.Max(0, page ?? 0);
            var (total, games) = await repo.GetGamesAsync(console, q, local ?? "all", dedupe ?? false,
                english ?? false, excludeCategories ?? false, mode ?? "substring", p, ps, emulator, compat,
                sort ?? "name");
            return new CatalogGamesResponse(total, p, ps, games);
        });

        // Curation (E6 R3): the best non-owned games (highest rank_score first) that fit a cumulative
        // byte budget — the "best N up to X GB" picker. Same filter params as /api/catalog/games
        // (availability is forced to non-owned — you can't download what you own); ?budgetBytes= is
        // required, ?maxCount= optional (0/absent = unlimited). Returns the ids to pre-select + their
        // cumulative size; the user then confirms via POST /api/catalog/games/queue.
        app.MapGet("/api/catalog/curate", async (string? console, string? q, bool? dedupe, bool? english,
            bool? excludeCategories, string? mode, string? emulator, string? compat, long? budgetBytes,
            int? maxCount, CatalogRepository repo) =>
        {
            var budget = budgetBytes ?? 0;
            if (budget <= 0) return Results.BadRequest("budgetBytes must be a positive number");
            var (ids, totalBytes) = await repo.SelectBestWithinBudgetAsync(console, q, dedupe ?? false,
                english ?? false, excludeCategories ?? false, mode ?? "substring", emulator, compat,
                budget, Math.Max(0, maxCount ?? 0));
            return Results.Ok(new CatalogCurateResponse(ids, ids.Count, totalBytes, budget));
        });

        // --- download sets (per-console arrays of archive.org links) ---
        // Validate + clean an add/update request → (name, console, links) or an error message.
        static (string Name, string Console, List<string> Links, string? Error) NormalizeSet(AddSetRequest req)
        {
            var name = req.Name?.Trim() ?? "";
            var console = req.Console?.Trim() ?? "";
            var links = (req.Links ?? [])
                .Select(l => l?.Trim() ?? "")
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (name.Length == 0) return (name, console, links, "name is required");
            if (console.Length == 0) return (name, console, links, "console is required");
            if (links.Count == 0) return (name, console, links, "at least one link is required");
            return (name, console, links, null);
        }

        app.MapGet("/api/catalog/sets", async (CatalogRepository repo) => await repo.GetSetsAsync());

        app.MapPost("/api/catalog/sets", async (AddSetRequest req, CatalogRepository repo) =>
        {
            var (name, console, links, error) = NormalizeSet(req);
            if (error != null) return Results.BadRequest(error);
            var id = await repo.AddSetAsync(name, console, links);
            return Results.Ok(new CatalogSetDto((int)id, name, console, links));
        });

        app.MapPut("/api/catalog/sets/{id:int}", async (int id, AddSetRequest req, CatalogRepository repo) =>
        {
            var (name, console, links, error) = NormalizeSet(req);
            if (error != null) return Results.BadRequest(error);
            return await repo.UpdateSetAsync(id, name, console, links)
                ? Results.Ok(new CatalogSetDto(id, name, console, links))
                : Results.NotFound();
        });

        app.MapDelete("/api/catalog/sets/{id:int}", async (int id, CatalogRepository repo) =>
            await repo.DeleteSetAsync(id) ? Results.Ok() : Results.NotFound());

        // A game's cached box art / title screen, fetched from libretro-thumbnails on first request and
        // cached on disk (404s negative-cached). ?type=boxart (default) | title. A 404 means "no art" —
        // the Library shows a placeholder. Long-cached: a given game's image bytes never change.
        app.MapGet("/api/catalog/games/{id:int}/image", async (int id, string? type, MediaService media,
            HttpResponse res, CancellationToken ct) =>
        {
            var kind = (type ?? "boxart").Trim().ToLowerInvariant();
            if (!Module.Catalog.LibretroThumbnails.IsKnownType(kind))
                return Results.BadRequest("type must be boxart or title");
            var path = await media.GetImageAsync(id, kind, ct);
            if (path is null) return Results.NotFound();
            res.Headers.CacheControl = "public, max-age=2592000, immutable"; // 30 days
            return Results.File(path, "image/png");
        });

        // A game's IGDB description for the Library detail panel, or 404 when none is stored (the panel
        // shows a "no description" placeholder). Populated by POST /api/catalog/igdb-sync.
        app.MapGet("/api/catalog/games/{id:int}/description", async (int id, CatalogRepository repo) =>
        {
            var description = await repo.GetDescriptionAsync(id);
            return string.IsNullOrWhiteSpace(description)
                ? Results.NotFound()
                : Results.Ok(new CatalogGameDescription(description));
        });

        // A game's Vimm download options (vault id + available formats) for the download format
        // picker, or 404 when the game has no Vimm match.
        app.MapGet("/api/catalog/games/{id:int}/vimm", async (int id, CatalogRepository repo) =>
        {
            var binding = await repo.GetVaultBindingAsync(id);
            return binding is null
                ? Results.NotFound()
                : Results.Ok(new CatalogVimmDto(binding.Value.VaultId,
                    binding.Value.Formats.Select(f => new CatalogVimmFormatDto(f.Alt, f.Label, f.SizeBytes, f.SizeText)).ToList()));
        });

        // Resolve a catalog game and queue it: prefer archive.org sets, fall back to the game's
        // pre-bound Vimm vault URL (optional ?format= picks the Vimm download format).
        app.MapPost("/api/catalog/games/{id:int}/queue", async (int id, int? format, CatalogRepository repo,
            CatalogResolveService resolver, QueueRepository queue, DownloadQueue downloadQueue, CancellationToken ct) =>
        {
            var game = await repo.GetGameByIdAsync(id);
            if (game is null) return Results.NotFound("Unknown catalog game");

            var resolved = await resolver.ResolveForQueueAsync(id, game.Value.Console, game.Value.Name, format, ct);
            if (resolved is null) return Results.NotFound("Not available from configured archive sets or Vimm");
            var (url, source, fmt, sourceId) = resolved.Value;

            if ((await queue.CheckDuplicatesAsync([url])).Count > 0)
                return Results.Conflict("Already queued or completed");

            await queue.AddToQueueAsync(url, fmt, source, sourceId);
            if (!downloadQueue.IsRunning) await downloadQueue.StartAsync(null);
            return Results.Ok(new CatalogQueueResponse(url, source));
        });

        // Batch-queue several catalog games at once (E3b "queue selected"): each id goes through the
        // same resolve path as the single-queue endpoint (archive-preferred, Vimm fallback, default
        // format). Partial success — already-queued/unavailable ids are reported, not fatal.
        app.MapPost("/api/catalog/games/queue", async (CatalogQueueBatchRequest req, CatalogRepository repo,
            CatalogResolveService resolver, QueueRepository queue, DownloadQueue downloadQueue, CancellationToken ct) =>
        {
            var ids = (req.Ids ?? []).Distinct().ToList();
            if (ids.Count == 0) return Results.BadRequest("No game ids provided");

            var resp = await CatalogQueueOps.ResolveAndQueueBatchAsync(ids, req.Format, repo, resolver, queue, ct);
            if (resp.Queued > 0 && !downloadQueue.IsRunning) await downloadQueue.StartAsync(null);
            return Results.Ok(resp);
        });
    }
}

/// <summary>
/// Single-flight guard + cancellation + progress shared by every background catalog job (L1 #255 — the
/// Jobs API). The implementation lives here once; the marker subclasses below exist only so DI hands
/// each job its own independent instance (so e.g. a scan and a verify can run concurrently but neither
/// twice), tagged with a stable <see cref="Kind"/> string that <c>GET /api/jobs</c> and
/// <c>POST /api/jobs/{kind}/cancel</c> key off of.
/// </summary>
abstract class BackgroundJobGate(string kind)
{
    private int _running;
    private CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private string? _message;
    private int? _current;
    private int? _total;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastCompletedAt;
    private long? _lastDurationMs;
    private string? _lastOutcome;

    /// <summary>Stable identifier for this job — the Jobs API route/response key.</summary>
    public string Kind { get; } = kind;

    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public CancellationToken Token => _cts.Token;

    public bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _cts = new CancellationTokenSource();
        lock (_lock) { _message = null; _current = null; _total = null; _startedAt = DateTimeOffset.UtcNow; }
        return true;
    }

    public void End() => Volatile.Write(ref _running, 0);

    /// <summary>Request cancellation of the running job. No-op (and safe to call) when not running.</summary>
    public void Cancel() => _cts.Cancel();

    /// <summary>
    /// Thread-safe progress checkpoint — services call this at natural checkpoints (per DAT / file /
    /// console section / emulator / page). <paramref name="current"/>/<paramref name="total"/> drive the
    /// Jobs API's <c>percent</c>; either may be omitted when the job doesn't know a total up front.
    /// </summary>
    public void Report(string? message, int? current = null, int? total = null)
    {
        lock (_lock) { _message = message; _current = current; _total = total; }
    }

    /// <summary>Current snapshot for <c>GET /api/jobs</c> — running state, latest progress, elapsed time,
    /// plus the last completed run's outcome/duration (#292, in-memory only, reset on restart).</summary>
    public JobStatusDto Snapshot()
    {
        string? message; int? current; int? total; DateTimeOffset? startedAt;
        DateTimeOffset? lastCompletedAt; long? lastDurationMs; string? lastOutcome;
        lock (_lock)
        {
            message = _message; current = _current; total = _total; startedAt = _startedAt;
            lastCompletedAt = _lastCompletedAt; lastDurationMs = _lastDurationMs; lastOutcome = _lastOutcome;
        }
        double? percent = current is { } c && total is { } t and > 0 ? Math.Round(100.0 * c / t, 2) : null;
        long? elapsedMs = startedAt is { } s ? (long)(DateTimeOffset.UtcNow - s).TotalMilliseconds : null;
        return new JobStatusDto(Kind, IsRunning, message, current, total, percent, startedAt, elapsedMs,
            lastCompletedAt, lastDurationMs, lastOutcome);
    }

    /// <summary>
    /// Run <paramref name="work"/> on a background task if no run is in progress (202 Accepted);
    /// otherwise 409 Conflict. The running flag is always cleared when the work finishes, throws, or is
    /// cancelled via <see cref="Cancel"/>. On every path, records the last-run outcome
    /// ("completed"/"failed"/"cancelled"), its completion time and its frozen duration (#292) — the
    /// message left by the last <see cref="Report"/> call already survives past completion.
    /// Only a cancellation of <em>this gate's</em> token counts as "cancelled": an
    /// <see cref="OperationCanceledException"/> raised by anything else (notably the
    /// <see cref="TaskCanceledException"/> an <c>HttpClient</c> throws on timeout) is a genuine
    /// failure and falls through to the "failed" path, so the UI badges it as Failed.
    /// </summary>
    public IResult Run(ILogger log, string name, Func<CancellationToken, Task> work)
    {
        if (!TryBegin()) return Results.Conflict($"{name} already in progress");
        var began = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            var outcome = "failed";
            try { await work(Token); outcome = "completed"; }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            { log.LogInformation("{Job} cancelled", name); outcome = "cancelled"; }
            catch (Exception ex) { log.LogError(ex, "{Job} crashed", name); outcome = "failed"; }
            finally
            {
                var completedAt = DateTimeOffset.UtcNow;
                var durationMs = (long)(completedAt - began).TotalMilliseconds;
                lock (_lock) { _lastCompletedAt = completedAt; _lastDurationMs = durationMs; _lastOutcome = outcome; }
                End();
            }
        });
        return Results.Accepted();
    }
}

sealed class CatalogSyncState() : BackgroundJobGate("sync");
sealed class CatalogScanState() : BackgroundJobGate("scan");
sealed class CatalogCompatState() : BackgroundJobGate("compat");
sealed class CatalogVerifyState() : BackgroundJobGate("verify");
sealed class CatalogVimmState() : BackgroundJobGate("vimm");
sealed class CatalogVimmSeedState() : BackgroundJobGate("vimm-seed");
sealed class CatalogImportState() : BackgroundJobGate("import");
sealed class CatalogIgdbDescState() : BackgroundJobGate("igdb-description");
sealed class CatalogIgdbRankState() : BackgroundJobGate("igdb-rank");
sealed class CatalogRaState() : BackgroundJobGate("ra");
