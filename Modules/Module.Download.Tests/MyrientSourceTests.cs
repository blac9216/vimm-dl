using Module.Core;
using Module.Download.Sources;

[TestClass]
public class MyrientSourceTests
{
    [TestMethod]
    public async Task ResolveAsync_DirectUrl_DerivesFilenamePlatformAndUrl()
    {
        const string url = "https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Game%20Boy%20Advance/Some%20Game%20(USA).zip";
        var result = await new MyrientSource().ResolveAsync(url, 0, new HttpClient(), CancellationToken.None);

        Assert.IsTrue(result.IsOk, result.Error);
        var r = result.Value!;
        Assert.AreEqual(url, r.DownloadUrl);                       // pasted URL is the download URL
        Assert.AreEqual("Some Game (USA).zip", r.SuggestedFilename); // decoded last segment
        Assert.AreEqual("Some Game (USA)", r.Title);
        Assert.AreEqual("Game Boy Advance", r.Platform);          // "Nintendo - " stripped
        Assert.AreEqual(0, r.ResolvedFormat);
        Assert.IsNull(r.RequestHeaders);
    }

    [DataTestMethod]
    [DataRow("https://myrient.erista.me/files/Redump/Sony%20-%20PlayStation%202/Game%20(USA).zip", "ps2")]
    [DataRow("https://myrient.erista.me/files/Redump/Nintendo%20-%20GameCube%20-%20NKit%20RVZ/Game.rvz", "gc")]
    [DataRow("https://myrient.erista.me/files/No-Intro/Sega%20-%20Mega%20Drive%20-%20Genesis/Game.zip", "genesis")]
    [DataRow("https://myrient.erista.me/files/No-Intro/Nintendo%20-%20Super%20Nintendo%20Entertainment%20System/Game.zip", "snes")]
    public async Task ResolveAsync_PlatformMapsToConsoleFolder(string url, string expectedFolder)
    {
        var result = await new MyrientSource().ResolveAsync(url, 0, new HttpClient(), CancellationToken.None);
        Assert.IsTrue(result.IsOk, result.Error);
        Assert.AreEqual(expectedFolder, ConsoleDirectories.Resolve(result.Value!.Platform));
    }

    [TestMethod]
    public async Task ResolveAsync_NonHttpUrl_Fails()
    {
        var result = await new MyrientSource().ResolveAsync("ftp://x/y.zip", 0, new HttpClient(), CancellationToken.None);
        Assert.IsFalse(result.IsOk);
    }

    [TestMethod]
    public void Metadata_IsMyrient()
    {
        var s = new MyrientSource();
        Assert.AreEqual("myrient", s.Id);
        Assert.AreEqual("myrient", s.HttpClientName);
        Assert.AreEqual("Myrient", s.DisplayName);
    }
}
