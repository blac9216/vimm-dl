using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Module.Core.Pipeline;
using Module.WiiUPipeline.Bridge;

/// <summary>
/// Routes Wii U pipeline events to the event log, the completed_urls conversion-state projection, and
/// SignalR — mirroring <see cref="SignalRPs3PipelineBridge"/> so the existing Active/trace UI renders Wii U
/// conversions with no UI changes.
/// </summary>
class SignalRWiiUPipelineBridge(IHubContext<DownloadHub> hub, QueueRepository repo) : IWiiUPipelineBridge
{
    public async Task SendAsync(PipelineStatusEvent evt)
    {
        long? gameId = null;
        int? format = null;
        string? source = null;
        try { (gameId, format, source) = await repo.ResolveEventIdentityAsync(evt.ItemName); }
        catch { }

        // 1. Append all events to the log (with correlation id + catalog identity).
        try
        {
            var data = evt.OutputFilename != null
                ? $"{{\"outputFilename\":\"{EscapeJson(evt.OutputFilename)}\"}}"
                : null;
            await repo.AppendEventAsync(evt.ItemName, "pipeline_status", evt.Phase, evt.Message, data, evt.CorrelationId,
                gameId, format, source);
        }
        catch { }

        // 2. Update the projection on terminal states.
        if (PipelinePhase.IsTerminal(evt.Phase))
        {
            try { await repo.SaveConversionStateAsync(evt.ItemName, evt.Phase, evt.Message, evt.OutputFilename); }
            catch { }
        }

        // 3. Broadcast — same "ConvertStatus"/"Status" channels the PS3 trace already consumes.
        try
        {
            var live = evt with { GameId = gameId, Format = format };
            var json = JsonSerializer.SerializeToElement(live, AppJsonContext.Default.PipelineStatusEvent);
            await hub.Clients.All.SendAsync("ConvertStatus", json);
            await hub.Clients.All.SendAsync("Status", $"[Wii U] {evt.ItemName}: {evt.Message}");
        }
        catch { }
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
