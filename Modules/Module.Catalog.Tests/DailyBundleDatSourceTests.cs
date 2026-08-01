using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class DailyBundleDatSourceTests
{
    // The real bundles ship XML (DAT-o-MATIC v3 in no-intro.zip, Logiqx in redump.zip) — NOT the
    // clrmamepro text the libretro mirror serves. This fixture mirrors the real wire format; the
    // earlier clrmamepro fixture let #265 hide, because these tests only ever asserted on the
    // extracted text and never parsed it.
    private const string GbaDat = """
        <?xml version="1.0"?>
        <datafile>
        	<header><name>Nintendo - Game Boy Advance</name><version>20260601-000000</version></header>
        	<game name="Advance Wars (USA)"><rom name="Advance Wars (USA).gba" size="4194304" crc="dbef116c" serial="AWRE"/></game>
        </datafile>
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

    /// <summary>
    /// Wii U regression (#266): "Nintendo - Wii U (Digital) (CDN)" shares its filename prefix with the
    /// "(CDN) (Dev)" and "(CDN) (Lotcheck)" DATs in the same bundle, so all three satisfy the prefix
    /// test. The retail DAT is the least-qualified match and must win regardless of zip ordering.
    /// </summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ExtractDat_PrefersLeastQualifiedMatch_WiiUVariants(int retailIndex)
    {
        const string name = "Nintendo - Wii U (Digital) (CDN)";
        var entries = new List<(string, string)>
        {
            ($"{name} (Dev) (20220718-071500).dat", "DEV"),
            ($"{name} (Lotcheck) (20220718-071500).dat", "LOTCHECK"),
        };
        entries.Insert(retailIndex, ($"{name} (20260618-192853).dat", "RETAIL"));

        var r = DailyBundleDatSource.ExtractDat(Zip([.. entries]), name);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual("RETAIL", r.Value);
    }

    [TestMethod]
    [DataRow("Nintendo - Wii U (Digital) (CDN) (20260618-192853).dat", 1)]
    [DataRow("Nintendo - Wii U (Digital) (CDN) (Dev) (20220718-071500).dat", 2)]
    [DataRow("Nintendo - Wii U (Digital) (CDN).dat", 0)]
    public void CountQualifiers_CountsTrailingGroups(string fileName, int expected)
        => Assert.AreEqual(expected, DailyBundleDatSource.CountQualifiers(fileName, "Nintendo - Wii U (Digital) (CDN)"));

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

    /// <summary>
    /// The end-to-end guard for #265: what the bundle actually serves must survive the parser the sync
    /// service picks for it. Asserting on parsed games (not extracted text) is the check that was
    /// missing — with the clrmamepro-only parser this yielded 0 while every other test stayed green.
    /// </summary>
    [TestMethod]
    public async Task GetDat_ExtractedBundleText_ActuallyParses()
    {
        var zip = Zip(("Nintendo - Game Boy Advance (20260601-000000).dat", GbaDat));
        var src = NewSource(_ => (HttpStatusCode.OK, zip));

        var r = await src.GetDatAsync(Gba, default);
        Assert.IsTrue(r.IsOk, r.Error);

        var parser = DatParsers.For(r.Value!);
        var games = parser.Parse(new StringReader(r.Value!)).ToList();

        Assert.IsInstanceOfType<XmlDatParser>(parser);
        Assert.HasCount(1, games);
        Assert.AreEqual("Advance Wars (USA)", games[0].Name);
        Assert.AreEqual("Nintendo - Game Boy Advance", parser.Header?.Name);
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
