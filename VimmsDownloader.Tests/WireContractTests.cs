using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Module.Sync;

namespace VimmsDownloader.Tests;

/// <summary>
/// Guards the C# &lt;-&gt; TypeScript wire contract (#283). Bug #280 shipped because
/// <c>SettingsResponse.WiiUCommonKey</c> serialized as <c>wiiUCommonKey</c> under the
/// <see cref="AppJsonContext"/> camelCase policy while <c>types/api.ts</c> declared
/// <c>wiiuCommonKey</c> — and nothing caught the drift: the client has no test framework, and no
/// backend test asserted wire names.
///
/// Each test here serializes a fully-populated response record through the REAL
/// <see cref="AppJsonContext"/> and asserts the exact set of top-level JSON property names against a
/// checked-in expected list. The expected list is written to be easy to diff, field-by-field, against
/// the matching interface in <c>VimmsDownloader/client/src/types/api.ts</c> — if a property is renamed,
/// added, or removed on the C# side (or the naming policy changes how it serializes), the mismatched
/// test fails immediately instead of silently drifting from the frontend type.
///
/// This test is intentionally test-only: it does not change production serialization or
/// <c>types/api.ts</c>. Where an existing mismatch between the wire and <c>types/api.ts</c> was found
/// during authoring, the assertion is pinned to the actual wire (C#) names — see the comment on
/// <see cref="SyncCompareResponse_WireNames_MatchExpected"/> — and is called out in the PR description
/// for a human to decide whether to fix it.
/// </summary>
[TestClass]
public class WireContractTests
{
    /// <summary>
    /// Serializes <paramref name="instance"/> through the source-generated <paramref name="typeInfo"/>
    /// and asserts the resulting top-level JSON property names are exactly <paramref name="expected"/>
    /// (order-independent — JSON property order is not a contract). On mismatch, the message reports
    /// what's missing/extra so the diff against <c>types/api.ts</c> is immediate.
    /// </summary>
    private static void AssertWireNames<T>(JsonTypeInfo<T> typeInfo, T instance, params string[] expected)
    {
        var json = JsonSerializer.Serialize(instance, typeInfo);
        using var doc = JsonDocument.Parse(json);
        var actual = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expectedSorted = expected.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(expectedSorted, actual,
            $"{typeof(T).Name} wire property names changed. Actual: [{string.Join(", ", actual)}]. " +
            $"Expected: [{string.Join(", ", expectedSorted)}]. If this is intentional, update both this " +
            "test AND VimmsDownloader/client/src/types/api.ts to match.");
    }

    // ---- The record at the center of #280/#282 ------------------------------------------------

    [TestMethod]
    public void SettingsResponse_WireNames_MatchExpected()
    {
        var settings = new SettingsResponse(
            Platform: "linux", OsDescription: "Ubuntu", Hostname: "host", User: "user",
            Ipv4: "127.0.0.1", DefaultPath: "/data", ActivePath: "/data",
            FixThe: true, AddSerial: true, StripRegion: true, Ps3Parallelism: 3,
            Ps3DefaultFormat: 1, Ps3PreserveArchive: true,
            FeatureSync: false, FeatureEvents: false, FeatureImport: false,
            CatalogDatSource: "libretro",
            ArchiveParallelism: 4, ArchiveRetries: 3, ArchiveIdle: 60,
            ArchiveS3Access: "", ArchiveS3Secret: "",
            ImportPath: "/import", RejectedPath: "/rejected",
            WiiUCommonKey: "deadbeef",
            IgdbClientId: "", IgdbClientSecret: "",
            RaApiKey: "");

        // Matches the SettingsResponse interface in VimmsDownloader/client/src/types/api.ts.
        AssertWireNames(AppJsonContext.Default.SettingsResponse, settings,
            "platform", "osDescription", "hostname", "user",
            "ipv4", "defaultPath", "activePath",
            "fixThe", "addSerial", "stripRegion", "ps3Parallelism",
            "ps3DefaultFormat", "ps3PreserveArchive",
            "featureSync", "featureEvents", "featureImport",
            "catalogDatSource",
            "archiveParallelism", "archiveRetries", "archiveIdle",
            "archiveS3Access", "archiveS3Secret",
            "importPath", "rejectedPath",
            "wiiUCommonKey",
            "igdbClientId", "igdbClientSecret",
            "raApiKey");
    }

    // ---- Other response records mirrored by types/api.ts --------------------------------------

