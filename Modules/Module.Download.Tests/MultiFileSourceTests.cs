using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core;
using Module.Core.Testing;
using Module.Download.Bridge;
using Module.Download.Sources;
using Module.Download.Tests.Helpers;

namespace Module.Download.Tests;

/// <summary>
/// W4 (#108): the multi-file download loop. Drives <see cref="DownloadService"/> with a fake
/// <see cref="IMultiFileSource"/> to prove one queue item fans out into N files (landing together under
/// completed/{console}/{SubFolder}/), produces a single completion + one post-download, resumes partials
/// per file, and that the single-file path is unaffected.
/// </summary>
[TestClass]
public class MultiFileSourceTests
{
    private FakeDownloadBridge _bridge = null!;
    private TempDirectory _tmp = null!;

    [TestInitialize]
    public void Setup()
    {
        _tmp = new TempDirectory("MultiFileSourceTests");
        _bridge = new FakeDownloadBridge();
    }

    [TestCleanup]
    public void Cleanup() => _tmp.Dispose();

    // Deterministic payload of a given length (no RNG → reproducible).
    private static byte[] Bytes(int len)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = (byte)(i * 31 + 7);
        return b;
    }

    private (DownloadService Svc, List<(string Url, string Filename, string Filepath, int Format)> Posts) NewService(
        HttpMessageHandler handler, params IDownloadSource[] sources)
    {
        var svc = new DownloadService(_bridge, NullLogger<DownloadService>.Instance,
            new HandlerFactory(handler), new SourceRegistry(sources));
        svc.Configure(_tmp.Root);
        var posts = new List<(string, string, string, int)>();
        svc.OnPostDownload = (url, fn, fp, fmt) => { lock (posts) posts.Add((url, fn, fp, fmt)); return Task.CompletedTask; };
        return (svc, posts);
    }

    private static async Task WaitFor(Func<bool> cond, int timeout = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (cond()) return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for condition");
    }

    private static ResolvedDownload File(string url, string name) =>
        new(url, name, "Wii U", name, null, 0, null);

    [TestMethod]
    public async Task MultiFile_DownloadsAllFiles_IntoSubfolder_OneCompletion()
    {
        const string titleId = "0005000010123400";
        var contents = new Dictionary<string, byte[]>
        {
            ["http://nus/title/tmd"] = Bytes(64),
            ["http://nus/title/cetk"] = Bytes(48),
            ["http://nus/title/00000001.app"] = Bytes(3000),
        };
        var handler = new MapHandler(contents);
        var resolution = new MultiFileResolution("My WiiU Game", "Wii U",
        [
            File("http://nus/title/tmd", "tmd"),
            File("http://nus/title/cetk", "cetk"),
            File("http://nus/title/00000001.app", "00000001.app"),
        ], SubFolder: titleId);

        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(resolution));
        var provider = new CapturingProvider(new DownloadItem(1, titleId, 0) { Source = "fake-multi" });
        var (svc, posts) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Completions.Count >= 1);
        svc.Stop();

        // Exactly one completion, recorded against the per-title folder.
        Assert.HasCount(1, provider.Completions);
        var completion = provider.Completions[0];
        Assert.AreEqual(titleId, completion.Filename);
        var expectedDir = Path.Combine(_tmp.Root, "completed", "wiiu", titleId);
        Assert.AreEqual(expectedDir, completion.Filepath);

        // All three files present with byte-correct content.
        foreach (var (url, body) in contents)
        {
            var name = url.Split('/')[^1];
            var path = Path.Combine(expectedDir, name);
            Assert.IsTrue(System.IO.File.Exists(path), $"{name} should exist");
            CollectionAssert.AreEqual(body, System.IO.File.ReadAllBytes(path));
        }

        // One post-download for the whole set, pointing at the folder. Nothing removed.
        Assert.HasCount(1, posts);
        Assert.AreEqual(expectedDir, posts[0].Filepath);
        Assert.AreEqual(titleId, posts[0].Filename);
        Assert.IsEmpty(provider.Removed);
    }

    [TestMethod]
    public async Task MultiFile_ResumesPartialFile_PerFile()
    {
        const string titleId = "0005000010ABCDEF";
        var contentBody = Bytes(4000);
        var contents = new Dictionary<string, byte[]>
        {
            ["http://nus/t/tmd"] = Bytes(32),
            ["http://nus/t/00000001.app"] = contentBody,
        };
        var handler = new MapHandler(contents);

        // Pre-seed a partial of the content file in downloading/ (crash-recovery scenario).
        var downloadingDir = Path.Combine(_tmp.Root, "downloading");
        Directory.CreateDirectory(downloadingDir);
        System.IO.File.WriteAllBytes(Path.Combine(downloadingDir, "00000001.app"), contentBody[..1500]);

        var resolution = new MultiFileResolution("Resumable", "Wii U",
        [
            File("http://nus/t/tmd", "tmd"),
            File("http://nus/t/00000001.app", "00000001.app"),
        ], SubFolder: titleId);
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(resolution));
        var provider = new CapturingProvider(new DownloadItem(1, titleId, 0) { Source = "fake-multi" });
        var (svc, _) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Completions.Count >= 1);
        svc.Stop();

        // The content file resumed (a Range request was issued) and ended byte-correct.
        var finalPath = Path.Combine(_tmp.Root, "completed", "wiiu", titleId, "00000001.app");
        CollectionAssert.AreEqual(contentBody, System.IO.File.ReadAllBytes(finalPath));
        Assert.IsTrue(handler.RangeRequests.Any(u => u.Contains("00000001.app")),
            "expected a Range (resume) request for the partial content file");
    }

    [TestMethod]
    public async Task MultiFile_FileFailure_AbortsSet_RemovesItem_NoCompletion()
    {
        // The second file 404s (absent from the map) → the whole set fails and the item is dropped.
        var contents = new Dictionary<string, byte[]> { ["http://nus/x/tmd"] = Bytes(16) };
        var handler = new MapHandler(contents);
        var resolution = new MultiFileResolution("Broken", "Wii U",
        [
            File("http://nus/x/tmd", "tmd"),
            File("http://nus/x/missing.app", "missing.app"),
        ], SubFolder: "0005000010DEAD00");
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(resolution));
        var provider = new CapturingProvider(new DownloadItem(1, "0005000010DEAD00", 0) { Source = "fake-multi" });
        var (svc, posts) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Removed.Count >= 1);
        svc.Stop();

        Assert.IsEmpty(provider.Completions);
        Assert.IsEmpty(posts);
        CollectionAssert.Contains(provider.Removed, 1);
        Assert.IsNotEmpty(_bridge.ErrorEvents, "an error should be emitted");
    }

    [TestMethod]
    public async Task MultiFile_NoSubfolder_LandsInConsoleRoot()
    {
        var contents = new Dictionary<string, byte[]> { ["http://nus/n/a.bin"] = Bytes(100) };
        var handler = new MapHandler(contents);
        var resolution = new MultiFileResolution("NoSub", "Wii U",
            [File("http://nus/n/a.bin", "a.bin")], SubFolder: null);
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(resolution));
        var provider = new CapturingProvider(new DownloadItem(1, "id", 0) { Source = "fake-multi" });
        var (svc, _) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Completions.Count >= 1);
        svc.Stop();

        var consoleRoot = Path.Combine(_tmp.Root, "completed", "wiiu");
        Assert.IsTrue(System.IO.File.Exists(Path.Combine(consoleRoot, "a.bin")));
        Assert.AreEqual(consoleRoot, provider.Completions[0].Filepath);
    }

    [TestMethod]
    public async Task MultiFile_UnknownPlatform_LandsInCompletedRoot()
    {
        var contents = new Dictionary<string, byte[]> { ["http://nus/u/a.bin"] = Bytes(100) };
        var handler = new MapHandler(contents);
        var resolution = new MultiFileResolution("Unknown", Platform: null,
            [File("http://nus/u/a.bin", "a.bin")], SubFolder: "SET1");
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(resolution));
        var provider = new CapturingProvider(new DownloadItem(1, "id", 0) { Source = "fake-multi" });
        var (svc, _) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Completions.Count >= 1);
        svc.Stop();

        // No console dir for a null platform → completed/{SubFolder}/.
        var expected = Path.Combine(_tmp.Root, "completed", "SET1");
        Assert.AreEqual(expected, provider.Completions[0].Filepath);
        Assert.IsTrue(System.IO.File.Exists(Path.Combine(expected, "a.bin")));
    }

    [TestMethod]
    public async Task MultiFile_ResolveFails_RemovesItem()
    {
        var handler = new MapHandler([]);
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Fail("title not found"));
        var provider = new CapturingProvider(new DownloadItem(1, "bad", 0) { Source = "fake-multi" });
        var (svc, posts) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Removed.Count >= 1);
        svc.Stop();

        Assert.IsEmpty(provider.Completions);
        Assert.IsEmpty(posts);
        Assert.IsTrue(_bridge.ErrorEvents.Any(e => e.Message.Contains("title not found")));
    }

    [TestMethod]
    public async Task MultiFile_EmptyFileSet_RemovesItem()
    {
        var handler = new MapHandler([]);
        var resolution = new MultiFileResolution("Empty", "Wii U", [], SubFolder: "X");
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(resolution));
        var provider = new CapturingProvider(new DownloadItem(1, "id", 0) { Source = "fake-multi" });
        var (svc, posts) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Removed.Count >= 1);
        svc.Stop();

        Assert.IsEmpty(provider.Completions);
        Assert.IsEmpty(posts);
    }

    [TestMethod]
    public async Task SingleFileSource_Unaffected_DownloadsOneFile()
    {
        // Regression guard: a plain IDownloadSource still takes the single-file path unchanged.
        var contents = new Dictionary<string, byte[]> { ["http://single/file"] = Bytes(500) };
        var handler = new MapHandler(contents);
        var source = new SingleFileTestSource("http://single/file", "game.bin", "Wii U");
        var provider = new CapturingProvider(new DownloadItem(1, "whatever", 0) { Source = "single" });
        var (svc, posts) = NewService(handler, source);

        svc.Start(provider);
        await WaitFor(() => provider.Completions.Count >= 1);
        svc.Stop();

        Assert.HasCount(1, provider.Completions);
        Assert.AreEqual("game.bin", provider.Completions[0].Filename);
        var path = Path.Combine(_tmp.Root, "completed", "wiiu", "game.bin");
        Assert.AreEqual(path, provider.Completions[0].Filepath);
        Assert.IsTrue(System.IO.File.Exists(path));
        Assert.HasCount(1, posts);
    }

    [TestMethod]
    public async Task MultiFileSource_DefaultResolveAsync_Fails()
    {
        // The default IDownloadSource.ResolveAsync on a multi-file source is never used by the engine;
        // it returns a clear failure so the single-file entry point can't be invoked by accident.
        var source = new FakeMultiSource(_ => Result<MultiFileResolution>.Ok(
            new MultiFileResolution("x", "Wii U", [], null)));
        using var http = new HttpClient(new MapHandler([]));
        var r = await ((IDownloadSource)source).ResolveAsync("id", 0, http, CancellationToken.None);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "multi-file");
    }
}

