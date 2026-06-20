using System.Net;
using Module.Download.Sources;

[TestClass]
public class VimmSourceTests
{
    private static HttpClient StubClient(string html, HttpStatusCode status = HttpStatusCode.OK)
        => new(new StubHandler(html, status));

    [TestMethod]
    public async Task ResolveAsync_ValidVaultPage_ReturnsResolvedDownload()
    {
        var html = """
            <title>The Vault: God of War III (PlayStation 3)</title>
            <div class="sectionTitle">PlayStation 3</div>
            <input type="hidden" name="mediaId" value="83789">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = await new VimmSource().ResolveAsync(
            "https://vimm.net/vault/84578", 0, StubClient(html), CancellationToken.None);

        Assert.IsTrue(result.IsOk, result.Error);
        var r = result.Value!;
        Assert.AreEqual("PlayStation 3", r.Platform);
        StringAssert.Contains(r.DownloadUrl, "mediaId=83789");
        Assert.AreEqual(0, r.ResolvedFormat);
        Assert.IsNotNull(r.RequestHeaders);
        Assert.IsTrue(r.RequestHeaders!.Any(h => h.Name == "Referer" && h.Value == "https://vimm.net/vault/84578"),
            "Referer header should be the vault URL");
        Assert.IsTrue(r.RequestHeaders!.Any(h => h.Name == "Sec-Fetch-Site" && h.Value == "cross-site"),
            "Sec-Fetch-Site header should be cross-site");
    }

    [TestMethod]
    public async Task ResolveAsync_NoMediaId_Fails()
    {
        var result = await new VimmSource().ResolveAsync(
            "https://vimm.net/vault/1", 0, StubClient("<title>Nope</title><body>no media</body>"),
            CancellationToken.None);

        Assert.IsFalse(result.IsOk);
        StringAssert.Contains(result.Error!, "Could not find mediaId");
    }

    [TestMethod]
    public void Metadata_IsVimm()
    {
        var source = new VimmSource();
        Assert.AreEqual("vimm", source.Id);
        Assert.AreEqual("vimms", source.HttpClientName);
        Assert.AreEqual("Vimm's Lair", source.DisplayName);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
