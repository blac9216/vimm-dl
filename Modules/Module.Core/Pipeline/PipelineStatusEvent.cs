namespace Module.Core.Pipeline;

/// <summary>
/// Generic status event for any pipeline item. Used by all console pipelines.
/// The Phase field carries both universal phases (queued/done/error) and
/// console-specific sub-phases (extracting/converting/etc).
///
/// <para><see cref="GameId"/> / <see cref="Format"/> carry the catalog identity on the live payload so the
/// Active panel can group conversions of one game across formats (#151 / A). The pipeline leaves them
/// null; the bridge stamps them. <see cref="ItemName"/> stays the display + abort key.</para>
/// </summary>
public record PipelineStatusEvent(
    string ItemName,
    string Phase,
    string Message,
    string? OutputFilename = null,
    string? CorrelationId = null,
    long? GameId = null,
    int? Format = null
);
