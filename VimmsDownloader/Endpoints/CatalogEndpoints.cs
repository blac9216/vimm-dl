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
        app.MapGet("/api/catalog/status", async (CatalogRepository repo, CatalogSyncState state) =>
        {
            var systems = await repo.GetSystemsAsync();
            return new CatalogStatusResponse(state.IsSyncing, systems.Sum(s => s.GameCount), systems);
        });

        // Consoles with counts — for the Library filter.
        app.MapGet("/api/catalog/consoles", async (CatalogRepository repo) => await repo.GetConsolesAsync());

        // Paged game browse, filtered by console and/or name.
        app.MapGet("/api/catalog/games", async (string? console, string? q, int? page, int? pageSize,
            CatalogRepository repo) =>
        {
            var ps = Math.Clamp(pageSize ?? 100, 1, 200);
            var p = Math.Max(0, page ?? 0);
            var (total, games) = await repo.GetGamesAsync(console, q, p, ps);
            return new CatalogGamesResponse(total, p, ps, games);
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
