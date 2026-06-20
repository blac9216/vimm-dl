using Module.Catalog;

/// <summary>
/// Fetches the RPCS3 compatibility export and stores it (normalized serial → status) so PS3
/// catalog games can show a compatibility badge. Other emulators will follow the same shape.
/// </summary>
class CompatSyncService(CatalogRepository catalog, IHttpClientFactory httpFactory, ILogger<CompatSyncService> log)
{
    private const string Rpcs3Export = "https://rpcs3.net/compatibility?api=v1&export";

    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient("rpcs3");
        var json = await http.GetStringAsync(Rpcs3Export, ct);
        var entries = RpcsCompat.Parse(json).ToList();
        await catalog.ReplaceCompatAsync("rpcs3", entries, ct);
        log.LogInformation("Compat: rpcs3 → {Count} entries", entries.Count);
        return entries.Count;
    }
}
