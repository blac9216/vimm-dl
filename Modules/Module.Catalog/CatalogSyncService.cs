using Microsoft.Extensions.Logging;
using Module.Core;

namespace Module.Catalog;

/// <summary>
/// Orchestrates a catalog sync: for each configured system it pulls the clrmamepro DAT text from the
/// selected <see cref="IDatSource"/> (libretro mirror or daily bundle), parses it via
/// <see cref="ClrMameProParser"/>, and persists the games through <see cref="ICatalogStore"/>. One
/// system at a time; a failed system (fetch or parse error) is logged and skipped so a single failure
/// never aborts the run.
///
/// <para>Rate-safety lives in the source — this service only paces between systems by the source's
/// <see cref="IDatSource.InterSystemDelay"/> (zero for the bundle, which makes ~2 requests total).</para>
/// </summary>
public sealed class CatalogSyncService(ICatalogStore store, ILogger<CatalogSyncService> log)
{
    /// <summary>Delay seam — defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests substitute a no-op recorder.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Sync every system from <paramref name="source"/>, skipping any that fail. Returns a run summary.</summary>
    public async Task<CatalogSyncSummary> SyncAsync(IReadOnlyList<CatalogSystemInfo> systems, IDatSource source, CancellationToken ct = default)
    {
        int synced = 0, failed = 0, games = 0;
        for (int i = 0; i < systems.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            // Pace the run: pause before every system after the first so we don't burst a rate cap.
            if (i > 0 && source.InterSystemDelay > TimeSpan.Zero) await Delay(source.InterSystemDelay, ct);
            var sys = systems[i];
            var r = await SyncSystemAsync(sys, source, ct);
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

    /// <summary>Fetch (via <paramref name="source"/>), parse and persist a single system. Returns the game count or an error.</summary>
    public async Task<Result<int>> SyncSystemAsync(CatalogSystemInfo sys, IDatSource source, CancellationToken ct = default)
    {
        var fetch = await source.GetDatAsync(sys, ct);
        if (!fetch.IsOk) return Result<int>.Fail(fetch.Error!);   // Error is set whenever !IsOk
        var content = fetch.Value!;                               // Value is set whenever IsOk

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
