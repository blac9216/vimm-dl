using System.Text;

/// <summary>
/// Header detection for the import path (#118 / L1): the four fixed-size headers some dumpers prepend
/// (iNES, FDS, Atari Lynx, Atari 7800). Detection is by magic bytes, and a truncated read must never
/// report a header (the import would then strip the wrong number of bytes).
/// </summary>
[TestClass]
public class RomHeadersTests
{
    private static byte[] WithMagic(string magicAscii, int totalLen, int magicOffset = 0)
    {
        var buf = new byte[totalLen];
        var bytes = Encoding.ASCII.GetBytes(magicAscii);
        bytes.CopyTo(buf, magicOffset);
        return buf;
    }

    [TestMethod]
    public void Detects_INes_Header_16Bytes()
    {
        var head = WithMagic("NES", 32);
        Assert.AreEqual(16, RomHeaders.DetectHeaderLength(head));
    }

    [TestMethod]
    public void Detects_Fds_Header_16Bytes()
    {
        var head = WithMagic("FDS", 32);
        Assert.AreEqual(16, RomHeaders.DetectHeaderLength(head));
    }

    [TestMethod]
    public void Detects_Lynx_Header_64Bytes()
    {
        var head = WithMagic("LYNX", 128);
        Assert.AreEqual(64, RomHeaders.DetectHeaderLength(head));
    }

    [TestMethod]
    public void Detects_Atari7800_Header_128Bytes()
    {
        // A78: a version byte at offset 0, then the "ATARI7800" magic at offset 1.
        var head = WithMagic("ATARI7800", 256, magicOffset: 1);
        head[0] = 3; // header version
        Assert.AreEqual(128, RomHeaders.DetectHeaderLength(head));
    }

    [TestMethod]
    public void NoMagic_ReturnsZero()
    {
        var head = Encoding.ASCII.GetBytes(new string('A', 256));
        Assert.AreEqual(0, RomHeaders.DetectHeaderLength(head));
    }

    [TestMethod]
    public void EmptyInput_ReturnsZero()
    {
        Assert.AreEqual(0, RomHeaders.DetectHeaderLength(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void TruncatedRead_DoesNotReportHeader()
    {
        // "LYNX" magic present, but fewer than the 64 header bytes were supplied — a real headerless ROM
        // shorter than the header must not be mistaken for headered (we'd strip past the file otherwise).
        var head = WithMagic("LYNX", 40);
        Assert.AreEqual(0, RomHeaders.DetectHeaderLength(head));
    }
}
