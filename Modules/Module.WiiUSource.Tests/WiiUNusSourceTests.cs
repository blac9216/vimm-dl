using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;
using Module.Download;
using Module.Download.Bridge;
using Module.Download.Sources;

namespace Module.WiiUSource.Tests;

/// <summary>
/// Tests for the NUS source: title-ID normalization, the resolved file set (tmd + cetk + content + .h3,
/// with TMD SHA-1 wired in as the integrity check), and an end-to-end download through the real
/// multi-file loop (success lands the WUP set under completed/wiiu/{TitleID}/; a SHA-1 mismatch fails the
/// item). Driven by a synthetic TMD + a fake HTTP handler — no live NUS, no keys.
/// </summary>
[TestClass]
public class WiiUNusSourceTests
{
    const string Base = "http://nus.test/ccs/download";

    static byte[] Bytes(int len, byte fill) { var b = new byte[len]; Array.Fill(b, fill); return b; }
    static byte[] Sha1(byte[] data) => SHA1.HashData(data);

    // Minimal synthetic TMD (RSA-2048 SHA-256 signature → 0x140 blob), per wiiubrew "Title metadata".
    static byte[] BuildTmd(ulong titleId, (uint id, ushort idx, ushort type, ulong size, byte[] sha1)[] contents)
    {
        const int blob = 0x140;
        const int recordsOffset = 0x9A4;
        var buf = new byte[blob + recordsOffset + contents.Length * 0x24];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        var h = buf.AsSpan(blob);
        BinaryPrimitives.WriteUInt64BigEndian(h[0x4C..], titleId);
        BinaryPrimitives.WriteUInt16BigEndian(h[0x9E..], (ushort)contents.Length);
        for (var i = 0; i < contents.Length; i++)
        {
            var r = h[(recordsOffset + i * 0x24)..];
            BinaryPrimitives.WriteUInt32BigEndian(r, contents[i].id);
            BinaryPrimitives.WriteUInt16BigEndian(r[0x4..], contents[i].idx);
            BinaryPrimitives.WriteUInt16BigEndian(r[0x6..], contents[i].type);
            BinaryPrimitives.WriteUInt64BigEndian(r[0x8..], contents[i].size);
            contents[i].sha1.CopyTo(r[0x10..]);
        }
        return buf;
    }

    // ---- NormalizeTitleId ----

    [TestMethod]
    public void NormalizeTitleId_AcceptsHex_StripsSeparators_Uppercases()
    {
        Assert.AreEqual("0005000010123456", WiiUNusSource.NormalizeTitleId("0005000010123456"));
        Assert.AreEqual("0005000010123456", WiiUNusSource.NormalizeTitleId("00050000-10123456"));
        Assert.AreEqual("000500001012ABCD", WiiUNusSource.NormalizeTitleId("000500001012abcd"));
    }

    [TestMethod]
    public void NormalizeTitleId_Rejects_WrongLengthOrNonHex()
    {
        Assert.IsNull(WiiUNusSource.NormalizeTitleId("00050000"));        // too short
        Assert.IsNull(WiiUNusSource.NormalizeTitleId("0005000010123456FF")); // too long
        Assert.IsNull(WiiUNusSource.NormalizeTitleId("000500001012ZZZZ")); // non-hex
        Assert.IsNull(WiiUNusSource.NormalizeTitleId(""));
        Assert.IsNull(WiiUNusSource.NormalizeTitleId(null));
    }

    // ---- ResolveManyAsync ----

    [TestMethod]
    public async Task ResolveMany_BuildsFileSet_TmdCetkContentsAndH3()
    {
        var tmd = BuildTmd(0x0005000010123456UL,
        [
            (0x00000000u, 0, 0x0001, 64, Bytes(20, 0x11)),  // non-hashed → .app verified
            (0x00000001u, 1, 0x2003, 128, Bytes(20, 0x22)), // hashed → .h3 verified, .app not
        ]);
        var handler = new MapHandler(new() { [$"{Base}/0005000010123456/tmd"] = tmd });
        var source = new WiiUNusSource(Base);

        var r = await source.ResolveManyAsync("0005000010123456", 0, new HttpClient(handler), CancellationToken.None);
        Assert.IsTrue(r.IsOk, r.Error);
        var set = r.Value!;

        Assert.AreEqual("Wii U", set.Platform);
        Assert.AreEqual("0005000010123456", set.SubFolder);

        var byName = set.Files.ToDictionary(f => f.SuggestedFilename!);
        // tmd + cetk + 2 .app + 1 .h3 = 5 files
        Assert.HasCount(5, set.Files);
        Assert.IsTrue(byName.ContainsKey("title.tmd"));
        Assert.IsTrue(byName.ContainsKey("title.tik"));
        Assert.IsTrue(byName.ContainsKey("00000000.app"));
        Assert.IsTrue(byName.ContainsKey("00000001.app"));
        Assert.IsTrue(byName.ContainsKey("00000001.h3"));

        // URLs use the lowercase title id; content is the bare 8-hex id (no extension).
        Assert.AreEqual($"{Base}/0005000010123456/tmd", byName["title.tmd"].DownloadUrl);
        Assert.AreEqual($"{Base}/0005000010123456/cetk", byName["title.tik"].DownloadUrl);
        Assert.AreEqual($"{Base}/0005000010123456/00000000", byName["00000000.app"].DownloadUrl);
        Assert.AreEqual($"{Base}/0005000010123456/00000001.h3", byName["00000001.h3"].DownloadUrl);

        // Non-hashed .app carries its TMD SHA-1; hashed .app does not (the .h3 carries it).
        var app0Sha = byName["00000000.app"].ExpectedSha1;
        var h3Sha = byName["00000001.h3"].ExpectedSha1;
        Assert.AreEqual(Convert.ToHexString(Bytes(20, 0x11)), app0Sha);
        Assert.IsNull(byName["00000001.app"].ExpectedSha1);
        Assert.AreEqual(Convert.ToHexString(Bytes(20, 0x22)), h3Sha);
    }

