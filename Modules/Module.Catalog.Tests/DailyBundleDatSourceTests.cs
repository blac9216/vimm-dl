using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class DailyBundleDatSourceTests
{
    private const string GbaDat = """
        clrmamepro ( name "Nintendo - Game Boy Advance" version "2026.06.01" )
        game ( name "Advance Wars (USA)" region "USA" rom ( name "Advance Wars (USA).gba" size 4194304 crc DBEF116C ) )
        """;

    private static readonly CatalogSystemInfo Gba = new("Nintendo - Game Boy Advance", "no-intro", "gba");

    // ---- entry-name matching (real No-Intro/Redump filename shapes) ----

    [TestMethod]
    public void IsMatch_TimestampedOriginal_Matches()
        => Assert.IsTrue(DailyBundleDatSource.IsMatch(
            "Nintendo - Game Boy Advance (20260601-123456).dat", "Nintendo - Game Boy Advance"));

    [TestMethod]
    public void IsMatch_BareName_Matches()
        => Assert.IsTrue(DailyBundleDatSource.IsMatch("Nintendo - Game Boy Advance.dat", "Nintendo - Game Boy Advance"));

    [TestMethod]
    public void IsMatch_ParentCloneVariant_Matches()
        => Assert.IsTrue(DailyBundleDatSource.IsMatch(
            "Nintendo - Game Boy Advance (Parent-Clone) (20260601-123456).dat", "Nintendo - Game Boy Advance"));

    [TestMethod]
    public void IsMatch_ShorterPrefix_DoesNotMatchLongerSystem()
        => Assert.IsFalse(DailyBundleDatSource.IsMatch(
            "Nintendo - Game Boy Advance (20260601).dat", "Nintendo - Game Boy"));

    [TestMethod]
    public void IsMatch_NonDatFile_DoesNotMatch()
        => Assert.IsFalse(DailyBundleDatSource.IsMatch("Nintendo - Game Boy Advance (20260601).txt", "Nintendo - Game Boy Advance"));

    // ---- ExtractDat ----

    [TestMethod]
    public void ExtractDat_ReturnsMatchingEntryText()
    {
        var zip = Zip(("Nintendo - Game Boy Advance (20260601-000000).dat", GbaDat));
        var r = DailyBundleDatSource.ExtractDat(zip, "Nintendo - Game Boy Advance");
        Assert.IsTrue(r.IsOk, r.Error);
        StringAssert.Contains(r.Value!, "Advance Wars");
    }

    [TestMethod]
    public void ExtractDat_MissingSystem_Fails()
    {
        var zip = Zip(("Sega - Mega Drive (20260601).dat", "x"));
        var r = DailyBundleDatSource.ExtractDat(zip, "Nintendo - Game Boy Advance");
        Assert.IsFalse(r.IsOk);
    }

    [TestMethod]
    [DataRow(true)]   // parent-clone entry listed first
    [DataRow(false)]  // standard entry listed first
    public void ExtractDat_PrefersNonParentClone(bool parentCloneFirst)
    {
        var std = ("Nintendo - Game Boy Advance (20260601-000000).dat", "STANDARD");
        var pc = ("Nintendo - Game Boy Advance (Parent-Clone) (20260601-000000).dat", "PARENTCLONE");
        var zip = parentCloneFirst ? Zip(pc, std) : Zip(std, pc);

        var r = DailyBundleDatSource.ExtractDat(zip, "Nintendo - Game Boy Advance");

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual("STANDARD", r.Value);
    }

    // ---- GetDatAsync (download + cache) ----

    [TestMethod]
    public async Task GetDat_DownloadsAndExtracts()
    {
        var zip = Zip(("Nintendo - Game Boy Advance (20260601-000000).dat", GbaDat));
        var src = NewSource(_ => (HttpStatusCode.OK, zip));

        var r = await src.GetDatAsync(Gba, default);

        Assert.IsTrue(r.IsOk, r.Error);
        StringAssert.Contains(r.Value!, "Advance Wars");
    }

    [TestMethod]
    public async Task GetDat_DownloadsBundleOncePerGroup_AcrossManySystems()
    {
        var zip = Zip(
            ("Nintendo - Game Boy Advance (20260601).dat", GbaDat),
            ("Nintendo - Super Nintendo Entertainment System (20260601).dat", GbaDat));
        var handler = new ZipHandler(_ => (HttpStatusCode.OK, zip));
        var src = new DailyBundleDatSource(new HttpClient(handler), NullLogger<DailyBundleDatSource>.Instance);

        var a = await src.GetDatAsync(Gba, default);
        var b = await src.GetDatAsync(new CatalogSystemInfo("Nintendo - Super Nintendo Entertainment System", "no-intro", "snes"), default);

        Assert.IsTrue(a.IsOk, a.Error);
        Assert.IsTrue(b.IsOk, b.Error);
        Assert.AreEqual(1, handler.Calls);   // one zip download served both systems
    }

    [TestMethod]
    public async Task GetDat_SeparateGroups_DownloadOnceEach()
    {
        var noIntro = Zip(("Nintendo - Game Boy Advance (20260601).dat", GbaDat));
        var redump = Zip(("Sony - PlayStation 3 (20260601).dat", GbaDat));
        var handler = new ZipHandler(url => (HttpStatusCode.OK, url.Contains("redump") ? redump : noIntro));
        var src = new DailyBundleDatSource(new HttpClient(handler), NullLogger<DailyBundleDatSource>.Instance);

        await src.GetDatAsync(Gba, default);
        await src.GetDatAsync(new CatalogSystemInfo("Sony - PlayStation 3", "redump", "ps3"), default);
        await src.GetDatAsync(Gba, default);   // no-intro again — still cached

        Assert.AreEqual(2, handler.Calls);   // one per group, no re-fetch
    }

    [TestMethod]
    public async Task GetDat_HttpError_Fails()
    {
        var src = NewSource(_ => (HttpStatusCode.NotFound, null));
        var r = await src.GetDatAsync(Gba, default);
        Assert.IsFalse(r.IsOk);
    }

    [TestMethod]
    public async Task GetDat_MissingSystemInBundle_FailsSoft()
    {
        var zip = Zip(("Sega - Mega Drive (20260601).dat", "x"));
        var src = NewSource(_ => (HttpStatusCode.OK, zip));
        var r = await src.GetDatAsync(Gba, default);
        Assert.IsFalse(r.IsOk);   // skipped, like a 404 on the libretro path
    }

    [TestMethod]
    public void InterSystemDelay_IsZero()
        => Assert.AreEqual(TimeSpan.Zero,
            new DailyBundleDatSource(new HttpClient(new ZipHandler(_ => (HttpStatusCode.OK, []))),
                NullLogger<DailyBundleDatSource>.Instance).InterSystemDelay);

    // ---- helpers ----

    private static DailyBundleDatSource NewSource(Func<string, (HttpStatusCode, byte[]?)> responder)
        => new(new HttpClient(new ZipHandler(responder)), NullLogger<DailyBundleDatSource>.Instance);

    private static byte[] Zip(params (string name, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(archive.CreateEntry(name).Open());
                w.Write(content);
            }
        return ms.ToArray();
    }

    private sealed class ZipHandler(Func<string, (HttpStatusCode, byte[]?)> responder) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var (code, bytes) = responder(request.RequestUri!.AbsoluteUri);
            var msg = new HttpResponseMessage(code);
            if (bytes is not null) msg.Content = new ByteArrayContent(bytes);
            return Task.FromResult(msg);
        }
    }
}
