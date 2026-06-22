using Module.Core.Pipeline;
using Module.Download;
using Module.Ps3Pipeline;
using Module.Ps3IsoTools;

/// <summary>
/// Thin host-level wrapper that wires DownloadService to the repo, PS3 pipeline, and settings.
/// </summary>
class DownloadQueue
{
    private readonly DownloadService _service;
    private readonly QueueRepository _repo;
    private readonly QueueItemProvider _provider;
    private readonly Ps3ConversionPipeline _ps3Pipeline;

    public DownloadQueue(DownloadService service, QueueRepository repo, Ps3ConversionPipeline ps3Pipeline)
    {
        _service = service;
        _repo = repo;
        _provider = new QueueItemProvider(repo);
        _ps3Pipeline = ps3Pipeline;

        _service.OnPostDownload = HandlePostDownload;
    }

    // Delegate to service
    public bool IsRunning => _service.IsRunning;
    public bool IsPaused => _service.IsPaused;
    public string? CurrentFile => _service.CurrentFile;
    public string? CurrentUrl => _service.CurrentUrl;
    public string? CurrentProgress => _service.CurrentProgress;
    public long TotalBytes => _service.TotalBytes;
    public long DownloadedBytes => _service.DownloadedBytes;
    public IReadOnlyList<Module.Download.ActiveDownload> ActiveDownloads => _service.ActiveDownloads;

    public string GetBasePath() => _service.GetBasePath();
    public void Stop() => _service.Stop();
    public void Pause() => _service.Pause();

    public IPipeline? GetPipeline(string? platform)
    {
        if (Module.Core.Platforms.IsPS3(platform)) return _ps3Pipeline;
        return null;
    }

    public async Task StartAsync(string? overridePath)
    {
        // Read archive concurrency fresh each start, so changing the setting takes effect for the next
        // run (EPIC #113 / A3). Default 4 — Internet Archive rate-limits above ~4–5 concurrent.
        var parallelism = int.TryParse(await _repo.GetSettingAsync(SettingsKeys.ArchiveParallelism), out var p) ? p : 4;
        _service.Configure(_repo.GetDownloadPath(), parallelism);
        _service.Start(_provider, overridePath);
    }

    private async Task HandlePostDownload(string url, string filename, string completedFilePath, int format)
    {
        var dlMeta = await _repo.GetMetaAsync(url);
        if (dlMeta == null || !Module.Core.Platforms.IsPS3(dlMeta.Platform))
            return;

        var serial = dlMeta.Serial;
        var renameOpts = await LoadRenameOptionsAsync();
        var downloadPath = _service.GetBasePath();

        // The archive already landed in its per-console folder (e.g. completed/ps3/).
        // Keep conversion output (ISO / extracted files) alongside it in the same folder.
        var completedDir = Path.GetDirectoryName(completedFilePath) is { Length: > 0 } dir
            ? dir
            : Path.Combine(downloadPath, "completed");
        var tempBaseDir = Path.Combine(downloadPath, "ps3_temp");

        if (format == 0)
        {
            _ps3Pipeline.JbFolder.Enqueue(completedFilePath, completedDir, tempBaseDir);
        }
        else if (Module.Core.FileExtensions.IsDecIso(filename))
        {
            _ = Task.Run(() => _ps3Pipeline.DecIso.RenameDecIsoAsync(completedFilePath, serial, renameOpts));
        }
        else if (format > 0 && Module.Core.FileExtensions.IsArchive(filename))
        {
            var deleteArchive = !await IsPreserveArchiveAsync();
            _ = Task.Run(() => _ps3Pipeline.DecIso.ExtractAndRenameDecIsoAsync(
                completedFilePath, completedDir, tempBaseDir, serial, renameOpts, deleteArchive));
        }
    }

    private async Task<bool> IsPreserveArchiveAsync()
    {
        var val = await _repo.GetSettingAsync(SettingsKeys.Ps3PreserveArchive);
        return val != "false";
    }

    private async Task<IsoRenameOptions> LoadRenameOptionsAsync()
    {
        var s = await _repo.GetAllSettingsAsync();
        return new IsoRenameOptions(
            FixThe: s.GetValueOrDefault(SettingsKeys.FixThe, "true") == "true",
            AddSerial: s.GetValueOrDefault(SettingsKeys.AddSerial, "true") == "true",
            StripRegion: s.GetValueOrDefault(SettingsKeys.StripRegion, "true") == "true"
        );
    }
}
