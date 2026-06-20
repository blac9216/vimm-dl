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

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentFile { get; private set; }
    public string? CurrentUrl { get; private set; }
    public string? CurrentProgress { get; private set; }
    public long TotalBytes { get; private set; }
    public long DownloadedBytes { get; private set; }
    public string? ActiveDownloadPath { get; private set; }

    private string _downloadPath = "";

    public DownloadService(IDownloadBridge bridge, ILogger<DownloadService> log,
        IHttpClientFactory httpFactory, ISourceRegistry sources)
    {
        _bridge = bridge;
        _log = log;
        _httpFactory = httpFactory;
        _sources = sources;
    }

    public void Configure(string downloadPath)
    {
        _downloadPath = downloadPath;
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

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var item = await provider.GetNextAsync();
                if (item == null) break;

                var (id, url, format) = item;
                CurrentFile = url;
                CurrentUrl = url;
                CurrentProgress = "starting";
                await Emit(new DownloadStatusEvent($"Processing: {url}"));

                try
                {
                    var source = _sources.Get(item.Source);
                    if (source == null)
                    {
                        await Emit(new DownloadErrorEvent($"Unknown download source '{item.Source}' for {url}"));
                        await provider.RemoveAsync(id);
                        continue;
                    }

                    var http = _httpFactory.CreateClient(source.HttpClientName);

                    // The source ("where the bytes come from") resolves the item into a
                    // concrete, streamable download. Everything below is source-agnostic.
                    var resolveResult = await source.ResolveAsync(url, format, http, ct);
                    if (!resolveResult.IsOk)
                    {
                        await Emit(new DownloadErrorEvent($"{resolveResult.Error}"));
                        await provider.RemoveAsync(id);
                        continue;
                    }
                    var resolved = resolveResult.Value!;

                    if (resolved.FormatNote != null)
                        await Emit(new DownloadStatusEvent($"Format fallback: {resolved.FormatNote}"));

                    var formatLabel = resolved.ResolvedFormat == 0 ? "JB Folder" : $".dec.iso (format {resolved.ResolvedFormat})";
                    await Emit(new DownloadStatusEvent($"Download URL: {resolved.DownloadUrl}"));
                    await Emit(new DownloadStatusEvent($"Downloading: {resolved.Title} [{formatLabel}] (source={source.Id})"));

                    // Sort completed files into an EmuDeck-style per-console folder
                    // (e.g. completed/ps3/). Unknown platforms stay in completed/.
                    var consoleDir = ConsoleDirectories.Resolve(resolved.Platform);
                    var itemCompletedPath = consoleDir != null
                        ? Path.Combine(completedPath, consoleDir)
                        : completedPath;
                    if (consoleDir != null)
                    {
                        Directory.CreateDirectory(itemCompletedPath);
                        await Emit(new DownloadStatusEvent($"Console folder: {consoleDir} ({resolved.Platform})"));
                    }

                    var result = await StreamDownload(
                        http, resolved.DownloadUrl, resolved.RequestHeaders, resolved.Title,
                        downloadingPath, itemCompletedPath, ct);

                    if (!result.IsOk)
                    {
                        _log.LogError("Download failed for {Url}: {Error}", url, result.Error);
                        await Emit(new DownloadErrorEvent($"Failed: {url} - {result.Error}"));
                        var backoff = rand.Next(15, 46);
                        await Emit(new DownloadStatusEvent($"Waiting {backoff}s before retry..."));
                        await Task.Delay(backoff * 1000, ct);
                        continue;
                    }

                    var (filename, completedFilePath) = result.Value;

                    await provider.CompleteAsync(id, url, filename, completedFilePath, format);
                    await Emit(new DownloadCompletedEvent(url, filename, completedFilePath));
                    _log.LogInformation("Downloaded {Filename} -> completed/", filename);

                    if (OnPostDownload != null)
                        await OnPostDownload(url, filename, completedFilePath, format);

                    var delay = rand.Next(5, 31);
                    await Emit(new DownloadStatusEvent($"Waiting {delay}s before next download..."));
                    await Task.Delay(delay * 1000, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _log.LogWarning("Rate limited on {Url}, backing off 60s", url);
                    await Emit(new DownloadStatusEvent($"Rate limited: {url} - waiting 60s before retry"));
                    await Task.Delay(60_000, ct);
                }
            }

            await Emit(new DownloadDoneEvent());
        }
        catch (OperationCanceledException)
        {
            if (IsPaused)
                await Emit(new DownloadStatusEvent("Downloads paused. Resume to continue."));
            else
                await Emit(new DownloadStatusEvent("Downloads stopped."));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Queue processing failed");
            await Emit(new DownloadErrorEvent($"Queue failed: {ex.Message}"));
        }
        finally
        {
            IsRunning = false;
            if (!IsPaused)
            {
                CurrentFile = null;
                CurrentUrl = null;
                CurrentProgress = null;
                TotalBytes = 0;
                DownloadedBytes = 0;
            }
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<Result<(string Filename, string CompletedPath)>> StreamDownload(
        HttpClient http, string downloadUrl, IReadOnlyList<(string Name, string Value)>? extraHeaders,
        string gameTitle, string downloadingPath, string completedPath, CancellationToken ct)
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
            ?? $"{gameTitle}.zip";
        filename = string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));

        CurrentFile = filename;
        var filePath = Path.Combine(downloadingPath, filename);
        var completedFilePath = Path.Combine(completedPath, filename);
        var totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
        TotalBytes = totalBytes;

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
            DownloadedBytes = downloaded;

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
                CurrentProgress = progressMsg;
                await Emit(new DownloadProgressEvent(filename, progressMsg, pct, speedMBps, downloaded, totalBytes));
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
