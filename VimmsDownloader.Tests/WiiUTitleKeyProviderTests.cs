namespace VimmsDownloader.Tests;

/// <summary>
/// Covers <see cref="WiiUTitleKeyProvider.SetCommonKey"/> validation (32-hex parse, wrong length, non-hex,
/// null/blank/whitespace, set-then-clear) — the Wii U mirror of <see cref="ArchiveAuth"/>, host-internal
/// via InternalsVisibleTo. No real key material; the bytes here are an arbitrary test pattern.
/// </summary>
[TestClass]
public class WiiUTitleKeyProviderTests
{
    const string ValidHex = "000102030405060708090A0B0C0D0E0F";
    static readonly byte[] ValidBytes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    [TestMethod]
    public void ValidHex_ParsesToSixteenBytes()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey(ValidHex);
        CollectionAssert.AreEqual(ValidBytes, p.GetCommonKey());
    }

    [TestMethod]
    public void LowercaseHex_Accepted()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey("ffeeddccbbaa99887766554433221100");
        Assert.HasCount(16, p.GetCommonKey()!);
    }

    [TestMethod]
    public void LeadingTrailingWhitespace_Trimmed()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey($"  {ValidHex}  ");
        CollectionAssert.AreEqual(ValidBytes, p.GetCommonKey());
    }

    [TestMethod]
    public void WrongLength_ClearsKey()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey("00112233");            // too short
        Assert.IsNull(p.GetCommonKey());
        p.SetCommonKey(new string('a', 33));   // too long
        Assert.IsNull(p.GetCommonKey());
    }

    [TestMethod]
    public void NonHex_ClearsKey()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey(new string('Z', 32));   // 32 chars but not hex
        Assert.IsNull(p.GetCommonKey());
    }

    [TestMethod]
    public void NullBlankOrWhitespace_ClearsKey()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey(null);
        Assert.IsNull(p.GetCommonKey());
        p.SetCommonKey("");
        Assert.IsNull(p.GetCommonKey());
        p.SetCommonKey("   ");
        Assert.IsNull(p.GetCommonKey());
    }

    [TestMethod]
    public void SetValidThenBlank_ClearsKey()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey(ValidHex);
        Assert.IsNotNull(p.GetCommonKey());
        p.SetCommonKey("");
        Assert.IsNull(p.GetCommonKey());
    }

    [TestMethod]
    public void GetTitleKey_AlwaysNull()
    {
        var p = new WiiUTitleKeyProvider();
        p.SetCommonKey(ValidHex);
        Assert.IsNull(p.GetTitleKey("0005000010123456"));
    }
}
