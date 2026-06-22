namespace Module.Download;

/// <summary>
/// One in-flight download's live state. Owned (mutated) by the worker driving it and snapshotted for
/// the API. This is the per-download model that replaces <see cref="DownloadService"/>'s former
/// singleton current-download fields, so N concurrent downloads can be represented (EPIC #113 / A1).
/// At one worker (today's default) there is at most one of these, so behavior is unchanged.
/// </summary>
public sealed class ActiveDownload
{
    /// <summary>Stable per-download key (the queue item id) — survives filename resolution and retries.</summary>
    public required string Key { get; init; }
    public required string Url { get; init; }
    public required string Source { get; init; }
    public string? Filename { get; set; }
    /// <summary>starting | downloading | done | error</summary>
    public string State { get; set; } = "starting";
    public string? Progress { get; set; }
    public double Pct { get; set; } = -1;
    public double SpeedMBps { get; set; }
    public long Downloaded { get; set; }
    public long Total { get; set; }
}