    [TestMethod]
    public void DataResponse_WireNames_MatchExpected()
    {
        var active = new ActiveDownloadDto("k", "http://u", "vimm", "f.7z", "downloading", "50%", 50.0, 1.5, 100, 200);
        var queued = new QueuedItem(1, "http://u", 0, "Title", "PS3", "1 GB", "0,1");
        var trace = new PipelineTrace("ps3", [new TraceStep("extract", "done", null, 100)], "Game.iso", 123, ["retry"]);
        var history = new HistoryItem(1, "http://u", "f.7z", "/completed/f.7z", "Title", "PS3", "1 GB",
            true, 100, trace, "2026-01-01", 1, 42);

        var data = new DataResponse([queued], [history], true, false, "f.7z", "http://u", "50%", 200, 100, [active]);

        AssertWireNames(AppJsonContext.Default.DataResponse, data,
            "queued", "history", "isRunning", "isPaused", "currentFile", "currentUrl",
            "progress", "totalBytes", "downloadedBytes", "activeDownloads");
    }

    [TestMethod]
    public void ActiveDownloadDto_WireNames_MatchExpected()
    {
        var active = new ActiveDownloadDto("k", "http://u", "vimm", "f.7z", "downloading", "50%", 50.0, 1.5, 100, 200);

        // Matches the ActiveDownload interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.ActiveDownloadDto, active,
            "key", "url", "source", "filename", "state", "progress", "pct", "speedMBps", "downloaded", "total");
    }

    [TestMethod]
    public void QueuedItem_WireNames_MatchExpected()
    {
        var item = new QueuedItem(1, "http://u", 0, "Title", "PS3", "1 GB", "0,1");

        AssertWireNames(AppJsonContext.Default.QueuedItem, item,
            "id", "url", "format", "title", "platform", "size", "formats");
    }

    [TestMethod]
    public void HistoryItem_WireNames_MatchExpected()
    {
        var trace = new PipelineTrace("ps3", [new TraceStep("extract", "done", null, 100)], "Game.iso", 123, ["retry"]);
        var history = new HistoryItem(1, "http://u", "f.7z", "/completed/f.7z", "Title", "PS3", "1 GB",
            true, 100, trace, "2026-01-01", 1, 42);

        AssertWireNames(AppJsonContext.Default.HistoryItem, history,
            "id", "url", "filename", "filepath", "title", "platform", "size",
            "fileExists", "fileSize", "trace", "completedAt", "format", "gameId");
    }

    [TestMethod]
    public void PipelineTrace_WireNames_MatchExpected()
    {
        var trace = new PipelineTrace("ps3", [new TraceStep("extract", "done", null, 100)], "Game.iso", 123, ["retry"]);

        AssertWireNames(AppJsonContext.Default.PipelineTrace, trace,
            "pipelineType", "steps", "isoFilename", "isoSize", "actions");
    }

    [TestMethod]
    public void TraceStep_WireNames_MatchExpected()
    {
        var step = new TraceStep("extract", "done", "message", 100);

        AssertWireNames(AppJsonContext.Default.TraceStep, step,
            "name", "status", "message", "durationMs");
    }

    [TestMethod]
    public void MetaResponse_WireNames_MatchExpected()
    {
        var meta = new MetaResponse("Title", "PS3", "1 GB", "0,1", "BLES-00043");

        AssertWireNames(AppJsonContext.Default.MetaResponse, meta,
            "title", "platform", "size", "formats", "serial");
    }

    [TestMethod]
    public void FormatOption_WireNames_MatchExpected()
    {
        var option = new FormatOption(0, "Label", "Title", "1 GB");

        AssertWireNames(AppJsonContext.Default.FormatOption, option,
            "value", "label", "title", "size");
    }

    [TestMethod]
    public void CatalogConsole_WireNames_MatchExpected()
    {
        var console = new CatalogConsole("PS3", 100, 10, "PlayStation 3");

        AssertWireNames(AppJsonContext.Default.CatalogConsole, console,
            "console", "gameCount", "ownedCount", "displayName");
    }

    [TestMethod]
    public void CompatStatus_WireNames_MatchExpected()
    {
        var compat = new CompatStatus("rpcs3", "Playable");

        AssertWireNames(AppJsonContext.Default.CompatStatus, compat,
            "emulator", "status");
    }

    [TestMethod]
    public void EmulatorDto_WireNames_MatchExpected()
    {
        var emulator = new EmulatorDto("rpcs3", "RPCS3", "PS3", "serial");

        // Matches the Emulator interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.EmulatorDto, emulator,
            "id", "name", "console", "matchKind");
    }

    [TestMethod]
    public void CatalogGameDto_WireNames_MatchExpected()
    {
        var game = new CatalogGameDto(1, "Name", "PS3", "USA", "BLES-00043", "En", 1000, true,
            [new CompatStatus("rpcs3", "Playable")], true, "sha1",
            [0, 1], [0], ["vimm"], ["libretro"], 0.9);

        // Matches the CatalogGame interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.CatalogGameDto, game,
            "id", "name", "console", "region", "serial", "languages", "size", "owned",
            "compat", "verified", "vimmMatch", "availableFormats", "ownedFormats", "ownedSources",
            "origins", "rankScore");
    }

    [TestMethod]
    public void CatalogGamesResponse_WireNames_MatchExpected()
    {
        var game = new CatalogGameDto(1, "Name", "PS3", "USA", "BLES-00043", "En", 1000, true,
            [new CompatStatus("rpcs3", "Playable")], true, "sha1",
            [0, 1], [0], ["vimm"], ["libretro"], 0.9);
        var response = new CatalogGamesResponse(1, 1, 100, [game]);

        AssertWireNames(AppJsonContext.Default.CatalogGamesResponse, response,
            "total", "page", "pageSize", "games");
    }

    [TestMethod]
    public void CatalogSetDto_WireNames_MatchExpected()
    {
        var set = new CatalogSetDto(1, "Set", "PS3", ["http://a"]);

        // Matches the CatalogSet interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.CatalogSetDto, set,
            "id", "name", "console", "links");
    }

    [TestMethod]
    public void CatalogQueueBatchResponse_WireNames_MatchExpected()
    {
        var response = new CatalogQueueBatchResponse(1, 0, 0, [new CatalogQueueResultDto(1, "queued", "archive")]);

        AssertWireNames(AppJsonContext.Default.CatalogQueueBatchResponse, response,
            "queued", "skipped", "failed", "results");
    }

    [TestMethod]
    public void CatalogQueueResultDto_WireNames_MatchExpected()
    {
        var result = new CatalogQueueResultDto(1, "queued", "archive");

        // Matches the CatalogQueueResult interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.CatalogQueueResultDto, result,
            "id", "status", "source");
    }

    [TestMethod]
    public void CatalogCurateResponse_WireNames_MatchExpected()
    {
        var response = new CatalogCurateResponse([1, 2], 2, 1000, 2000);

        AssertWireNames(AppJsonContext.Default.CatalogCurateResponse, response,
            "ids", "count", "totalBytes", "budgetBytes");
    }

    [TestMethod]
    public void CatalogVimmDto_WireNames_MatchExpected()
    {
        var vimm = new CatalogVimmDto(1001, [new CatalogVimmFormatDto(0, "Label", 1000, "1 GB")]);

        // Matches the CatalogVimm interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.CatalogVimmDto, vimm,
            "vaultId", "formats");
    }

    [TestMethod]
    public void CatalogGameDescription_WireNames_MatchExpected()
    {
        var description = new CatalogGameDescription("A game.");

        AssertWireNames(AppJsonContext.Default.CatalogGameDescription, description,
            "description");
    }

    [TestMethod]
    public void CatalogSystemStatus_WireNames_MatchExpected()
    {
        var status = new CatalogSystemStatus("PlayStation 3.dat", "PS3", "No-Intro", "20260101", 100, "2026-01-01");

        AssertWireNames(AppJsonContext.Default.CatalogSystemStatus, status,
            "datName", "console", "source", "datVersion", "gameCount", "syncedAt");
    }

    [TestMethod]
    public void CatalogStatusResponse_WireNames_MatchExpected()
    {
        var system = new CatalogSystemStatus("PlayStation 3.dat", "PS3", "No-Intro", "20260101", 100, "2026-01-01");
        var response = new CatalogStatusResponse(false, false, false, false, false, false, false, false, 100, [system]);

        // Matches the CatalogStatus interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.CatalogStatusResponse, response,
            "syncing", "scanning", "compatSyncing", "verifying", "vimmSyncing", "importing",
            "igdbSyncing", "raSyncing", "totalGames", "systems");
    }

    [TestMethod]
    public void JobStatusDto_WireNames_MatchExpected()
    {
        var job = new JobStatusDto("catalog-sync", true, "34 of 95", 34, 95, 0.36, DateTimeOffset.UtcNow, 5000);

        // Matches the JobStatus interface in types/api.ts.
        AssertWireNames(AppJsonContext.Default.JobStatusDto, job,
            "kind", "running", "message", "current", "total", "percent", "startedAt", "elapsedMs");
    }

    [TestMethod]
    public void VersionResponse_WireNames_MatchExpected()
    {
        var version = new VersionResponse("1.0.0", "1.1.0", true, "http://u", "Changelog");

        AssertWireNames(AppJsonContext.Default.VersionResponse, version,
            "current", "latest", "hasUpdate", "url", "changelog");
    }

    [TestMethod]
    public void QueueExportItem_WireNames_MatchExpected()
    {
        var item = new QueueExportItem("http://u", 0);

        AssertWireNames(AppJsonContext.Default.QueueExportItem, item,
            "url", "format");
    }

    [TestMethod]
    public void QueueImportResponse_WireNames_MatchExpected()
    {
        var response = new QueueImportResponse(5, 1);

        AssertWireNames(AppJsonContext.Default.QueueImportResponse, response,
            "added", "skipped");
    }

    [TestMethod]
    public void CheckPathResponse_WireNames_MatchExpected()
    {
        var response = new CheckPathResponse("/data", true, true, 1000, null);

        AssertWireNames(AppJsonContext.Default.CheckPathResponse, response,
            "path", "exists", "writable", "freeSpace", "error");
    }

    [TestMethod]
    public void Ps3ConvertResponse_WireNames_MatchExpected()
    {
        var response = new Ps3ConvertResponse(1, 0, ["Game.7z"]);

        AssertWireNames(AppJsonContext.Default.Ps3ConvertResponse, response,
            "queued", "skipped", "files");
    }

    [TestMethod]
    public void MetricsResponse_WireNames_MatchExpected()
    {
        var response = new MetricsResponse(1000, 2000, 300, 3, 400, 4, 500, 5, 600, 6);

        AssertWireNames(AppJsonContext.Default.MetricsResponse, response,
            "diskFreeBytes", "diskTotalBytes", "queuedTotalBytes", "queuedCount",
            "completedTotalBytes", "completedCount", "orphanedTotalBytes", "orphanedCount",
            "downloadingTotalBytes", "downloadingCount");
    }

    [TestMethod]
    public void EventRow_WireNames_MatchExpected()
    {
        var row = new EventRow(1, "Game.7z", "download", "downloading", "50%", "{}", "2026-01-01T00:00:00Z",
            "abcdef123456", 42, 1);

        AssertWireNames(AppJsonContext.Default.EventRow, row,
            "id", "itemName", "eventType", "phase", "message", "data", "timestamp",
            "correlationId", "gameId", "format");
    }

    [TestMethod]
    public void EventsResponse_WireNames_MatchExpected()
    {
        var row = new EventRow(1, "Game.7z", "download", "downloading", "50%", "{}", "2026-01-01T00:00:00Z",
            "abcdef123456", 42, 1);
        var response = new EventsResponse([row], 1);

        AssertWireNames(AppJsonContext.Default.EventsResponse, response,
            "events", "total");
    }

    // ---- Module.Sync records --------------------------------------------------------------------

    /// <summary>
    /// FINDING (not fixed here, see PR description): the C# response carries two fields —
    /// <c>syncPath</c> and <c>pathExists</c> — that the <c>SyncCompareResponse</c> interface in
    /// <c>types/api.ts</c> does not declare at all (it only has new/synced/targetOnly/source/target/
    /// error). This isn't a casing drift like #280, but the same class of client/server contract gap
    /// this issue is meant to catch. Per scope, the test is pinned to the actual C# wire (the source of
    /// truth) rather than "fixed" on either side.
    /// </summary>
    [TestMethod]
    public void SyncCompareResponse_WireNames_MatchExpected()
    {
        var response = new SyncCompareResponse("/sync", true,
            [new SyncFileInfo("a.iso", 100)], [new SyncFileInfo("b.iso", 200)], [new SyncFileInfo("c.iso", 300)],
            new SyncDiskInfo("Source", 10, 1000, 2000, 3000),
            new SyncDiskInfo("Target", 5, 500, 1000, 1500),
            null);

        AssertWireNames(AppJsonContext.Default.SyncCompareResponse, response,
            "syncPath", "pathExists", "new", "synced", "targetOnly", "source", "target", "error");
    }

    [TestMethod]
    public void SyncDiskInfo_WireNames_MatchExpected()
    {
        var info = new SyncDiskInfo("Label", 10, 1000, 2000, 3000);

        AssertWireNames(AppJsonContext.Default.SyncDiskInfo, info,
            "label", "isoCount", "isoTotalSize", "freeSpace", "totalSpace");
    }

    [TestMethod]
    public void SyncFileInfo_WireNames_MatchExpected()
    {
        var info = new SyncFileInfo("a.iso", 100);

        AssertWireNames(AppJsonContext.Default.SyncFileInfo, info,
            "name", "size");
    }
}