// --- test doubles ---

/// <summary>Serves per-URL byte content, with 206 Range support so per-file resume is observable.</summary>
sealed class MapHandler(Dictionary<string, byte[]> content) : HttpMessageHandler
{
    private readonly object _lock = new();
    public List<string> RangeRequests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        if (!content.TryGetValue(url, out var body))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        if (request.Headers.Range is { Ranges.Count: 1 } range && range.Ranges.First().From is long from)
        {
            lock (_lock) RangeRequests.Add(url);
            var slice = body[(int)from..];
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(slice) };
            partial.Content.Headers.ContentLength = slice.Length;
            return Task.FromResult(partial);
        }

        var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        ok.Content.Headers.ContentLength = body.Length;
        return Task.FromResult(ok);
    }
}

sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

sealed class FakeMultiSource(Func<string, Result<MultiFileResolution>> resolve) : IMultiFileSource
{
    public string Id => "fake-multi";
    public string DisplayName => "Fake Multi";
    public string HttpClientName => "fake-multi";
    public Task<Result<MultiFileResolution>> ResolveManyAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
        => Task.FromResult(resolve(sourceId));
}

sealed class SingleFileTestSource(string url, string filename, string? platform) : IDownloadSource
{
    public string Id => "single";
    public string DisplayName => "Single";
    public string HttpClientName => "single";
    public Task<Result<ResolvedDownload>> ResolveAsync(string sourceId, int format, HttpClient http, CancellationToken ct)
        => Task.FromResult(Result<ResolvedDownload>.Ok(new ResolvedDownload(url, "Title", platform, filename, null, 0, null)));
}

sealed class CapturingProvider : IDownloadItemProvider
{
    private readonly List<DownloadItem> _items;
    private readonly object _lock = new();
    private readonly HashSet<int> _done = [];
    private readonly HashSet<int> _removed = [];

    public List<(int Id, string Url, string Filename, string Filepath, int Format)> Completions { get; } = [];
    public List<int> Removed { get; } = [];

    public CapturingProvider(params DownloadItem[] items) => _items = [.. items];

    public Task<DownloadItem?> GetNextAsync(IReadOnlySet<int> excludeIds)
    {
        lock (_lock)
        {
            var next = _items.FirstOrDefault(i =>
                !excludeIds.Contains(i.Id) && !_done.Contains(i.Id) && !_removed.Contains(i.Id));
            return Task.FromResult(next);
        }
    }

    public Task CompleteAsync(int id, string url, string filename, string filepath, int format)
    {
        lock (_lock) { _done.Add(id); Completions.Add((id, url, filename, filepath, format)); }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(int id)
    {
        lock (_lock) { _removed.Add(id); Removed.Add(id); }
        return Task.CompletedTask;
    }
}
