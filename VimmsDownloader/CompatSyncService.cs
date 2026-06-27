using Module.Catalog;

/// <summary>
/// Fetches each registered emulator's compatibility list (<see cref="CompatSources.All"/>) and stores
/// it (normalized match key → status) under the emulator's match kind, so catalog games can show a
/// per-emulator compatibility badge. Adding an emulator is an adapter in Module.Catalog, not a change
/// here. One source failing (network/parse) is logged and skipped so the rest still ingest.
/// </summary>
class CompatSyncService(CatalogRepository catalog, IHttpClientFactory httpFactory, ILogger<CompatSyncService> log)
{
    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient("compat");
        var total = 0;
        foreach (var source in CompatSources.All)
        {
            var emulator = Emulators.ById(source.EmulatorId);
            if (emulator is null)
            {
                log.LogWarning("Compat: source {Id} has no registered emulator — skipping", source.EmulatorId);
                continue;
            }
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
}
