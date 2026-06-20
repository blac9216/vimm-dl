using Module.Catalog;

static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // Kick off a background sync of all configured systems (tens of MB across ~18 DATs),
        // so it never blocks the request thread. 409 if one is already running.
        app.MapPost("/api/catalog/sync", (CatalogSyncService sync, CatalogSyncState state,
            ILogger<CatalogSyncService> log) =>
        {
            if (!state.TryBegin()) return Results.Conflict("Catalog sync already in progress");
            _ = Task.Run(async () =>
            {
                try { await sync.SyncAsync(CatalogSystems.All, state.Token); }
                catch (Exception ex) { log.LogError(ex, "Catalog sync crashed"); }
                finally { state.End(); }
            });
            return Results.Accepted();
        });

        // Per-console counts + versions, plus whether a sync is currently running.
        app.MapGet("/api/catalog/status", async (CatalogRepository repo, CatalogSyncState sync, CatalogScanState scan, CatalogCompatState compat) =>
        {
            var systems = await repo.GetSystemsAsync();
            return new CatalogStatusResponse(sync.IsSyncing, scan.IsScanning, compat.IsRunning, systems.Sum(s => s.GameCount), systems);
        });

        // Sync emulator compatibility (RPCS3 export) in the background (single-flight).
        app.MapPost("/api/catalog/compat/sync", (CompatSyncService svc, CatalogCompatState state,
            ILogger<CompatSyncService> log) =>
        {
            if (!state.TryBegin()) return Results.Conflict("Compatibility sync already in progress");
            _ = Task.Run(async () =>
            {
                try { await svc.SyncAsync(state.Token); }
                catch (Exception ex) { log.LogError(ex, "Compat sync crashed"); }
                finally { state.End(); }
            });
            return Results.Accepted();
        });

        // Scan completed/ and record which catalog games are present on disk (background, single-flight).
        app.MapPost("/api/catalog/scan", (CatalogScanService scanner, CatalogScanState state,
            ILogger<CatalogScanService> log) =>
        {
            if (!state.TryBegin()) return Results.Conflict("Catalog scan already in progress");
            _ = Task.Run(async () =>
            {
                try { await scanner.ScanAsync(state.Token); }
                catch (Exception ex) { log.LogError(ex, "Catalog scan crashed"); }
                finally { state.End(); }
            });
            return Results.Accepted();
        });

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

        // --- download sets (per-console source locations) ---
        app.MapGet("/api/catalog/sets", async (CatalogRepository repo) => await repo.GetSetsAsync());

        app.MapPost("/api/catalog/sets", async (AddSetRequest req, CatalogRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(req.Console) || string.IsNullOrWhiteSpace(req.Identifier))
                return Results.BadRequest("console and identifier are required");
            var source = string.IsNullOrWhiteSpace(req.Source) ? "archive" : req.Source!.Trim();
            var id = await repo.AddSetAsync(req.Console.Trim(), source, req.Identifier.Trim(), req.Label);
            return Results.Ok(new CatalogSetDto((int)id, req.Console.Trim(), source, req.Identifier.Trim(), req.Label));
        });

        app.MapDelete("/api/catalog/sets/{id:int}", async (int id, CatalogRepository repo) =>
            await repo.DeleteSetAsync(id) ? Results.Ok() : Results.NotFound());

        // Resolve a catalog game via its console's sets and queue it for download.
        app.MapPost("/api/catalog/games/{id:int}/queue", async (int id, CatalogRepository repo,
            CatalogResolveService resolver, QueueRepository queue, DownloadQueue downloadQueue, CancellationToken ct) =>
        {
            var game = await repo.GetGameByIdAsync(id);
            if (game is null) return Results.NotFound("Unknown catalog game");

            var url = await resolver.ResolveAsync(game.Value.Console, game.Value.Name, ct);
            if (url is null) return Results.NotFound("No configured set provides this game");

            if ((await queue.CheckDuplicatesAsync([url])).Count > 0)
                return Results.Conflict("Already queued or completed");

            await queue.AddToQueueAsync(url, 0, "archive");
            if (!downloadQueue.IsRunning) await downloadQueue.StartAsync(null);
            return Results.Ok(new CatalogQueueResponse(url));
        });
    }
}

/// <summary>Single-flight guard + cancellation for the background catalog sync.</summary>
sealed class CatalogSyncState
{
    private int _running;
    private CancellationTokenSource _cts = new();

    public bool IsSyncing => Volatile.Read(ref _running) == 1;
    public CancellationToken Token => _cts.Token;

    public bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _cts = new CancellationTokenSource();
        return true;
    }

    public void End() => Volatile.Write(ref _running, 0);
}

/// <summary>Single-flight guard + cancellation for the background catalog scan (separate from sync).</summary>
sealed class CatalogScanState
{
    private int _running;
    private CancellationTokenSource _cts = new();

    public bool IsScanning => Volatile.Read(ref _running) == 1;
    public CancellationToken Token => _cts.Token;

    public bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _cts = new CancellationTokenSource();
        return true;
    }

    public void End() => Volatile.Write(ref _running, 0);
}

/// <summary>Single-flight guard + cancellation for the background emulator-compatibility sync.</summary>
sealed class CatalogCompatState
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
}
