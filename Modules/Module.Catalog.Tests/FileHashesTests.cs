using System.Text;

[TestClass]
public class FileHashesTests
{
    [TestMethod]
    public void ComputeAll_KnownVectors_ForHello()
    {
        var h = FileHashes.ComputeAll(new MemoryStream("hello"u8.ToArray()));
        // Independently-known hashes of "hello".
        Assert.AreEqual("3610A686", h.Crc);                                     // CRC32/IEEE, uppercase
        Assert.AreEqual("5d41402abc4b2a76b9719d911017c592", h.Md5);             // MD5, lowercase
        Assert.AreEqual("aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d", h.Sha1);    // SHA1, lowercase
    }

    [TestMethod]
    public void ComputeAll_MatchesSingleCrc_AcrossBufferBoundary()
    {
        // > 64KB to exercise the chunked read loop; the streamed CRC must match the dedicated path.
        var data = Encoding.ASCII.GetBytes(new string('A', 200_000));
        var multi = FileHashes.ComputeAll(new MemoryStream(data));
        Assert.AreEqual(Crc32.ComputeHex(new MemoryStream(data)), multi.Crc);
        Assert.AreEqual(40, multi.Sha1!.Length);
        Assert.AreEqual(32, multi.Md5!.Length);
    }

    [TestMethod]
    public void ComputeAll_EmptyStream_IsKnownEmptyHashes()
    {
        var h = FileHashes.ComputeAll(new MemoryStream());
        Assert.AreEqual("00000000", h.Crc);
        Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", h.Md5);          // MD5 of empty
        Assert.AreEqual("da39a3ee5e6b4b0d3255bfef95601890afd80709", h.Sha1); // SHA1 of empty
    }
}
