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

    private const string SearchJson = """
        {"response":{"docs":[
          {"identifier":"ef_gba_no-intro_2024-02-21","title":"Nintendo - Game Boy Advance (No-Intro 2024-02-21)","subject":["GBA","no-intro"]},
          {"identifier":"random_homebrew","title":"Some Random Thing"}
        ]}}
        """;

    private const string FilesJson = """
        {"files":[
          {"name":"007 - NightFire (USA, Europe).zip","size":"12345"},
          {"name":"Another Game (USA).zip","size":67890},
          {"name":"__ia_thumb.jpg","size":"100"},
          {"name":"ef_gba_meta.xml","size":"50"},
          {"name":"ef_gba_archive.torrent","size":"200"}
        ]}
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

    [TestMethod]
    [DataRow("https://archive.org/download/id/sub/Game.zip", "id", "Game.zip")]
    [DataRow("https://archive.org/download/ef_gba/Some%20Game%20(USA).zip", "ef_gba", "Some Game (USA).zip")]
    [DataRow("https://dl.archive.org/download/id/Game.zip", "id", "Game.zip")] // subdomain allowed
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
    [DataRow("https://fooarchive.org/download/id/Game.zip")] // look-alike host, not a subdomain
    [DataRow("https://archive.org.evil.com/download/id/Game.zip")] // host suffix trick
    [DataRow("ftp://archive.org/download/id/Game.zip")] // non-http(s) scheme
    [DataRow("file:///download/id/Game.zip")]
    public void TryParse_RejectsForeignHostOrScheme(string url)
        => Assert.IsFalse(ArchiveSource.TryParse(url, out _, out _));

    [TestMethod]
    public void Metadata_IsArchive()
    {
        var s = NewSource();
        Assert.AreEqual("archive", s.Id);
        Assert.AreEqual("archive", s.HttpClientName);
        Assert.AreEqual("Internet Archive", s.DisplayName);
    }

    // --- ICatalogSource: search sets ---

    [TestMethod]
    public async Task SearchSetsAsync_ParsesDocs_DerivesPlatformFromMetadata()
    {
        var r = await NewSource().SearchSetsAsync("game boy advance", StubClient(SearchJson), CancellationToken.None);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(2, r.Value!.Count);

        var gba = r.Value[0];
        Assert.AreEqual("ef_gba_no-intro_2024-02-21", gba.Id);
        Assert.AreEqual("Nintendo - Game Boy Advance (No-Intro 2024-02-21)", gba.Title);
        Assert.AreEqual("gba", ConsoleDirectories.Resolve(gba.Platform)); // console from title/subject

        Assert.IsNull(r.Value[1].Platform); // unknown console → null, still listed
    }

    [TestMethod]
    public async Task SearchSetsAsync_HttpFails_ReturnsFail()
    {
        var r = await NewSource().SearchSetsAsync("x", StubClient("err", HttpStatusCode.InternalServerError), CancellationToken.None);
        Assert.IsFalse(r.IsOk);
    }

    // --- ICatalogSource: list files ---

    [TestMethod]
    public async Task ListFilesAsync_ParsesFiles_ExcludesMetadata_BuildsDownloadUrl()
    {
        var r = await NewSource().ListFilesAsync("ef_gba_no-intro_2024-02-21", null, StubClient(FilesJson), CancellationToken.None);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(2, r.Value!.Count); // .jpg thumb, .xml, .torrent excluded

        var first = r.Value[0];
        Assert.AreEqual("007 - NightFire (USA, Europe).zip", first.Name);
        Assert.AreEqual(12345, first.Size); // size parsed from string
        StringAssert.StartsWith(first.DownloadUrl, "https://archive.org/download/ef_gba_no-intro_2024-02-21/");
        StringAssert.Contains(first.DownloadUrl, "NightFire");        // name is URL-encoded into the path
        Assert.IsFalse(first.DownloadUrl.Contains(' '));               // spaces escaped

        Assert.AreEqual(67890, r.Value[1].Size); // size parsed from number
    }

    [TestMethod]
    public async Task ListFilesAsync_Filter_NarrowsResultsCaseInsensitively()
    {
        var r = await NewSource().ListFilesAsync("ef_gba_no-intro_2024-02-21", "nightfire", StubClient(FilesJson), CancellationToken.None);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(1, r.Value!.Count);
        Assert.AreEqual("007 - NightFire (USA, Europe).zip", r.Value[0].Name);
    }

    [TestMethod]
    public async Task ListFilesAsync_HttpFails_ReturnsFail()
    {
        var r = await NewSource().ListFilesAsync("id", null, StubClient("nope", HttpStatusCode.NotFound), CancellationToken.None);
        Assert.IsFalse(r.IsOk);
    }

    [TestMethod]
    [DataRow("game_meta.xml")]
    [DataRow("game.sqlite")]
    [DataRow("game_archive.torrent")]
    [DataRow("__ia_thumb.jpg")]
    [DataRow("game_reviews.xml")]
    public void IsMetadataFile_SkipsSidecars(string name)
        => Assert.IsTrue(ArchiveSource.IsMetadataFile(name));

    [TestMethod]
    [DataRow("Cool Game (USA).zip")]
    [DataRow("Cool Game (USA).7z")]
    public void IsMetadataFile_KeepsRoms(string name)
        => Assert.IsFalse(ArchiveSource.IsMetadataFile(name));

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