    [TestMethod]
    public async Task ResolveMany_BadTitleId_Fails()
    {
        var r = await new WiiUNusSource(Base).ResolveManyAsync("nope", 0, new HttpClient(new MapHandler([])), CancellationToken.None);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "title ID");
    }

    [TestMethod]
    public async Task ResolveMany_TmdFetch404_Fails()
    {
        var handler = new MapHandler([]); // nothing served → 404
        var r = await new WiiUNusSource(Base).ResolveManyAsync("0005000010123456", 0, new HttpClient(handler), CancellationToken.None);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "TMD");
    }

    [TestMethod]
    public void Source_Identity_IsWiiUMultiFile()
    {
        var s = new WiiUNusSource(Base);
        Assert.AreEqual("wiiu", s.Id);
        Assert.AreEqual("wiiu", s.HttpClientName);
        Assert.IsInstanceOfType<IMultiFileSource>(s);
        Assert.IsInstanceOfType<IDownloadSource>(s);
    }

    // ---- End-to-end through the real multi-file download loop ----

    [TestMethod]
    public async Task EndToEnd_DownloadsAndVerifies_WupSet_IntoTitleFolder()
    {
        const string titleId = "0005000010123456";
        var c0 = Bytes(0x40, 0xA1);   // non-hashed content
        var c1 = Bytes(0x80, 0xB2);   // hashed content (.app not verified)
        var h3 = Bytes(0x20, 0xC3);   // its hash tree (TMD hash covers this)

        var tmd = BuildTmd(0x0005000010123456UL,
        [
            (0x00000000u, 0, 0x0001, (ulong)c0.Length, Sha1(c0)),
            (0x00000001u, 1, 0x2003, (ulong)c1.Length, Sha1(h3)),
        ]);
        var map = new Dictionary<string, byte[]>
        {
            [$"{Base}/{titleId}/tmd"] = tmd,
            [$"{Base}/{titleId}/cetk"] = Bytes(0x300, 0x55),
            [$"{Base}/{titleId}/00000000"] = c0,
            [$"{Base}/{titleId}/00000001"] = c1,
            [$"{Base}/{titleId}/00000001.h3"] = h3,
        };

        using var tmp = new TempDirectory("WiiUNus");
        var provider = await RunDownloadAsync(map, titleId, tmp.Root);

        Assert.HasCount(1, provider.Completions);
        Assert.IsEmpty(provider.Removed);
        var dir = Path.Combine(tmp.Root, "completed", "wiiu", titleId);
        Assert.AreEqual(dir, provider.Completions[0].Filepath);
        foreach (var name in new[] { "title.tmd", "title.tik", "00000000.app", "00000001.app", "00000001.h3" })
            Assert.IsTrue(File.Exists(Path.Combine(dir, name)), $"{name} should exist");
        CollectionAssert.AreEqual(c0, File.ReadAllBytes(Path.Combine(dir, "00000000.app")));
    }

    [TestMethod]
    public async Task EndToEnd_ContentHashMismatch_FailsItem()
    {
        const string titleId = "0005000010123456";
        var realContent = Bytes(0x40, 0xA1);
        var tmd = BuildTmd(0x0005000010123456UL,
            [(0x00000000u, 0, 0x0001, (ulong)realContent.Length, Sha1(realContent))]);
        var map = new Dictionary<string, byte[]>
        {
            [$"{Base}/{titleId}/tmd"] = tmd,
            [$"{Base}/{titleId}/cetk"] = Bytes(0x300, 0x55),
            [$"{Base}/{titleId}/00000000"] = Bytes(0x40, 0xFF), // corrupted — SHA-1 won't match the TMD
        };

        using var tmp = new TempDirectory("WiiUNusBad");
        var provider = await RunDownloadAsync(map, titleId, tmp.Root);

        Assert.IsEmpty(provider.Completions);
        CollectionAssert.Contains(provider.Removed, 1);
    }

    static async Task<CapturingProvider> RunDownloadAsync(Dictionary<string, byte[]> map, string titleId, string root)
    {
        var bridge = new FakeWiiUDownloadBridge();
        var svc = new DownloadService(bridge, NullLogger<DownloadService>.Instance,
            new HandlerFactory(new MapHandler(map)), new SourceRegistry([new WiiUNusSource(Base)]));
        svc.Configure(root);
        var provider = new CapturingProvider(new DownloadItem(1, titleId, 0) { Source = "wiiu" });

        svc.Start(provider);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && provider.Completions.Count == 0 && provider.Removed.Count == 0)
            await Task.Delay(20);
        svc.Stop();
        return provider;
    }
}

// --- test doubles ---

sealed class MapHandler(Dictionary<string, byte[]> content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        if (!content.TryGetValue(url, out var body))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        ok.Content.Headers.ContentLength = body.Length;
        return Task.FromResult(ok);
    }
}

sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

sealed class FakeWiiUDownloadBridge : FakeBridge<DownloadEvent>, IDownloadBridge;

sealed class CapturingProvider(params DownloadItem[] items) : IDownloadItemProvider
{
    private readonly List<DownloadItem> _items = [.. items];
    private readonly object _lock = new();
    private readonly HashSet<int> _done = [];
    private readonly HashSet<int> _removed = [];

    public List<(int Id, string Url, string Filename, string Filepath, int Format)> Completions { get; } = [];
    public List<int> Removed { get; } = [];

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
