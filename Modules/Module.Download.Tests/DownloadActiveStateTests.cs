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
/// EPIC #113 / A1: the per-download state model. Drives a real (small) download through a fake source
/// + fake HTTP and asserts <see cref="DownloadService.ActiveDownloads"/> tracks the in-flight item
/// with independent progress, that the back-compat Current* aliases derive from it, and that it clears
/// when the run ends. Behavior at one worker must match the former singleton model.
/// </summary>
[TestClass]
public class DownloadActiveStateTests
{
    private FakeDownloadBridge _bridge = null!;
    private DownloadService _service = null!;
    private TempDirectory _tmp = null!;
    private readonly byte[] _body = Encoding.ASCII.GetBytes(new string('x', 4096));

    [TestInitialize]
    public void Setup()
    {
        _tmp = new TempDirectory("DownloadActiveTests");
        _bridge = new FakeDownloadBridge();
        var registry = new SourceRegistry([new FakeSource()]);
        _service = new DownloadService(_bridge, NullLogger<DownloadService>.Instance,
            new BodyHttpClientFactory(_body), registry);
        _service.Configure(_tmp.Root);
    }

    [TestCleanup]
    public void Cleanup() => _tmp.Dispose();

    [TestMethod]
    public void ActiveDownloads_IdleIsEmpty()
    {
        Assert.IsEmpty(_service.ActiveDownloads);
        Assert.IsNull(_service.CurrentUrl);
        Assert.AreEqual(0, _service.TotalBytes);
    }

    [TestMethod]
    public async Task SingleDownload_TracksActiveDownload_ThenClears()
    {
        var provider = new OneItemProvider(new DownloadItem(7, "http://fake/item", 0) { Source = "fake" });
        _service.Start(provider);
        await WaitForCompleted();

        // File landed in completed/ (unknown platform → root).
        Assert.IsTrue(File.Exists(Path.Combine(_tmp.Root, "completed", "game.bin")));
        CollectionAssert.Contains(provider.Completed, 7);

        // During the post-complete delay the ActiveDownload is still present, fully described.
        var active = _service.ActiveDownloads;
        Assert.HasCount(1, active);
        Assert.AreEqual("7", active[0].Key);            // stable per-download key = queue item id
        Assert.AreEqual("http://fake/item", active[0].Url);
        Assert.AreEqual("fake", active[0].Source);
        Assert.AreEqual("game.bin", active[0].Filename);
        Assert.AreEqual("done", active[0].State);
        Assert.AreEqual(_body.Length, active[0].Total);
        Assert.AreEqual(_body.Length, active[0].Downloaded);

        // Back-compat aliases derive from the first active download.
        Assert.AreEqual("http://fake/item", _service.CurrentUrl);
        Assert.AreEqual("game.bin", _service.CurrentFile);
        Assert.AreEqual(_body.Length, _service.DownloadedBytes);

        // Ending the run clears the set (and the aliases with it).
        _service.Stop();
        await WaitForNotRunning();
        Assert.IsEmpty(_service.ActiveDownloads);
        Assert.IsNull(_service.CurrentUrl);
    }

    // --- helpers ---

    private async Task WaitForCompleted(int timeout = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (_bridge.AllEvents.Any(e => e is DownloadCompletedEvent)) return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for DownloadCompletedEvent");
    }

    private async Task WaitForNotRunning(int timeout = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (!_service.IsRunning) return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for IsRunning=false");
    }
}

// --- test doubles ---

file sealed class FakeSource : IDownloadSource
{
    public string Id => "fake";
    public string DisplayName => "Fake";
    public string HttpClientName => "fake";
    public Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
        => Task.FromResult(Result<ResolvedDownload>.Ok(
            new ResolvedDownload("http://fake/file", "GameTitle", null, "game.bin", null, 0, null)));
}

file sealed class BodyHttpClientFactory(byte[] body) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new BodyHandler(body));
}

file sealed class BodyHandler(byte[] body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
}

file sealed class OneItemProvider(DownloadItem item) : IDownloadItemProvider
{
    private bool _served;
    public readonly List<int> Completed = [];
    public Task<DownloadItem?> GetNextAsync()
    {
        if (_served) return Task.FromResult<DownloadItem?>(null);
        _served = true;
        return Task.FromResult<DownloadItem?>(item);
    }
    public Task CompleteAsync(int id, string url, string filename, string filepath, int format)
    {
        Completed.Add(id);
        return Task.CompletedTask;
    }
    public Task RemoveAsync(int id) => Task.CompletedTask;
}
