using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;
using Module.Download.Bridge;
using Module.Download.Sources;
using Module.Download.Tests.Helpers;

namespace Module.Download.Tests;

/// <summary>
/// EPIC #113 / A4: archive retries + idle/stall watchdog. Drives archive downloads through a scripted
/// HTTP handler to prove a failing transfer retries up to <c>archive_retries</c> (then surfaces a clear
/// error and is dropped), and a stalled transfer (no byte progress for <c>archive_idle</c>) is cancelled
/// and retried. Scoped to archive items — Vimm keeps its existing behavior.
/// </summary>
[TestClass]
public class DownloadResilienceTests
{
    private FakeDownloadBridge _bridge = null!;
    private TempDirectory _tmp = null!;

    [TestInitialize]
    public void Setup()
    {
        _tmp = new TempDirectory("DownloadResilienceTests");
        _bridge = new FakeDownloadBridge();
    }

    [TestCleanup]
    public void Cleanup() => _tmp.Dispose();

    private DownloadService NewService(ScriptedHandler handler)
    {
        var registry = new SourceRegistry([new GateSource("archive")]);
        return new DownloadService(_bridge, NullLogger<DownloadService>.Instance,
            new SingleScriptedFactory(handler), registry);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("payload")) };
    private static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError);

    [TestMethod]
    public async Task ArchiveDownload_RetriesThenSucceeds()
    {
        var handler = new ScriptedHandler([ServerError, ServerError, Ok]); // fail twice, then succeed
        var svc = NewService(handler);
        svc.ArchiveRetryBackoff = _ => TimeSpan.Zero;
        svc.Configure(_tmp.Root, archiveParallelism: 1, archiveRetries: 3);
        var provider = new MultiItemProvider(One("archive"));
        svc.Start(provider);

        await WaitFor(() => provider.CompletedCount == 1, "completion after retries");
        Assert.AreEqual(3, handler.Calls); // 2 failed attempts + 1 success
        Assert.IsTrue(_bridge.AllEvents.OfType<DownloadStatusEvent>().Any(e => e.Message.StartsWith("Retry 1/3")));
        svc.Stop();
    }

    [TestMethod]
    public async Task ArchiveDownload_ExhaustsRetries_SurfacesErrorAndStops()
    {
        var handler = new ScriptedHandler([]); // always 500 (empty script → default error)
        var svc = NewService(handler);
        svc.ArchiveRetryBackoff = _ => TimeSpan.Zero;
        svc.Configure(_tmp.Root, archiveParallelism: 1, archiveRetries: 2);
        var provider = new MultiItemProvider(One("archive"));
        svc.Start(provider);

        await WaitFor(() => _bridge.AllEvents.OfType<DownloadErrorEvent>().Any(), "error surfaced");
        await WaitFor(() => _bridge.AllEvents.OfType<DownloadDoneEvent>().Any(), "run finished (item dropped, not looping)");
        Assert.AreEqual(0, provider.CompletedCount);
        Assert.AreEqual(3, handler.Calls); // initial + 2 retries, then dropped (no infinite loop)
        svc.Stop();
    }

    [TestMethod]
    public async Task ArchiveDownload_StallTriggersCancelAndRetry()
    {
        // First attempt yields one byte then hangs; the idle watchdog cancels it; the retry succeeds.
        var handler = new ScriptedHandler([() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallContent() }, Ok]);
        var svc = NewService(handler);
        svc.ArchiveRetryBackoff = _ => TimeSpan.Zero;
        svc.Configure(_tmp.Root, archiveParallelism: 1, archiveRetries: 2, archiveIdleSeconds: 1);
        var provider = new MultiItemProvider(One("archive"));
        svc.Start(provider);

        await WaitFor(() => provider.CompletedCount == 1, "completion after stall-retry", timeout: 8000);
        Assert.AreEqual(2, handler.Calls); // stalled attempt + successful retry
        svc.Stop();
    }

    // --- helpers ---

    private static List<DownloadItem> One(string source) => [new DownloadItem(1, $"http://{source}/1", 0) { Source = source }];

    private async Task WaitFor(Func<bool> cond, string what, int timeout = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (cond()) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Timed out waiting for {what}");
    }
}

// --- test doubles ---

sealed class ScriptedHandler(IEnumerable<Func<HttpResponseMessage>> responses) : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new(responses);
    private int _calls;
    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        Func<HttpResponseMessage> next;
        lock (_responses)
            next = _responses.Count > 0 ? _responses.Dequeue() : () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        return Task.FromResult(next());
    }
}

sealed class SingleScriptedFactory(ScriptedHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>Content that yields a single byte then blocks until the read is cancelled — simulates a stall.</summary>
sealed class StallContent : HttpContent
{
    public StallContent() => Headers.ContentLength = 1000;
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
    protected override bool TryComputeLength(out long length) { length = 1000; return true; }
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new StallStream());
}

sealed class StallStream : Stream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        // Never yields a byte — the transfer makes no progress, so the idle watchdog cancels the attempt.
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 1000;
    public override long Position { get => 0; set { } }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
    public override void Write(byte[] buffer, int offset, int count) { }
}
