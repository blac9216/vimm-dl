using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Module.Core;
using Module.Download.Bridge;
using Module.Download.Sources;

namespace Module.Download;

public class DownloadService
{
    private readonly IDownloadBridge _bridge;
    private readonly ILogger<DownloadService> _log;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ISourceRegistry _sources;
    private CancellationTokenSource? _cts;

    // Per-download state (EPIC #113 / A1): the set of in-flight downloads, keyed by queue item id.
    // At one worker there is at most one entry, so the back-compat aliases below behave as before.
    private readonly ConcurrentDictionary<string, ActiveDownload> _active = new();

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }

    /// <summary>Snapshot of all in-flight downloads, each with independent progress.</summary>
    public IReadOnlyList<ActiveDownload> ActiveDownloads => _active.Values.ToList();

    // Back-compat aliases of the first active download (retained one release while callers move to
    // ActiveDownloads). Null/0 when idle, matching the former singleton fields.
    private ActiveDownload? First => _active.Values.FirstOrDefault();
    public string? CurrentFile => First is { } d ? d.Filename ?? d.Url : null;
    public string? CurrentUrl => First?.Url;
    public string? CurrentProgress => First?.Progress;
    public long TotalBytes => First?.Total ?? 0;
    public long DownloadedBytes => First?.Downloaded ?? 0;
    public string? ActiveDownloadPath { get; private set; }

    private string _downloadPath = "";

    // Source-aware concurrency (EPIC #113 / A3): archive.org items run up to N at once; everything else
    // (Vimm) stays strictly serial. Applied on each Start, so changing the setting takes effect for the
    // next run. A 429 from any archive transfer parks all archive workers via a shared cooldown.
    private int _archiveParallelism = 1;
    private readonly object _cooldownLock = new();
    private DateTime _archiveCooldownUntil = DateTime.MinValue;
    /// <summary>How long a 429 parks archive workers. Internal so tests can shorten it.</summary>
    internal TimeSpan ArchiveCooldownDuration { get; set; } = TimeSpan.FromSeconds(60);

    // Archive resilience (EPIC #113 / A4): a failed archive transfer is retried up to N times (resuming
    // from the partial), and a transfer making no byte progress for the idle window is cancelled and
    // retried. Both default off (0) so non-production/serial flows are unchanged; the host wires the
    // archive_retries / archive_idle settings in.
    private int _archiveRetries;
    private int _archiveIdleSeconds;
    /// <summary>Backoff before retry attempt N (0-based). Internal so tests can zero it out.</summary>
    internal Func<int, TimeSpan> ArchiveRetryBackoff { get; set; } =
        attempt => TimeSpan.FromSeconds(Math.Min(30, (attempt + 1) * 5));

    public DownloadService(IDownloadBridge bridge, ILogger<DownloadService> log,
        IHttpClientFactory httpFactory, ISourceRegistry sources)
    {
        _bridge = bridge;
        _log = log;
        _httpFactory = httpFactory;
        _sources = sources;
    }

    public void Configure(string downloadPath, int archiveParallelism = 1, int archiveRetries = 0, int archiveIdleSeconds = 0)
    {
        _downloadPath = downloadPath;
        _archiveParallelism = Math.Max(1, archiveParallelism);
        _archiveRetries = Math.Max(0, archiveRetries);
        _archiveIdleSeconds = Math.Max(0, archiveIdleSeconds);
    }

    public string GetBasePath()
    {
        if (!string.IsNullOrEmpty(ActiveDownloadPath))
        {
            var trimmed = ActiveDownloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.EndsWith("downloading") ? Path.GetDirectoryName(ActiveDownloadPath)! : ActiveDownloadPath;
        }
        return _downloadPath;
    }

    public void Stop() { IsPaused = false; _cts?.Cancel(); }
    public void Pause() { IsPaused = true; _cts?.Cancel(); }

    public void Start(IDownloadItemProvider provider, string? overridePath = null)
    {
        if (IsRunning) return;
        _ = Task.Run(() => Run(provider, overridePath));
    }

    /// <summary>
    /// Callback invoked after a download completes. The host uses this to trigger
    /// PS3 pipeline processing or other post-download actions.
    /// </summary>
    public Func<string, string, string, int, Task>? OnPostDownload { get; set; }

    private Task Emit(DownloadEvent evt) => _bridge.SendAsync(evt);

    private async Task Run(IDownloadItemProvider provider, string? overridePath)
    {
        IsRunning = true;
        IsPaused = false;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var rand = new Random();
        var downloadPath = !string.IsNullOrWhiteSpace(overridePath) ? overridePath : _downloadPath;

        var downloadingPath = Path.Combine(downloadPath, "downloading");
        var completedPath = Path.Combine(downloadPath, "completed");
        Directory.CreateDirectory(downloadingPath);
        Directory.CreateDirectory(completedPath);
        ActiveDownloadPath = downloadingPath;
        await Emit(new DownloadStatusEvent($"Download path: {downloadPath}"));

        // Single dispatcher claims items in queue order. Archive items fan out to background workers
        // bounded by an archive semaphore (up to N concurrent); non-archive (Vimm) items are processed
        // inline so they stay strictly serial. Claiming excludes in-flight ids so no row runs twice.
        // (SemaphoreSlim is intentionally not disposed — background workers may still release it as the
        // run unwinds, and we never touch its wait handle.)
        var archiveSlots = new SemaphoreSlim(_archiveParallelism);
        var archiveTasks = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var claim = await ClaimNextAsync(provider);
                if (claim is null)
                {
                    archiveTasks.RemoveAll(t => t.IsCompleted);
                    if (archiveTasks.Count == 0) break;   // queue drained and no archive worker running
                    await Task.WhenAny(archiveTasks);      // wait for a worker, then re-check the queue
                    continue;
                }

                var (item, active) = claim.Value;
                await Emit(new DownloadStatusEvent($"Processing: {item.Url}"));

                if (string.Equals(item.Source, "archive", StringComparison.OrdinalIgnoreCase))
                {
                    await archiveSlots.WaitAsync(ct);
                    archiveTasks.Add(Task.Run(async () =>
                    {
                        try { await ProcessItemAsync(item, active, provider, downloadingPath, completedPath, rand, ct); }
                        catch (OperationCanceledException) { }
                        catch (Exception ex) { _log.LogError(ex, "Archive worker failed for {Url}", item.Url); }
                        finally { _active.TryRemove(active.Key, out _); archiveSlots.Release(); }
                    }, CancellationToken.None));
                }
                else
                {
                    // Serial source (Vimm / unknown): process inline. OperationCanceledException
                    // propagates to stop/pause the run; the per-item finally removes the ActiveDownload.
                    await ProcessItemAsync(item, active, provider, downloadingPath, completedPath, rand, ct);
                }
            }

            await DrainAsync(archiveTasks);   // drain in-flight archive workers before signalling done
            await Emit(new DownloadDoneEvent());
        }
        catch (OperationCanceledException)
        {
            await DrainAsync(archiveTasks);   // let cancelled archive workers unwind cleanly
            if (IsPaused)
                await Emit(new DownloadStatusEvent("Downloads paused. Resume to continue."));
            else
                await Emit(new DownloadStatusEvent("Downloads stopped."));
        }
        catch (Exception ex)
        {
            await DrainAsync(archiveTasks);
            _log.LogError(ex, "Queue processing failed");
            await Emit(new DownloadErrorEvent($"Queue failed: {ex.Message}"));
        }
        finally
        {
            IsRunning = false;
            // Per-item finallys already removed each ActiveDownload; clear defensively. The Current*
            // aliases derive from this set, so they read null/0 once it's empty.
            _active.Clear();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static async Task DrainAsync(List<Task> tasks)
    {
        try { await Task.WhenAll(tasks); } catch { /* individual worker faults are logged in-task */ }
    }

    /// <summary>
    /// Claim the next queue item not already in flight, registering its <see cref="ActiveDownload"/> so
    /// concurrent workers (and the next claim) skip it. The dispatcher is single-threaded, so claiming
    /// needs no extra lock. Returns null when nothing is left to claim.
    /// </summary>
    private async Task<(DownloadItem Item, ActiveDownload Active)?> ClaimNextAsync(IDownloadItemProvider provider)
    {
        var exclude = new HashSet<int>();
        foreach (var key in _active.Keys)
            if (int.TryParse(key, out var i)) exclude.Add(i);

        var item = await provider.GetNextAsync(exclude);
        if (item == null) return null;

        var active = new ActiveDownload { Key = item.Id.ToString(), Url = item.Url, Source = item.Source, Progress = "starting" };
        _active[active.Key] = active;
        return (item, active);
    }

    /// <summary>
    /// Resolve and stream one queue item, reporting progress on its <see cref="ActiveDownload"/>. Removes
    /// the ActiveDownload when done. Archive items honour the shared 429 cooldown and skip the per-download
    /// politeness delay (parallel by design); serial sources keep the inter-download pacing.
    /// <see cref="OperationCanceledException"/> is allowed to propagate so the caller can stop/pause.
    /// </summary>
    private async Task ProcessItemAsync(DownloadItem item, ActiveDownload active, IDownloadItemProvider provider,
        string downloadingPath, string completedPath, Random rand, CancellationToken ct)
    {
        var (id, url, format) = item;
        var isArchive = string.Equals(item.Source, "archive", StringComparison.OrdinalIgnoreCase);
        try
        {
            var source = _sources.Get(item.Source);
            if (source == null)
            {
                await Emit(new DownloadErrorEvent($"Unknown download source '{item.Source}' for {url}"));
                await provider.RemoveAsync(id);
                return;
            }

            if (isArchive) await WaitArchiveCooldownAsync(ct);

            var http = _httpFactory.CreateClient(source.HttpClientName);

            // Multi-file sources (e.g. Wii U NUS: TMD + ticket + cert + N content files) resolve into a
            // set of files for one queue item. Diverted here before the single-file path, which is left
            // untouched. The finally below still removes the ActiveDownload after this returns.
            if (source is IMultiFileSource multiSource)
            {
                await ProcessMultiFileAsync(multiSource, item, active, provider, http, downloadingPath, completedPath, ct);
                return;
            }

            // The source ("where the bytes come from") resolves the item into a concrete, streamable
            // download. Everything below is source-agnostic.
            var resolveResult = await source.ResolveAsync(url, format, http, ct);
            if (!resolveResult.IsOk)
            {
                await Emit(new DownloadErrorEvent($"{resolveResult.Error}"));
                await provider.RemoveAsync(id);
                return;
            }
            var resolved = resolveResult.Value!;

            if (resolved.FormatNote != null)
                await Emit(new DownloadStatusEvent($"Format fallback: {resolved.FormatNote}"));

            var formatLabel = resolved.ResolvedFormat == 0 ? "JB Folder" : $".dec.iso (format {resolved.ResolvedFormat})";
            await Emit(new DownloadStatusEvent($"Download URL: {resolved.DownloadUrl}"));
            await Emit(new DownloadStatusEvent($"Downloading: {resolved.Title} [{formatLabel}] (source={source.Id})"));

            // Sort completed files into an EmuDeck-style per-console folder (e.g. completed/ps3/).
            // Unknown platforms stay in completed/.
            var consoleDir = ConsoleDirectories.Resolve(resolved.Platform);
            var itemCompletedPath = consoleDir != null ? Path.Combine(completedPath, consoleDir) : completedPath;
            if (consoleDir != null)
            {
                Directory.CreateDirectory(itemCompletedPath);
                await Emit(new DownloadStatusEvent($"Console folder: {consoleDir} ({resolved.Platform})"));
            }

            active.State = "downloading";
            var result = isArchive
                ? await DownloadArchiveWithRetriesAsync(active, http, resolved, downloadingPath, itemCompletedPath, ct)
                : await StreamDownload(active, http, resolved.DownloadUrl, resolved.RequestHeaders,
                    resolved.SuggestedFilename, resolved.Title, downloadingPath, itemCompletedPath, ct);

            if (!result.IsOk)
            {
                active.State = "error";
                _log.LogError("Download failed for {Url}: {Error}", url, result.Error);
                await Emit(new DownloadErrorEvent($"Failed: {url} - {result.Error}"));
                if (isArchive)
                    // The archive retry budget is already exhausted (DownloadArchiveWithRetriesAsync);
                    // drop the item so it isn't re-claimed into an endless retry loop.
                    await provider.RemoveAsync(id);
                else
                {
                    var backoff = rand.Next(15, 46);
                    await Emit(new DownloadStatusEvent($"Waiting {backoff}s before retry..."));
                    await Task.Delay(backoff * 1000, ct);
                }
                return;
            }

            var (filename, completedFilePath) = result.Value;
            active.State = "done";

            await provider.CompleteAsync(id, url, filename, completedFilePath, format);
            await Emit(new DownloadCompletedEvent(url, filename, completedFilePath));
            _log.LogInformation("Downloaded {Filename} -> completed/", filename);

            if (OnPostDownload != null)
                await OnPostDownload(url, filename, completedFilePath, format);

            // Serial sources keep the polite inter-download delay; archive runs back-to-back (parallel).
            if (!isArchive)
            {
                var delay = rand.Next(5, 31);
                await Emit(new DownloadStatusEvent($"Waiting {delay}s before next download..."));
                await Task.Delay(delay * 1000, ct);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _log.LogWarning("Rate limited on {Url}, backing off", url);
            await Emit(new DownloadStatusEvent($"Rate limited: {url} - backing off before retry"));
            if (isArchive) TriggerArchiveCooldown(ArchiveCooldownDuration);
            else await Task.Delay(ArchiveCooldownDuration, ct);
        }
        finally { _active.TryRemove(active.Key, out _); }
    }

    /// <summary>
    /// Process a multi-file queue item: resolve the whole set, then stream each file (reusing
    /// <see cref="StreamDownload"/>, so per-file resume/progress works) into
    /// <c>completed/{console}/{SubFolder}/</c>. The set produces a single completion + one post-download
    /// callback, keeping queue identity coherent (one item → one completion). Files are fetched
    /// sequentially, which keeps the source polite by construction. <see cref="OperationCanceledException"/>
    /// propagates so stop/pause works mid-set.
    /// </summary>
    private async Task ProcessMultiFileAsync(IMultiFileSource source, DownloadItem item, ActiveDownload active,
        IDownloadItemProvider provider, HttpClient http, string downloadingPath, string completedPath, CancellationToken ct)
    {
        var (id, url, format) = item;

        var resolveResult = await source.ResolveManyAsync(url, format, http, ct);
        if (!resolveResult.IsOk)
        {
            await Emit(new DownloadErrorEvent($"{resolveResult.Error}"));
            await provider.RemoveAsync(id);
            return;
        }
        var set = resolveResult.Value!;
        if (set.Files.Count == 0)
        {
            await Emit(new DownloadErrorEvent($"No files resolved for {url}"));
            await provider.RemoveAsync(id);
            return;
        }

        // Land the set in completed/{console}/{SubFolder}/ — console from the platform, SubFolder an
        // optional per-item folder (e.g. a Wii U title ID). Unknown platform → completed/ root.
        var consoleDir = ConsoleDirectories.Resolve(set.Platform);
        var itemCompletedPath = consoleDir != null ? Path.Combine(completedPath, consoleDir) : completedPath;
        if (!string.IsNullOrWhiteSpace(set.SubFolder))
            itemCompletedPath = Path.Combine(itemCompletedPath, SanitizeFolder(set.SubFolder));
        Directory.CreateDirectory(itemCompletedPath);

        await Emit(new DownloadStatusEvent(
            $"Downloading {set.Files.Count} file(s): {set.Title} -> {itemCompletedPath} (source={source.Id})"));

        active.State = "downloading";
        for (var i = 0; i < set.Files.Count; i++)
        {
            var file = set.Files[i];
            await Emit(new DownloadStatusEvent(
                $"File {i + 1}/{set.Files.Count}: {file.SuggestedFilename ?? file.DownloadUrl}"));

            var fileResult = await StreamDownload(active, http, file.DownloadUrl, file.RequestHeaders,
                file.SuggestedFilename, file.Title, downloadingPath, itemCompletedPath, ct);
            if (!fileResult.IsOk)
            {
                active.State = "error";
                _log.LogError("Multi-file download failed for {Url} ({File}): {Error}",
                    url, file.SuggestedFilename ?? file.DownloadUrl, fileResult.Error);
                await Emit(new DownloadErrorEvent($"Failed: {url} - {fileResult.Error}"));
                await provider.RemoveAsync(id);   // drop the item; a partial set isn't auto-retried
                return;
            }
        }

        active.State = "done";

        // One completion for the whole set: the folder is the "filepath", its name the "filename".
        var setName = !string.IsNullOrWhiteSpace(set.SubFolder) ? set.SubFolder! : set.Title;
        await provider.CompleteAsync(id, url, setName, itemCompletedPath, format);
        await Emit(new DownloadCompletedEvent(url, setName, itemCompletedPath));
        _log.LogInformation("Downloaded {Count} file(s) for {Name} -> completed/", set.Files.Count, setName);

        if (OnPostDownload != null)
            await OnPostDownload(url, setName, itemCompletedPath, format);
    }

    /// <summary>Strip path-invalid characters from a per-item subfolder name (defensive; title IDs are hex).</summary>
    private static string SanitizeFolder(string name)
    {
        var cleaned = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim().Trim('.').Trim();
        return string.IsNullOrEmpty(cleaned) ? "set" : cleaned;
    }

    /// <summary>
    /// Stream an archive transfer with resilience (EPIC #113 / A4): retry up to <c>archive_retries</c>
    /// times (resuming from the partial via the existing Range support), and abort+retry a transfer that
    /// makes no byte progress for <c>archive_idle</c> seconds. Stop/pause (the outer token) is never
    /// retried. Returns the final result; the caller emits the error if every attempt failed.
    /// </summary>
    private async Task<Result<(string Filename, string CompletedPath)>> DownloadArchiveWithRetriesAsync(
        ActiveDownload active, HttpClient http, ResolvedDownload resolved,
        string downloadingPath, string completedPath, CancellationToken ct)
    {
        var idle = _archiveIdleSeconds > 0 ? TimeSpan.FromSeconds(_archiveIdleSeconds) : Timeout.InfiniteTimeSpan;
        var result = Result<(string, string)>.Fail("not attempted");

        for (var attempt = 0; ; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watchdog = _archiveIdleSeconds > 0 ? StallWatchdogAsync(active, idle, attemptCts) : Task.CompletedTask;
            try
            {
                result = await StreamDownload(active, http, resolved.DownloadUrl, resolved.RequestHeaders,
                    resolved.SuggestedFilename, resolved.Title, downloadingPath, completedPath, attemptCts.Token);
            }
            catch (OperationCanceledException) when (attemptCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Watchdog tripped — a stall, not a real stop/pause. Treat it as a failed attempt.
                result = Result<(string, string)>.Fail($"stalled (no progress for {_archiveIdleSeconds}s)");
            }
            finally
            {
                if (!attemptCts.IsCancellationRequested) attemptCts.Cancel();
                await watchdog;
            }

            if (result.IsOk || attempt >= _archiveRetries) return result;

            var backoff = ArchiveRetryBackoff(attempt);
            await Emit(new DownloadStatusEvent(
                $"Retry {attempt + 1}/{_archiveRetries} for {active.Filename ?? active.Url} in {backoff.TotalSeconds:F0}s ({result.Error})"));
            await Task.Delay(backoff, ct);
        }
    }

    /// <summary>
    /// Cancel <paramref name="attemptCts"/> once a transfer goes an entire idle window with no new bytes.
    /// Polls the download's byte counter every window; resolves quietly when the attempt ends.
    /// </summary>
    private static async Task StallWatchdogAsync(ActiveDownload active, TimeSpan idle, CancellationTokenSource attemptCts)
    {
        try
        {
            var last = active.Downloaded;
            while (true)
            {
                await Task.Delay(idle, attemptCts.Token);
                var now = active.Downloaded;
                if (now == last) { attemptCts.Cancel(); return; }
                last = now;
            }
        }
        catch (OperationCanceledException) { /* attempt finished or was cancelled */ }
    }

    /// <summary>Park all archive workers until any active 429 cooldown elapses (EPIC #113 / A3).</summary>
    private async Task WaitArchiveCooldownAsync(CancellationToken ct)
    {
        TimeSpan wait;
        lock (_cooldownLock) wait = _archiveCooldownUntil - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Emit(new DownloadStatusEvent($"Archive rate-limited — waiting {wait.TotalSeconds:F0}s"));
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>Start (or extend) a shared cooldown that all archive workers observe before fetching.</summary>
    private void TriggerArchiveCooldown(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        lock (_cooldownLock)
            if (until > _archiveCooldownUntil) _archiveCooldownUntil = until;
    }

    private async Task<Result<(string Filename, string CompletedPath)>> StreamDownload(
        ActiveDownload active, HttpClient http, string downloadUrl, IReadOnlyList<(string Name, string Value)>? extraHeaders,
        string? suggestedFilename, string gameTitle, string downloadingPath, string completedPath, CancellationToken ct)
    {
        // Apply any source-specific request headers (e.g. Vimm's Referer / Sec-Fetch-Site).
        static void ApplyHeaders(HttpRequestMessage req, IReadOnlyList<(string Name, string Value)>? headers)
        {
            if (headers == null) return;
            foreach (var (name, value) in headers)
                req.Headers.TryAddWithoutValidation(name, value);
        }

        var headRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        ApplyHeaders(headRequest, extraHeaders);
        using var headResponse = await http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!headResponse.IsSuccessStatusCode)
            return Result<(string, string)>.Fail($"HTTP {(int)headResponse.StatusCode} {headResponse.ReasonPhrase}");

        var filename = headResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? headResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? (string.IsNullOrEmpty(suggestedFilename) ? $"{gameTitle}.zip" : suggestedFilename);
        filename = string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));

        active.Filename = filename;
        var filePath = Path.Combine(downloadingPath, filename);
        var completedFilePath = Path.Combine(completedPath, filename);
        var totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
        active.Total = totalBytes;

        long existingBytes = 0;
        if (File.Exists(filePath))
            existingBytes = new FileInfo(filePath).Length;

        // File already fully downloaded (app killed before move)
        if (existingBytes > 0 && totalBytes > 0 && existingBytes >= totalBytes)
        {
            headResponse.Dispose();
            await Emit(new DownloadStatusEvent($"Already downloaded: {filename}, moving to completed"));

            var moveResult = FileOps.TryMove(filePath, completedFilePath);
            return moveResult.IsOk
                ? Result<(string, string)>.Ok((filename, completedFilePath))
                : Result<(string, string)>.Fail($"Failed to move file: {moveResult.Error}");
        }

        HttpResponseMessage response;
        bool resumed = false;

        if (existingBytes > 0 && existingBytes < totalBytes)
        {
            headResponse.Dispose();
            var rangeRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            ApplyHeaders(rangeRequest, extraHeaders);
            rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
            response = await http.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                resumed = true;
                await Emit(new DownloadStatusEvent($"Resuming {filename} from {existingBytes / 1048576.0:F2} MB"));
            }
            else
            {
                response.Dispose();
                existingBytes = 0;
                var freshRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                ApplyHeaders(freshRequest, extraHeaders);
                response = await http.SendAsync(freshRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                    return Result<(string, string)>.Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        else
        {
            existingBytes = 0;
            response = headResponse;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(filePath,
            resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = existingBytes;
        int bytesRead;
        var lastReport = DateTime.UtcNow;
        long lastReportBytes = existingBytes;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;
            active.Downloaded = downloaded;

            var now = DateTime.UtcNow;
            var elapsed = (now - lastReport).TotalSeconds;
            if (elapsed >= 2)
            {
                var pct = totalBytes > 0 ? (double)downloaded * 100 / totalBytes : -1;
                var mb = downloaded / 1048576.0;
                var totalMb = totalBytes > 0 ? totalBytes / 1048576.0 : 0;
                var speedMBps = (downloaded - lastReportBytes) / 1048576.0 / elapsed;
                var progressMsg = totalBytes > 0
                    ? $"{filename}: {mb:F2} / {totalMb:F2} MB ({pct:F2}%) [{speedMBps:F2} MB/s]"
                    : $"{filename}: {mb:F2} MB downloaded [{speedMBps:F2} MB/s]";
                active.Progress = progressMsg;
                active.Pct = pct;
                active.SpeedMBps = speedMBps;
                await Emit(new DownloadProgressEvent(filename, progressMsg, pct, speedMBps, downloaded, totalBytes, active.Key));
                lastReport = now;
                lastReportBytes = downloaded;
            }
        }

        await fileStream.DisposeAsync();
        await contentStream.DisposeAsync();
        if (!resumed) response.Dispose();

        // Move to completed
        var finalMove = FileOps.TryMove(filePath, completedFilePath);
        return finalMove.IsOk
            ? Result<(string, string)>.Ok((filename, completedFilePath))
            : Result<(string, string)>.Fail($"Failed to move to completed: {finalMove.Error}");
    }
}
