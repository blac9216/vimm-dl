using Module.Core;

namespace Module.Download.Sources;

/// <summary>
/// Capability layered on top of <see cref="IDownloadSource"/> for sources where one queue item
/// maps to <em>several</em> files (e.g. a Wii U NUS title = TMD + ticket + cert + N <c>.app</c>/<c>.h3</c>).
/// <see cref="DownloadService"/> checks for this capability and, when present, drives the item through
/// a multi-file loop instead of the single-file path. Single-file sources do not implement it and are
/// completely unaffected.
/// </summary>
public interface IMultiFileSource : IDownloadSource
{
    /// <summary>
    /// Resolve a source-specific item id into the full set of files to download. Returns a failed
    /// <see cref="Result{T}"/> when the item can't be resolved.
    /// </summary>
    Task<Result<MultiFileResolution>> ResolveManyAsync(string sourceId, int format, HttpClient http, CancellationToken ct);

    /// <summary>
    /// A multi-file source is driven via <see cref="ResolveManyAsync"/>; the single-file entry point is
    /// never used by the engine for it. This default keeps such a source from having to implement a
    /// meaningless single-file resolve.
    /// </summary>
    Task<Result<ResolvedDownload>> IDownloadSource.ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
        => Task.FromResult(Result<ResolvedDownload>.Fail($"Source '{Id}' is multi-file; use ResolveManyAsync."));
}

/// <summary>
/// A multi-file source's answer for one queue item: the shared title/platform plus the concrete files to
/// fetch. All files land together in their own per-item subfolder under the console folder —
/// <c>completed/{console}/{subfolder}/</c> (the console folder is derived from <see cref="Platform"/>).
/// <see cref="SubFolder"/> names that subfolder (e.g. a Wii U title ID); when it is null/blank the loop
/// derives a stable one from <see cref="Title"/> + the queue id, so two no-SubFolder items never collide
/// on the shared console dir. The whole set produces a single queue completion and one post-download.
/// </summary>
public record MultiFileResolution(
    string Title,
    string? Platform,
    IReadOnlyList<ResolvedDownload> Files,
    string? SubFolder = null);
