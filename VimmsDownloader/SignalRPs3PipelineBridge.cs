using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Module.Core.Pipeline;
using Module.Ps3Pipeline.Bridge;

class SignalRPs3PipelineBridge(IHubContext<DownloadHub> hub, QueueRepository repo) : IPs3PipelineBridge
{
    public async Task SendAsync(PipelineStatusEvent evt)
    {
        // Resolve the catalog identity once (best-effort): used both to stamp the persisted event (so a
        // game's conversion history groups across formats/retries — Phase C / C2) and to enrich the live
        // payload below (so the Active panel can group conversions by game — #151 / A). Null for legacy /
        // unmatched items, which fall back to filename grouping.
        long? gameId = null;
        int? format = null;
        string? source = null;
        try { (gameId, format, source) = await repo.ResolveEventIdentityAsync(evt.ItemName); }
        catch { }

        // 1. Append ALL events to event log (with correlation ID + identity)
        try
        {
            var data = evt.OutputFilename != null
                ? $"{{\"outputFilename\":\"{EscapeJson(evt.OutputFilename)}\"}}"
                : null;
            await repo.AppendEventAsync(evt.ItemName, "pipeline_status", evt.Phase, evt.Message, data, evt.CorrelationId,
                gameId, format, source);
        }
        catch { }

        // 2. Update projection (terminal states only)
        if (PipelinePhase.IsTerminal(evt.Phase))
        {
            try { await repo.SaveConversionStateAsync(evt.ItemName, evt.Phase, evt.Message, evt.OutputFilename); }
            catch { }
        }

        // 3. SignalR broadcast — carry the identity on the live payload (ItemName stays the display/abort key).
        try
        {
            var live = evt with { GameId = gameId, Format = format };
            var json = JsonSerializer.SerializeToElement(live, AppJsonContext.Default.PipelineStatusEvent);
            await hub.Clients.All.SendAsync("ConvertStatus", json);
            await hub.Clients.All.SendAsync("Status", $"[PS3] {evt.ItemName}: {evt.Message}");
        }
        catch { }
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
