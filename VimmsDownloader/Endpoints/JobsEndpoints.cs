/// <summary>
/// L1 #255 — the unified background-job surface: one row per registered <see cref="BackgroundJobGate"/>
/// with live progress/message/elapsed, plus a cancel path. Complements (does not replace)
/// <c>GET /api/catalog/status</c>, whose response shape stays frozen for the existing UI. PS3/Wii U
/// conversions are out of scope here — they stay on <c>ConvertStatus</c> + <c>/api/data</c> traces.
/// </summary>
static class JobsEndpoints
{
    public static void MapJobsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs", (CatalogSyncState sync, CatalogScanState scan, CatalogCompatState compat,
            CatalogVerifyState verify, CatalogVimmState vimm, CatalogVimmSeedState vimmSeed, CatalogImportState import,
            CatalogIgdbDescState igdbDesc, CatalogIgdbRankState igdbRank, CatalogRaState ra) =>
            Results.Ok(JobsOps.Snapshot(AllGates(sync, scan, compat, verify, vimm, vimmSeed, import, igdbDesc, igdbRank, ra))));

        // 204 when cancellation is signalled, 404 for an unknown kind, 409 when the job isn't running.
        app.MapPost("/api/jobs/{kind}/cancel", (string kind, CatalogSyncState sync, CatalogScanState scan,
            CatalogCompatState compat, CatalogVerifyState verify, CatalogVimmState vimm, CatalogVimmSeedState vimmSeed, CatalogImportState import,
            CatalogIgdbDescState igdbDesc, CatalogIgdbRankState igdbRank, CatalogRaState ra) =>
            JobsOps.Cancel(kind, AllGates(sync, scan, compat, verify, vimm, vimmSeed, import, igdbDesc, igdbRank, ra)));
    }

    private static BackgroundJobGate[] AllGates(CatalogSyncState sync, CatalogScanState scan,
        CatalogCompatState compat, CatalogVerifyState verify, CatalogVimmState vimm, CatalogVimmSeedState vimmSeed, CatalogImportState import,
        CatalogIgdbDescState igdbDesc, CatalogIgdbRankState igdbRank, CatalogRaState ra) =>
        [sync, scan, compat, verify, vimm, vimmSeed, import, igdbDesc, igdbRank, ra];
}

/// <summary>
/// The Jobs API logic, extracted from the endpoints so the status-code matrix (200/204/404/409) is
/// unit-testable without an HTTP host — mirroring <c>CatalogQueueOps</c>.
/// </summary>
static class JobsOps
{
    public static List<JobStatusDto> Snapshot(IReadOnlyList<BackgroundJobGate> gates)
        => gates.Select(g => g.Snapshot()).ToList();

    public static IResult Cancel(string kind, IReadOnlyList<BackgroundJobGate> gates)
    {
        var gate = gates.FirstOrDefault(g => string.Equals(g.Kind, kind, StringComparison.OrdinalIgnoreCase));
        if (gate is null) return Results.NotFound();
        if (!gate.IsRunning) return Results.Conflict($"{kind} is not running");
        gate.Cancel();
        return Results.NoContent();
    }
}
