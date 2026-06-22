using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Module.Core.Pipeline;
using Module.Ps3Pipeline.Bridge;

class SignalRPs3PipelineBridge(IHubContext<DownloadHub> hub, QueueRepository repo) : IPs3PipelineBridge
{
    public async Task SendAsync(PipelineStatusEvent evt)
    {
        // 1. Append ALL events to event log (with correlation ID)
        try
        {
            var data = evt.OutputFilename != null
                ? $"{{\"outputFilename\":\"{EscapeJson(evt.OutputFilename)}\"}}"
                : null;
            // Stamp the event with the catalog identity (Phase C / C2) so a game's conversion history
            // groups across formats and retries; nulls for legacy / unmatched items (filename grouping).
            var (gameId, format, source) = await repo.ResolveEventIdentityAsync(evt.ItemName);
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

        // 3. SignalR broadcast
        try
        {
            var json = JsonSerializer.SerializeToElement(evt, AppJsonContext.Default.PipelineStatusEvent);
            await hub.Clients.All.SendAsync("ConvertStatus", json);
            await hub.Clients.All.SendAsync("Status", $"[PS3] {evt.ItemName}: {evt.Message}");
        }
        catch { }
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
