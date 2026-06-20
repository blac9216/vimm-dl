using Microsoft.Extensions.Logging;
using Module.Core;

namespace Module.Catalog;

/// <summary>
/// Fetches the configured console DATs from the libretro-database mirror, parses them, and
/// persists each system's games via <see cref="ICatalogStore"/>. One system at a time; a failed
/// system (HTTP/parse error) is logged and skipped so a single failure never aborts the run.
/// </summary>
public sealed class CatalogSyncService(HttpClient http, ICatalogStore store, ILogger<CatalogSyncService> log)
{
    private const string BaseUrl = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/";

    /// <summary>Sync every system, skipping any that fail. Returns a run summary.</summary>
    public async Task<CatalogSyncSummary> SyncAsync(IReadOnlyList<CatalogSystemInfo> systems, CancellationToken ct = default)
    {
        int synced = 0, failed = 0, games = 0;
        foreach (var sys in systems)
        {
            ct.ThrowIfCancellationRequested();
            var r = await SyncSystemAsync(sys, ct);
            if (r.IsOk)
            {
                synced++;
                games += r.Value;
                log.LogInformation("Catalog: {System} → {Count} games", sys.DatName, r.Value);
            }
            else
            {
                failed++;
                log.LogWarning("Catalog: {System} failed — {Error}", sys.DatName, r.Error);
            }
        }
        log.LogInformation("Catalog sync done: {Synced} systems, {Games} games, {Failed} failed", synced, games, failed);
        return new CatalogSyncSummary(synced, failed, games);
    }

    /// <summary>Fetch, parse and persist a single system. Returns the game count or an error.</summary>
    public async Task<Result<int>> SyncSystemAsync(CatalogSystemInfo sys, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}{sys.Group}/{Uri.EscapeDataString(sys.DatName)}.dat";

        string content;
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return Result<int>.Fail($"HTTP {(int)resp.StatusCode} for {sys.DatName}");
            content = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Fail($"fetch failed: {ex.Message}");
        }

        try
        {
            var parser = new ClrMameProParser();
            var games = parser.Parse(new StringReader(content)).ToList();
            var systemId = await store.UpsertSystemAsync(sys.DatName, sys.Console, sys.Group, ct);
            await store.ReplaceSystemGamesAsync(systemId, games, parser.Header?.Version, ct);
            return Result<int>.Ok(games.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Fail($"persist failed: {ex.Message}");
        }
    }
}

/// <summary>Outcome of a catalog sync run.</summary>
public sealed record CatalogSyncSummary(int SystemsSynced, int SystemsFailed, int TotalGames);
