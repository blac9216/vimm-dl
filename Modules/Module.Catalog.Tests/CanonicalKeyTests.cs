namespace Module.Catalog.Tests;

/// <summary>
/// Guards <see cref="CanonicalKey"/> — the hash-derived, name-independent cross-source identity (D2 /
/// #129): single-ROM uses the strongest hash, multi-ROM is an order-independent set hash, CRC-only and
/// unhashable ROMs yield no key.
/// </summary>
[TestClass]
public class CanonicalKeyTests
{
    private static DatRom Rom(string name, string? crc = null, string? md5 = null, string? sha1 = null)
        => new(name, 0, crc, md5, sha1, null);

    [TestMethod]
    public void SingleRom_UsesSha1Token()
    {
        var key = CanonicalKey.Compute([Rom("a.bin", sha1: "ABCDEF01")]);
        Assert.AreEqual("sha1:abcdef01", key);
    }

    [TestMethod]
    public void SingleRom_FallsBackToMd5_WhenNoSha1()
    {
        var key = CanonicalKey.Compute([Rom("a.bin", crc: "12345678", md5: "DEADBEEF")]);
        Assert.AreEqual("md5:deadbeef", key);
    }

    [TestMethod]
    public void CrcOnly_ReturnsNull()
    {
        // CRC32 alone is too collision-prone to anchor identity.
        Assert.IsNull(CanonicalKey.Compute([Rom("a.bin", crc: "12345678")]));
    }

    [TestMethod]
    public void NoRoms_ReturnsNull()
    {
        Assert.IsNull(CanonicalKey.Compute([]));
    }

    [TestMethod]
    public void Sha1_IsNormalized_CaseAndWhitespaceInsensitive()
    {
        var upper = CanonicalKey.Compute([Rom("a.bin", sha1: "  ABCDEF01 ")]);
        var lower = CanonicalKey.Compute([Rom("different-name.bin", sha1: "abcdef01")]);
        Assert.AreEqual(lower, upper);
    }

    [TestMethod]
    public void SameContent_DifferentRomNames_SameKey()
    {
        // Identity is by content, not filename.
        var a = CanonicalKey.Compute([Rom("Game (USA).sfc", sha1: "aaaa")]);
        var b = CanonicalKey.Compute([Rom("Game (Europe).sfc", sha1: "aaaa")]);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void MultiRom_IsOrderIndependent()
    {
        var roms1 = new[] { Rom("track1.bin", sha1: "1111"), Rom("track2.bin", sha1: "2222"), Rom("disc.cue", sha1: "3333") };
        var roms2 = new[] { Rom("disc.cue", sha1: "3333"), Rom("track1.bin", sha1: "1111"), Rom("track2.bin", sha1: "2222") };
        var key1 = CanonicalKey.Compute(roms1);
        var key2 = CanonicalKey.Compute(roms2);
        Assert.IsNotNull(key1);
        Assert.AreEqual(key1, key2);
        StringAssert.StartsWith(key1, "set:");
    }

    [TestMethod]
    public void MultiRom_DifferentContent_DiffersKey()
    {
        var a = CanonicalKey.Compute([Rom("t1", sha1: "1111"), Rom("t2", sha1: "2222")]);
        var b = CanonicalKey.Compute([Rom("t1", sha1: "1111"), Rom("t2", sha1: "9999")]);
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void MultiRom_AnyUnhashableRom_ReturnsNull()
    {
        // One CRC-only track ⇒ the whole multi-disc game has no reliable key.
        var key = CanonicalKey.Compute([Rom("t1", sha1: "1111"), Rom("t2", crc: "12345678")]);
        Assert.IsNull(key);
    }

    [TestMethod]
    public void SingleRom_DiffersFrom_SameHashInAMultiSet()
    {
        // A bare token and a set hash are different shapes, so a 1-ROM game never collides with a
        // multi-ROM game that merely happens to contain the same hash.
        var single = CanonicalKey.Compute([Rom("a", sha1: "1111")]);
        var set = CanonicalKey.Compute([Rom("a", sha1: "1111"), Rom("b", sha1: "2222")]);
        Assert.AreNotEqual(single, set);
    }
}
