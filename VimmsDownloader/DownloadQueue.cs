using Module.Core.Pipeline;
using Module.Download;
using Module.Ps3Pipeline;
using Module.Ps3IsoTools;
using Module.WiiUPipeline;

/// <summary>
/// Thin host-level wrapper that wires DownloadService to the repo, the PS3 + Wii U pipelines, and settings.
/// </summary>
class DownloadQueue
{
    private readonly DownloadService _service;
    private readonly QueueRepository _repo;
    private readonly QueueItemProvider _provider;
    private readonly Ps3ConversionPipeline _ps3Pipeline;
    private readonly WiiUConversionPipeline _wiiuPipeline;

    public DownloadQueue(DownloadService service, QueueRepository repo,
        Ps3ConversionPipeline ps3Pipeline, WiiUConversionPipeline wiiuPipeline)
    {
        _service = service;
        _repo = repo;
        _provider = new QueueItemProvider(repo);
        _ps3Pipeline = ps3Pipeline;
        _wiiuPipeline = wiiuPipeline;

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
        if (Module.Core.Platforms.IsWiiU(platform)) return _wiiuPipeline;
        return null;
    }

    public async Task StartAsync(string? overridePath)
    {
        // Read the archive tuning fresh each start, so changing a setting takes effect for the next run
        // (EPIC #113 / A3+A4). Defaults: 4 concurrent (IA rate-limits above ~4–5), 3 retries, 60s stall.
        var parallelism = int.TryParse(await _repo.GetSettingAsync(SettingsKeys.ArchiveParallelism), out var p) ? p : 4;
        var retries = int.TryParse(await _repo.GetSettingAsync(SettingsKeys.ArchiveRetries), out var r) ? r : 3;
        var idle = int.TryParse(await _repo.GetSettingAsync(SettingsKeys.ArchiveIdle), out var i) ? i : 60;
        _service.Configure(_repo.GetDownloadPath(), parallelism, retries, idle);
        _service.Start(_provider, overridePath);
    }

    private async Task HandlePostDownload(string url, string filename, string completedFilePath, int format)
    {
        var dlMeta = await _repo.GetMetaAsync(url);

        // Wii U: the NUS source lands a multi-file WUP set in completed/wiiu/{TitleID}/ (so completedFilePath
        // is that folder). Metadata may be absent, so fall back to inferring the platform from the folder.
        var platform = dlMeta?.Platform;
        if (platform == null && IsUnderConsoleDir(completedFilePath, "wiiu"))
            platform = Module.Core.Platforms.WiiU;
        if (Module.Core.Platforms.IsWiiU(platform))
        {
            _wiiuPipeline.Enqueue(completedFilePath);
            return;
        }

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

    /// <summary>True when <paramref name="path"/>'s immediate parent folder is the given console dir
    /// (e.g. a Wii U title folder completed/wiiu/{TitleID} sits directly under "wiiu").</summary>
    private static bool IsUnderConsoleDir(string path, string consoleDir)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetFileName(Path.GetDirectoryName(trimmed));
        return string.Equals(parent, consoleDir, StringComparison.OrdinalIgnoreCase);
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
