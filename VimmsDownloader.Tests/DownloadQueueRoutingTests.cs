namespace VimmsDownloader.Tests;

/// <summary>
/// Covers the Wii U post-download routing predicate (#267). Only an encrypted NUS title set enters
/// <c>WiiUConversionPipeline</c>; a Wii U <i>disc</i> download (Vimm ships .wux inside .7z) is the same
/// platform but needs no conversion, and routing it into the decrypt pipeline made a successful
/// download report "Missing title.tmd or title.tik".
/// </summary>
[TestClass]
public class DownloadQueueRoutingTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"wiiuroute-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public void NusTitleSet_DirectoryWithTmd_IsRecognised()
    {
        var title = Path.Combine(_dir, "0005000010144000");
        Directory.CreateDirectory(title);
        File.WriteAllText(Path.Combine(title, "title.tmd"), "tmd");
        File.WriteAllText(Path.Combine(title, "title.tik"), "tik");

        Assert.IsTrue(DownloadQueue.IsNusTitleSet(title));
    }

    [TestMethod]
    public void DiscDownload_SingleFile_IsNotANusTitleSet()
    {
        var wux = Path.Combine(_dir, "Adventure Time (USA).wux");
        File.WriteAllText(wux, "not a WUP set");

        Assert.IsFalse(DownloadQueue.IsNusTitleSet(wux));
    }

    [TestMethod]
    public void DiscArchive_SingleFile_IsNotANusTitleSet()
    {
        var sevenZip = Path.Combine(_dir, "Adventure Time (USA).7z");
        File.WriteAllText(sevenZip, "archive");

        Assert.IsFalse(DownloadQueue.IsNusTitleSet(sevenZip));
    }

    /// <summary>A folder of extracted disc files is still not a WUP set — the TMD is what defines one.</summary>
    [TestMethod]
    public void DirectoryWithoutTmd_IsNotANusTitleSet()
    {
        var extracted = Path.Combine(_dir, "extracted");
        Directory.CreateDirectory(extracted);
        File.WriteAllText(Path.Combine(extracted, "game.wux"), "x");

        Assert.IsFalse(DownloadQueue.IsNusTitleSet(extracted));
    }

    [TestMethod]
    public void MissingPath_IsNotANusTitleSet()
        => Assert.IsFalse(DownloadQueue.IsNusTitleSet(Path.Combine(_dir, "nope")));
}
