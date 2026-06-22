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
                await sync.SyncAsync(CatalogSystems.All, source, ct);
            }));

        // Per-console counts + versions, plus which background jobs are currently running.
        app.MapGet("/api/catalog/status", async (CatalogRepository repo, CatalogSyncState sync, CatalogScanState scan,
            CatalogCompatState compat, CatalogVerifyState verify, CatalogVimmState vimm, CatalogImportState import) =>
        {
            var systems = await repo.GetSystemsAsync();
            return new CatalogStatusResponse(sync.IsRunning, scan.IsRunning, compat.IsRunning, verify.IsRunning,
                vimm.IsRunning, import.IsRunning, systems.Sum(s => s.GameCount), systems);
        });

        // Ingest the import drop folder: hash-match each file → place into completed/{console}/ or
        // set aside in rejected/, one per-file event each. Background, single-flight (202/409).
        app.MapPost("/api/catalog/import", (CatalogImportService svc, CatalogImportState state,
            ILogger<CatalogImportService> log) =>
            state.Run(log, "Import", async ct => { await svc.ImportAsync(ct); }));

        // Scrape Vimm's Lair and bind each catalog game to its vault entry by hash (background,
        // single-flight). Optional ?console= to scrape one console; otherwise every Vimm-carried one.
        app.MapPost("/api/catalog/vimm-sync", (string? console, VimmSyncService svc, CatalogVimmState state,
            ILogger<VimmSyncService> log) =>
            state.Run(log, "Vimm sync", ct => svc.SyncAsync(console, ct)));

        // Verify owned files' CRC32 against the catalog (background, single-flight).
        app.MapPost("/api/catalog/verify", (CatalogVerifyService svc, CatalogVerifyState state,
            ILogger<CatalogVerifyService> log) =>
            state.Run(log, "Verify", svc.VerifyAsync));

        // Sync emulator compatibility (RPCS3 export) in the background (single-flight).
        app.MapPost("/api/catalog/compat/sync", (CompatSyncService svc, CatalogCompatState state,
            ILogger<CompatSyncService> log) =>
            state.Run(log, "Compatibility sync", svc.SyncAsync));

        // Scan completed/ and record which catalog games are present on disk (background, single-flight).
        app.MapPost("/api/catalog/scan", (CatalogScanService scanner, CatalogScanState state,
            ILogger<CatalogScanService> log) =>
            state.Run(log, "Catalog scan", scanner.ScanAsync));

        // Consoles with counts — for the Library filter.
        app.MapGet("/api/catalog/consoles", async (CatalogRepository repo) => await repo.GetConsolesAsync());

        // Paged game browse, filtered by console and/or name.
        app.MapGet("/api/catalog/games", async (string? console, string? q, string? local, bool? dedupe,
            int? page, int? pageSize, CatalogRepository repo) =>
        {
            var ps = Math.Clamp(pageSize ?? 100, 1, 200);
            var p = Math.Max(0, page ?? 0);
            var (total, games) = await repo.GetGamesAsync(console, q, local ?? "all", dedupe ?? false, p, ps);
            return new CatalogGamesResponse(total, p, ps, games);
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
            var (url, source, fmt) = resolved.Value;

            if ((await queue.CheckDuplicatesAsync([url])).Count > 0)
                return Results.Conflict("Already queued or completed");

            await queue.AddToQueueAsync(url, fmt, source);
            if (!downloadQueue.IsRunning) await downloadQueue.StartAsync(null);
            return Results.Ok(new CatalogQueueResponse(url, source));
        });
    }
}

/// <summary>
/// Single-flight guard + cancellation shared by every background catalog job. The single-flight
/// implementation lives here once; the marker subclasses below exist only so DI hands each job its
/// own independent instance (so e.g. a scan and a verify can run concurrently but neither twice).
/// </summary>
abstract class BackgroundJobGate
{
    private int _running;
    private CancellationTokenSource _cts = new();

    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public CancellationToken Token => _cts.Token;

    public bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _cts = new CancellationTokenSource();
        return true;
    }

    public void End() => Volatile.Write(ref _running, 0);

    /// <summary>
    /// Run <paramref name="work"/> on a background task if no run is in progress (202 Accepted);
    /// otherwise 409 Conflict. The running flag is always cleared when the work finishes or throws.
    /// </summary>
    public IResult Run(ILogger log, string name, Func<CancellationToken, Task> work)
    {
        if (!TryBegin()) return Results.Conflict($"{name} already in progress");
        _ = Task.Run(async () =>
        {
            try { await work(Token); }
            catch (Exception ex) { log.LogError(ex, "{Job} crashed", name); }
            finally { End(); }
        });
        return Results.Accepted();
    }
}

sealed class CatalogSyncState : BackgroundJobGate;
sealed class CatalogScanState : BackgroundJobGate;
sealed class CatalogCompatState : BackgroundJobGate;
sealed class CatalogVerifyState : BackgroundJobGate;
sealed class CatalogVimmState : BackgroundJobGate;
sealed class CatalogImportState : BackgroundJobGate;
