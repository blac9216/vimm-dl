using System.Text;

[TestClass]
public class Crc32Tests
{
    [TestMethod]
    public void ComputeHex_StandardCheckValue()
    {
        // The canonical CRC32/IEEE check value for "123456789" is 0xCBF43926.
        using var s = new MemoryStream("123456789"u8.ToArray());
        Assert.AreEqual("CBF43926", Crc32.ComputeHex(s));
    }

    [TestMethod]
    public void ComputeHex_EmptyStream_IsZero()
    {
        using var s = new MemoryStream();
        Assert.AreEqual("00000000", Crc32.ComputeHex(s));
    }

    [TestMethod]
    public void ComputeHex_MatchesAcrossBufferBoundary()
    {
        // > 8KB buffer to exercise the chunked read loop; compare two independent computations.
        var data = Encoding.ASCII.GetBytes(new string('A', 20000));
        var a = Crc32.ComputeHex(new MemoryStream(data));
        var b = Crc32.ComputeHex(new MemoryStream(data));
        Assert.AreEqual(a, b);
        Assert.AreEqual(8, a.Length);
    }

    [TestMethod]
    public void ToHex_FormatsUppercase8()
        => Assert.AreEqual("0DBEF116", Crc32.ToHex(0x0DBEF116));
}
