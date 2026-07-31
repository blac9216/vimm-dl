using Module.Catalog;

/// <summary>
/// Fetches each registered emulator's compatibility list (<see cref="CompatSources.All"/>) and stores
/// it (normalized match key → status) under the emulator's match kind, so catalog games can show a
/// per-emulator compatibility badge. Adding an emulator is an adapter in Module.Catalog, not a change
/// here. One source failing (network/parse) is logged and skipped so the rest still ingest.
/// </summary>
class CompatSyncService(CatalogRepository catalog, IHttpClientFactory httpFactory, ILogger<CompatSyncService> log)
{
    /// <summary>
    /// <paramref name="report"/>, when given, is called once per emulator source (message = emulator id,
    /// current = 1-based source index, total = source count) — the L1 Jobs API progress checkpoint.
    /// </summary>
    public async Task<int> SyncAsync(Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("compat");
        var sources = CompatSources.All;
        var total = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources[i];
            var emulator = Emulators.ById(source.EmulatorId);
            if (emulator is null)
            {
                log.LogWarning("Compat: source {Id} has no registered emulator — skipping", source.EmulatorId);
                continue;
            }
            report?.Invoke(emulator.Id, i + 1, sources.Count);
            try
            {
                var entries = await source.LoadAsync((url, c) => http.GetStringAsync(url, c), ct);
                // Name-keyed emulators carry the consoles they target so the title join is console-gated;
                // serial-keyed ones don't need it (serials are globally unique).
                var consoles = emulator.MatchKind == CompatMatchKind.Name ? string.Join(',', emulator.Consoles) : null;
                await catalog.ReplaceCompatAsync(emulator.Id, Emulators.Token(emulator.MatchKind), entries, ct, consoles);
                log.LogInformation("Compat: {Emulator} → {Count} entries", emulator.Id, entries.Count);
                total += entries.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Compat: {Emulator} sync failed — skipping", emulator.Id);
            }
        }
        return total;
    }

    public Task<int> SyncAsync(CancellationToken ct) => SyncAsync(null, ct);
}
