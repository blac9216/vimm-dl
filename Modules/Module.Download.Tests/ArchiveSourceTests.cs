using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core;
using Module.Download.Sources;

[TestClass]
public class ArchiveSourceTests
{
    private static ArchiveSource NewSource() => new(NullLogger<ArchiveSource>.Instance);
    private static HttpClient StubClient(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(new StubHandler(body, status));

    private const string GbaMeta = """
        {"metadata":{"title":"Nintendo - Game Boy Advance (No-Intro 2024-02-21)","subject":["Nintendo","handheld","Gameboy advance","no-intro","GBA"]}}
        """;

    [TestMethod]
    public async Task ResolveAsync_ValidUrl_DerivesConsoleFromMetadata()
    {
        const string url = "https://archive.org/download/ef_gba_no-intro_2024-02-21/007%20-%20NightFire%20(USA%2C%20Europe).zip";
        var r = await NewSource().ResolveAsync(url, 0, StubClient(GbaMeta), CancellationToken.None);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(url, r.Value!.DownloadUrl);                       // pasted URL is the download URL
        Assert.AreEqual("007 - NightFire (USA, Europe).zip", r.Value.SuggestedFilename);
        Assert.AreEqual("gba", ConsoleDirectories.Resolve(r.Value.Platform)); // console from metadata
    }

    [TestMethod]
    public async Task ResolveAsync_MetadataFails_StillOk_NoPlatform()
    {
        const string url = "https://archive.org/download/some_item/Game.zip";
        var r = await NewSource().ResolveAsync(url, 0, StubClient("err", HttpStatusCode.InternalServerError), CancellationToken.None);

        Assert.IsTrue(r.IsOk, r.Error);          // a metadata hiccup must not block the download
        Assert.IsNull(r.Value!.Platform);        // best-effort console → null (lands in completed/ root)
        Assert.AreEqual(url, r.Value.DownloadUrl);
    }

    [TestMethod]
    public async Task ResolveAsync_NonArchiveUrl_Fails()
    {
        var r = await NewSource().ResolveAsync("https://example.com/file.zip", 0, StubClient("{}"), CancellationToken.None);
        Assert.IsFalse(r.IsOk);
    }

    [DataTestMethod]
    [DataRow("https://archive.org/download/id/sub/Game.zip", "id", "Game.zip")]
    [DataRow("https://archive.org/download/ef_gba/Some%20Game%20(USA).zip", "ef_gba", "Some Game (USA).zip")]
    public void TryParse_ExtractsIdentifierAndFilename(string url, string id, string file)
    {
        Assert.IsTrue(ArchiveSource.TryParse(url, out var gotId, out var gotFile));
        Assert.AreEqual(id, gotId);
        Assert.AreEqual(file, gotFile);
    }

    [TestMethod]
    public void TryParse_NotDownloadPath_False()
    {
        Assert.IsFalse(ArchiveSource.TryParse("https://archive.org/details/foo", out _, out _));
        Assert.IsFalse(ArchiveSource.TryParse("https://example.com/download/a/b.zip", out _, out _));
    }

    [TestMethod]
    public void Metadata_IsArchive()
    {
        var s = NewSource();
        Assert.AreEqual("archive", s.Id);
        Assert.AreEqual("archive", s.HttpClientName);
        Assert.AreEqual("Internet Archive", s.DisplayName);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
