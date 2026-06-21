/// <summary>
/// Covers the vault-page hash/format parsers against trimmed real fixtures: inline media JSON
/// (single-file hash triple + per-format sizes, with nested objects/arrays the bracket-balancer must
/// skip), the dl_format select, and the hashes2.php multi-disc fragment. Values are from real pages
/// (ActRaiser /vault/1009, PS3 /vault/24614, FF7 hashes2 id=43962).
/// </summary>
[TestClass]
public class VimmVaultPageTests
{
    // Single-file SNES page: inline GoodHash/GoodMd5/GoodSha1; nested GoodDate object + Mirror array
    // (the extractor must balance brackets past them); only Zipped (alt 0) is non-zero.
    private const string SnesMedia = """
        <script>let media=[{"ID":989,"GoodDate":{"date":"2026-06-14 01:41:59.000000","timezone_type":3,"timezone":"UTC"},"GoodTitle":"QWN0UmFpc2VyIChVU0EpLnNmYw==","Serial":"SNS-AR-USA","SortOrder":1,"Version":"1.0","Zipped":"658","AltZipped":"0","AltZipped2":"0","GoodHash":"EAC3358D","GoodMd5":"635D5D7DD2AAD4768412FBAE4A32FD6E","GoodSha1":"E8365852CC20178D42C93CD188A7AE9AF45369D7","ZippedText":"658 KB","AltZippedText":"0 KB","AltZipped2Text":"0 KB","Mirror":["us","eu"]}];</script>
        """;

    // PS3 page: two formats (alt 0/1) with non-zero sizes + the dl_format select.
    private const string Ps3Page = """
        <script>let media=[{"ID":14104,"GoodTitle":"","Zipped":"6397335","AltZipped":"6586788","AltZipped2":"0","ZippedText":"6.1 GB","AltZippedText":"6.28 GB","AltZipped2Text":"0 KB"}];</script>
        <select id="dl_format" onchange="setFormat('dl_form', this.value, media)"><option value="0" title="JailBreak folder">JB Folder</option><option value="1" title="Decrypted ISO">.dec.iso</option></select>
        """;

    private const string Hashes2Fragment = """
        <div style="text-align:center; font-size:14pt">Redump File Hashes</div><div class="rounded"><div><div style="display:grid"><div style="grid-column:span 2">Final Fantasy VII (Europe) (Disc 1).bin</div><div>Crc</div><div>900e6a9e</div><div>Md5</div><div>95daa58e45d71bfe6ce3c699c87652a0</div><div>Sha1</div><div>6b0e68ff27c636d560d4575fd990091ab09bed80</div><div style="grid-column:span 2"><br>Final Fantasy VII (Europe) (Disc 1).cue</div><div>Crc</div><div>c64eedb0</div><div>Md5</div><div>03e74074ce7ce4987d7340d8ed398aa3</div><div>Sha1</div><div>3b05568e30762d4fed78bca98712d4d08541102a</div></div></div></div>
        """;

    [TestMethod]
    public void ParseMedia_SingleFile_ReadsHashesNameAndSize()
    {
        var media = VimmVaultParser.ParseMedia(SnesMedia);

        Assert.HasCount(1, media);
        var m = media[0];
        Assert.AreEqual(989, m.Id);
        Assert.AreEqual("SNS-AR-USA", m.Serial);
        Assert.AreEqual("ActRaiser (USA).sfc", m.Name);            // base64 GoodTitle decoded
        Assert.AreEqual("eac3358d", m.Crc);                        // lowercased
        Assert.AreEqual("635d5d7dd2aad4768412fbae4a32fd6e", m.Md5);
        Assert.AreEqual("e8365852cc20178d42c93cd188a7ae9af45369d7", m.Sha1);
        Assert.HasCount(1, m.Sizes);                               // alt 1/2 are zero → dropped
        Assert.AreEqual(0, m.Sizes[0].Alt);
        Assert.AreEqual(658, m.Sizes[0].Bytes);
        Assert.AreEqual("658 KB", m.Sizes[0].Text);
    }

    [TestMethod]
    public void ParseMedia_MultiFormat_KeepsBothNonZeroSizes()
    {
        var media = VimmVaultParser.ParseMedia(Ps3Page);

        Assert.HasCount(1, media);
        var sizes = media[0].Sizes;
        Assert.HasCount(2, sizes);                                 // alt 0 + alt 1; alt 2 zero → dropped
        Assert.AreEqual((0, 6397335L, "6.1 GB"), (sizes[0].Alt, sizes[0].Bytes, sizes[0].Text));
        Assert.AreEqual((1, 6586788L, "6.28 GB"), (sizes[1].Alt, sizes[1].Bytes, sizes[1].Text));
        Assert.IsNull(media[0].Crc);                               // PS3 page has no inline hash
    }

    [TestMethod]
    public void ParseMedia_NoMediaArray_ReturnsEmpty()
    {
        Assert.IsEmpty(VimmVaultParser.ParseMedia("<html>nothing here</html>"));
    }

    [TestMethod]
    public void ParseFormats_ReadsDlFormatOptions()
    {
        var formats = VimmVaultParser.ParseFormats(Ps3Page);

        Assert.HasCount(2, formats);
        Assert.AreEqual((0, "JB Folder"), (formats[0].Alt, formats[0].Label));
        Assert.AreEqual((1, ".dec.iso"), (formats[1].Alt, formats[1].Label));
    }

    [TestMethod]
    public void ParseFormats_SingleFileNoSelect_ReturnsEmpty()
    {
        Assert.IsEmpty(VimmVaultParser.ParseFormats(SnesMedia));
    }

    [TestMethod]
    public void ParseHashes2_ReadsEachFileTriple()
    {
        var files = VimmVaultParser.ParseHashes2(Hashes2Fragment);

        Assert.HasCount(2, files);
        Assert.AreEqual("Final Fantasy VII (Europe) (Disc 1).bin", files[0].FileName);
        Assert.AreEqual("900e6a9e", files[0].Crc);
        Assert.AreEqual("6b0e68ff27c636d560d4575fd990091ab09bed80", files[0].Sha1);
        Assert.AreEqual("Final Fantasy VII (Europe) (Disc 1).cue", files[1].FileName);   // <br> stripped
        Assert.AreEqual("c64eedb0", files[1].Crc);
        Assert.AreEqual("03e74074ce7ce4987d7340d8ed398aa3", files[1].Md5);
    }

    [TestMethod]
    public void ParseHashes2_NoGrid_ReturnsEmpty()
    {
        Assert.IsEmpty(VimmVaultParser.ParseHashes2("<div>no hashes</div>"));
    }
}
