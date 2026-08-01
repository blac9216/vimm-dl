namespace Module.Core.Pipeline;

/// <summary>
/// Generic status event for any pipeline item. Used by all console pipelines.
/// The Phase field carries both universal phases (queued/done/error) and
/// console-specific sub-phases (extracting/converting/etc).
///
/// <para><see cref="GameId"/> / <see cref="Format"/> carry the catalog identity on the live payload so the
/// Active panel can group conversions of one game across formats (#151 / A). The pipeline leaves them
/// null; the bridge stamps them. <see cref="ItemName"/> stays the display + abort key.</para>
///
/// <para><see cref="Platform"/> carries which pipeline emitted the event (e.g. <c>Module.Core.Platforms.PS3</c>
/// / <c>WiiU</c>), stamped by the host's console-specific bridge — the shared <c>extracting</c>/<c>queued</c>
/// phase strings otherwise can't tell a PS3 conversion apart from a Wii U one. The Jobs tab uses it to route
/// the stop button to the right pipeline (#274). Null for legacy/pre-#274 payloads.</para>
/// </summary>
public record PipelineStatusEvent(
    string ItemName,
    string Phase,
    string Message,
    string? OutputFilename = null,
    string? CorrelationId = null,
    long? GameId = null,
    int? Format = null,
    string? Platform = null
);
