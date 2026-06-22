using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core;
using Module.Core.Testing;
using Module.Download.Bridge;
using Module.Download.Sources;
using Module.Download.Tests.Helpers;

namespace Module.Download.Tests;

/// <summary>
/// EPIC #113 / A3: source-aware concurrency. Drives downloads through a gated HTTP handler that counts
/// how many transfers are in flight at once, to prove archive.org items run up to <c>archive_parallelism</c>
/// concurrently while Vimm (and unknown sources) stay strictly serial, plus the shared 429 cooldown.
/// </summary>
[TestClass]
public class DownloadConcurrencyTests
{
    private FakeDownloadBridge _bridge = null!;
    private TempDirectory _tmp = null!;
    private GateHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _tmp = new TempDirectory("DownloadConcurrencyTests");
        _bridge = new FakeDownloadBridge();
        _handler = new GateHandler(Encoding.ASCII.GetBytes("payload"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _handler.ReleaseAll();
        _tmp.Dispose();
    }

    private DownloadService NewService()
    {
        var registry = new SourceRegistry([new GateSource("archive"), new GateSource("vimm")]);
        var svc = new DownloadService(_bridge, NullLogger<DownloadService>.Instance,
            new SingleHandlerFactory(_handler), registry);
        return svc;
    }

    [TestMethod]
    public async Task ArchiveDownloads_RunConcurrently_UpToParallelism()
    {
        var svc = NewService();
        svc.Configure(_tmp.Root, archiveParallelism: 3);
        var provider = new MultiItemProvider(MakeItems(5, "archive"));
        svc.Start(provider);

        // Exactly 3 should enter the transfer at once (the slot cap); a 4th must not while those are held.
        await _handler.WaitForConcurrent(3);
        Assert.IsFalse(await _handler.TryWaitForConcurrent(4, 400), "archive concurrency must be capped at 3");

        _handler.ReleaseAll();
        await WaitForCompletedCount(provider, 5);
        Assert.AreEqual(3, _handler.MaxConcurrent);
        svc.Stop();
    }

    [TestMethod]
    public async Task VimmDownloads_StaySerial_RegardlessOfParallelism()
    {
        var svc = NewService();
        svc.Configure(_tmp.Root, archiveParallelism: 5); // high cap must not affect Vimm
        var provider = new MultiItemProvider(MakeItems(3, "vimm"));
        svc.Start(provider);

        // One Vimm transfer is held; a second must never start while three items sit queued. (Vimm
        // keeps a 5–30s inter-download delay, so we assert the cap during the held phase, not all 3.)
        await _handler.WaitForConcurrent(1);
        Assert.IsFalse(await _handler.TryWaitForConcurrent(2, 400), "Vimm must never run two transfers at once");
        Assert.AreEqual(1, _handler.MaxConcurrent);
        svc.Stop();
    }

    [TestMethod]
    public async Task Archive429_ParksArchiveWorkers_ViaSharedCooldown()
    {
        _handler.Throw429ForFirst = true; // the first transfer throws a 429
        var svc = NewService();
        svc.ArchiveCooldownDuration = TimeSpan.FromMilliseconds(300);
        svc.Configure(_tmp.Root, archiveParallelism: 1); // serial so the 429 lands before the next item
        var provider = new MultiItemProvider(MakeItems(2, "archive"));
        svc.Start(provider);

        // After the 429, the next archive item must observe the shared cooldown before fetching.
        await WaitForStatus("Archive rate-limited", 5000);
        _handler.ReleaseAll();
        await WaitForCompletedCount(provider, 1); // the second item eventually completes
        svc.Stop();
    }

    // --- helpers ---

    private static List<DownloadItem> MakeItems(int n, string source) =>
        Enumerable.Range(1, n).Select(i => new DownloadItem(i, $"http://{source}/{i}", 0) { Source = source }).ToList();

    private async Task WaitForCompletedCount(MultiItemProvider provider, int n, int timeout = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (provider.CompletedCount >= n) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Timed out waiting for {n} completions (got {provider.CompletedCount})");
    }

    private async Task WaitForStatus(string contains, int timeout)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (_bridge.AllEvents.OfType<DownloadStatusEvent>().Any(e => e.Message.Contains(contains))) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Timed out waiting for status containing '{contains}'");
    }
}

// --- test doubles ---

/// <summary>Counts concurrent transfers and blocks each until released, so concurrency is observable.</summary>
sealed class GateHandler(byte[] body) : HttpMessageHandler
{
    private int _current;
    private int _served;
    private readonly object _lock = new();
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int MaxConcurrent { get; private set; }
    public bool Throw429ForFirst { get; set; }

    public void ReleaseAll() => _release.TrySetResult();

    public async Task WaitForConcurrent(int n, int timeout = 5000)
    {
        if (!await TryWaitForConcurrent(n, timeout)) throw new TimeoutException($"never reached {n} concurrent");
    }

    public async Task<bool> TryWaitForConcurrent(int n, int timeout)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock) if (_current >= n) return true;
            await Task.Delay(10);
        }
        return false;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        bool throw429;
        lock (_lock) throw429 = Throw429ForFirst && _served++ == 0;
        if (throw429)
            throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);

        lock (_lock) { _current++; MaxConcurrent = Math.Max(MaxConcurrent, _current); }
        try { await _release.Task.WaitAsync(ct); }
        finally { lock (_lock) _current--; }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }
}

sealed class SingleHandlerFactory(GateHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

sealed class GateSource(string id) : IDownloadSource
{
    public string Id => id;
    public string DisplayName => id;
    public string HttpClientName => id;
    public Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
        => Task.FromResult(Result<ResolvedDownload>.Ok(
            new ResolvedDownload($"http://{id}/file", $"Title-{sourceId}", null, $"f-{Guid.NewGuid():N}.bin", null, 0, null)));
}

sealed class MultiItemProvider(List<DownloadItem> items) : IDownloadItemProvider
{
    private readonly object _lock = new();
    private readonly HashSet<int> _completed = [];
    private readonly HashSet<int> _removed = [];
    public int CompletedCount { get { lock (_lock) return _completed.Count; } }
    public int RemovedCount { get { lock (_lock) return _removed.Count; } }

    public Task<DownloadItem?> GetNextAsync(IReadOnlySet<int> excludeIds)
    {
        lock (_lock)
        {
            // Skip in-flight, completed, and removed (dropped) items so each runs at most once.
            var next = items.FirstOrDefault(i =>
                !excludeIds.Contains(i.Id) && !_completed.Contains(i.Id) && !_removed.Contains(i.Id));
            return Task.FromResult(next);
        }
    }
    public Task CompleteAsync(int id, string url, string filename, string filepath, int format)
    {
        lock (_lock) _completed.Add(id);
        return Task.CompletedTask;
    }
    public Task RemoveAsync(int id) { lock (_lock) _removed.Add(id); return Task.CompletedTask; }
}
