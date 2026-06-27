record AddRequest(List<string> Urls, int? Format = null, bool Force = false, string? Source = null);
record SourceInfo(string Id, string DisplayName, bool Catalog);
record AddResponse(List<QueueIdRow>? Queued, List<DuplicateInfo>? Duplicates);
record DuplicateInfo(string Url, string Source, string Reason, string? Title, string? Filename, string? IsoFilename,
    bool ArchiveExists, bool IsoExists, bool CrossFormat = false, int? ExistingFormat = null);
record QueuePatchRequest(string? Direction = null, int? Format = null);
record QueueReorderRequest(List<int> Ids);

record VersionResponse(string Current, string? Latest, bool HasUpdate, string? Url, string? Changelog);
record DataResponse(List<QueuedItem> Queued, List<HistoryItem> History,
    bool IsRunning, bool IsPaused, string? CurrentFile, string? CurrentUrl,
    string? Progress, long TotalBytes, long DownloadedBytes,
    List<ActiveDownloadDto> ActiveDownloads);
// Per-download state for N concurrent downloads (EPIC #113 / A1). The CurrentFile/CurrentUrl/Progress
// fields above remain as back-compat aliases of the first active download for one release.
record ActiveDownloadDto(string Key, string Url, string Source, string? Filename, string State,
    string? Progress, double Pct, double SpeedMBps, long Downloaded, long Total);
record TraceStep(string Name, string Status, string? Message, long? DurationMs = null);
record PipelineTrace(string PipelineType, List<TraceStep> Steps, string? IsoFilename, long? IsoSize, List<string> Actions);

record HistoryItem(int Id, string Url, string Filename, string? Filepath,
    string? Title, string? Platform, string? Size,
    bool FileExists, long? FileSize,
    PipelineTrace? Trace,
    string? CompletedAt, int? Format, long? GameId);
record QueueListResponse(List<QueueIdRow> Queued);
record QueueIdRow(int Id, string Url, int Format);
record QueuedItem(int Id, string Url, int Format, string? Title, string? Platform, string? Size, string? Formats);
record CompletedItem(int Id, string Url, string Filename, string? Filepath,
    string? CompletedAt = null, string? Title = null, string? Platform = null, string? Size = null,
    string? ConvPhase = null, string? ConvMessage = null, string? IsoFilename = null, int? Format = null,
    long? GameId = null);
record MetaResponse(string Title, string Platform, string Size, string? Formats, string? Serial);
record FormatOption(int Value, string Label, string Title, string Size);
record PartialFile(string Name, long Bytes, double Mb);
record LogEntry(string Time, string Type, string Message);
record CompletedEvent(string Url, string Filename, string Filepath);

record SettingsResponse(string Platform, string OsDescription, string Hostname, string User,
    string Ipv4, string DefaultPath, string ActivePath,
    bool FixThe, bool AddSerial, bool StripRegion, int Ps3Parallelism,
    int Ps3DefaultFormat, bool Ps3PreserveArchive,
    bool FeatureSync, bool FeatureEvents, bool FeatureLibrary, bool FeatureImport,
    string CatalogDatSource,
    int ArchiveParallelism, int ArchiveRetries, int ArchiveIdle,
    string ArchiveS3Access, string ArchiveS3Secret,
    string ImportPath, string RejectedPath);

// Catalog (No-Intro / Redump canonical game list)
record CatalogSystemStatus(string DatName, string Console, string Source,
    string? DatVersion, int GameCount, string? SyncedAt);
record CatalogStatusResponse(bool Syncing, bool Scanning, bool CompatSyncing, bool Verifying, bool VimmSyncing, bool Importing, int TotalGames, List<CatalogSystemStatus> Systems);
record CatalogConsole(string Console, int GameCount, int OwnedCount);
record CatalogGameDto(int Id, string Name, string Console, string? Region, string? Serial, string? Languages, long Size, bool Owned, string? Compat, bool? Verified, string? VimmMatch,
    List<int> AvailableFormats, List<int> OwnedFormats, List<string> OwnedSources, List<string> Origins);
record CatalogGamesResponse(int Total, int Page, int PageSize, List<CatalogGameDto> Games);
record CatalogSetDto(int Id, string Name, string Console, List<string> Links);
record AddSetRequest(string Name, string Console, List<string> Links);
record CatalogQueueResponse(string Url, string Source);
// E3b batch queue: resolve + queue many catalog games at once, reporting each id's outcome
// ("queued" | "duplicate" | "unavailable" | "unknown").
record CatalogQueueBatchRequest(List<int> Ids, int? Format);
record CatalogQueueBatchResponse(int Queued, int Skipped, int Failed, List<CatalogQueueResultDto> Results);
record CatalogQueueResultDto(int Id, string Status, string? Source);
// A game's Vimm download options (for the format picker), or null when not bound.
record CatalogVimmDto(long VaultId, List<CatalogVimmFormatDto> Formats);
record CatalogVimmFormatDto(int Alt, string Label, long SizeBytes, string? SizeText);

record MetricsResponse(long DiskFreeBytes, long DiskTotalBytes,
    long QueuedTotalBytes, int QueuedCount,
    long CompletedTotalBytes, int CompletedCount,
    long OrphanedTotalBytes, int OrphanedCount,
    long DownloadingTotalBytes, int DownloadingCount);
record SettingRequest(string Key, string Value);
record CheckPathResponse(string? Path, bool Exists, bool Writable, long? FreeSpace, string? Error);

record QueueExportItem(string Url, int Format);
record QueueImportResponse(int Added, int Skipped);

record Ps3ConvertRequest(string? Filename = null);
record Ps3ConvertResponse(int Queued, int Skipped, List<string> Files);
record Ps3ActionRequest(string Filename, string Action);
record Ps3ActionResponse(bool Success);

record SyncCompareRequest(string Path);
record SyncCopyRequest(string? Filename = null);

record EventRow(int Id, string ItemName, string EventType, string? Phase, string? Message, string? Data, string Timestamp, string? CorrelationId, long? GameId = null, int? Format = null);
record EventsResponse(List<EventRow> Events, int Total);

static class QueueLock
{
    public static readonly object Sync = new();
}

static class PathHelpers
{
    public static bool IsArchive(string filename)
        => Module.Core.FileExtensions.IsArchive(filename);

    public static string? ExpandPath(string? p)
    {
        p = p?.Trim();
        if (string.IsNullOrEmpty(p)) return p;
        if (p.StartsWith("~/"))
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
        return p;
    }
}
