namespace Module.WiiUTools.Tests;

/// <summary>
/// Round-trip + IV-derivation tests for the AES-128-CBC crypto core. No real Wii U keys are
/// used or needed: a throwaway synthetic key proves the cipher reverses, and explicit IV
/// assertions pin the derivation to the public spec (wiiubrew "Encryption keys" / "Title
/// metadata") independently of the round-trip.
/// </summary>
[TestClass]
public class WiiUCryptoTests
{
    // A throwaway 16-byte test key — NOT a real Wii U key. Used only to prove the round-trip.
    static byte[] TestKey() =>
    [
        0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
    ];

    // Deterministic, block-friendly payload of a given length (no RNG → reproducible).
    static byte[] Pattern(int len)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = (byte)(i * 7 + 3);
        return b;
    }

    [TestMethod]
    public void TitleKeyIv_IsTitleIdBigEndian_PaddedToSixteen()
    {
        var iv = WiiUCrypto.TitleKeyIv(0x0005000010101000UL);
        CollectionAssert.AreEqual(
            new byte[] { 0x00, 0x05, 0x00, 0x00, 0x10, 0x10, 0x10, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 },
            iv);
    }

    [TestMethod]
    public void ContentIv_IsContentIndexBigEndian_PaddedToSixteen()
    {
        var iv = WiiUCrypto.ContentIv(0x0102);
        CollectionAssert.AreEqual(
            new byte[] { 0x01, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            iv);
    }

    [TestMethod]
    public void DecryptTitleKey_RoundTrips_WithSyntheticKey()
    {
        var commonKey = TestKey();
        var titleKey = Pattern(16);
        const ulong titleId = 0x0005000010123400UL;

        var enc = WiiUCrypto.EncryptTitleKey(titleKey, titleId, commonKey);
        Assert.HasCount(16, enc);
        CollectionAssert.AreNotEqual(titleKey, enc, "ciphertext should differ from plaintext");

        var dec = WiiUCrypto.DecryptTitleKey(enc, titleId, commonKey);
        Assert.IsTrue(dec.IsOk, dec.Error);
        CollectionAssert.AreEqual(titleKey, dec.Value);
    }

    [TestMethod]
    public void DecryptTitleKey_WrongCommonKey_YieldsGarbage()
    {
        var titleKey = Pattern(16);
        const ulong titleId = 0x0005000010123400UL;
        var enc = WiiUCrypto.EncryptTitleKey(titleKey, titleId, TestKey());

        var wrong = TestKey();
        wrong[0] ^= 0xFF;
        var dec = WiiUCrypto.DecryptTitleKey(enc, titleId, wrong);

        Assert.IsTrue(dec.IsOk, "raw CBC decrypt always produces output");
        CollectionAssert.AreNotEqual(titleKey, dec.Value, "but a wrong key must not recover the title key");
    }

    [TestMethod]
    public void DecryptTitleKey_WrongTitleId_YieldsGarbage()
    {
        // The title ID is the IV, so the first block must differ under a different title ID.
        var commonKey = TestKey();
        var titleKey = Pattern(16);
        var enc = WiiUCrypto.EncryptTitleKey(titleKey, 0x0005000010123400UL, commonKey);

        var dec = WiiUCrypto.DecryptTitleKey(enc, 0x0005000010123401UL, commonKey);

        Assert.IsTrue(dec.IsOk);
        CollectionAssert.AreNotEqual(titleKey, dec.Value);
    }

    [TestMethod]
    public void DecryptTitleKey_BadEncryptedKeyLength_Fails()
    {
        var r = WiiUCrypto.DecryptTitleKey(new byte[15], 0, TestKey());
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "Encrypted title key");
    }

    [TestMethod]
    public void DecryptTitleKey_BadCommonKeyLength_Fails()
    {
        var r = WiiUCrypto.DecryptTitleKey(new byte[16], 0, new byte[8]);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "Common key");
    }

    [TestMethod]
    public void DecryptContent_RoundTrips_Buffer()
    {
        var titleKey = TestKey();
        const ushort idx = 3;
        var plain = Pattern(64);

        var enc = WiiUCrypto.EncryptContent(plain, titleKey, idx);
        CollectionAssert.AreNotEqual(plain, enc);

        var dec = WiiUCrypto.DecryptContent(enc, titleKey, idx);
        Assert.IsTrue(dec.IsOk, dec.Error);
        CollectionAssert.AreEqual(plain, dec.Value);
    }

    [TestMethod]
    public void DecryptContent_WrongContentIndex_YieldsGarbageFirstBlock()
    {
        var titleKey = TestKey();
        var plain = Pattern(64);
        var enc = WiiUCrypto.EncryptContent(plain, titleKey, 3);

        var dec = WiiUCrypto.DecryptContent(enc, titleKey, 4);

        Assert.IsTrue(dec.IsOk);
        CollectionAssert.AreNotEqual(plain, dec.Value);
    }

    [TestMethod]
    public void DecryptContent_NonBlockAligned_Fails()
    {
        var r = WiiUCrypto.DecryptContent(new byte[20], TestKey(), 0);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "block size");
    }

    [TestMethod]
    public void DecryptContent_BadTitleKeyLength_Fails()
    {
        var r = WiiUCrypto.DecryptContent(new byte[16], new byte[10], 0);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "Title key");
    }

    [TestMethod]
    public async Task DecryptContentAsync_RoundTrips_Stream()
    {
        var titleKey = TestKey();
        const ushort idx = 7;
        var plain = Pattern(1 << 16); // 64 KiB, block-aligned — exercises multi-block streaming
        var enc = WiiUCrypto.EncryptContent(plain, titleKey, idx);

        using var input = new MemoryStream(enc);
        using var output = new MemoryStream();
        var r = await WiiUCrypto.DecryptContentAsync(input, output, titleKey, idx);

        Assert.IsTrue(r.IsOk, r.Error);
        CollectionAssert.AreEqual(plain, output.ToArray());
    }

    [TestMethod]
    public async Task DecryptContentAsync_MatchesBufferOverload()
    {
        var titleKey = TestKey();
        const ushort idx = 1;
        var plain = Pattern(256);
        var enc = WiiUCrypto.EncryptContent(plain, titleKey, idx);

        using var input = new MemoryStream(enc);
        using var output = new MemoryStream();
        await WiiUCrypto.DecryptContentAsync(input, output, titleKey, idx);

        var buffer = WiiUCrypto.DecryptContent(enc, titleKey, idx);
        Assert.IsTrue(buffer.IsOk);
        CollectionAssert.AreEqual(buffer.Value, output.ToArray());
    }

    [TestMethod]
    public async Task DecryptContentAsync_NonBlockAligned_Fails()
    {
        using var input = new MemoryStream(new byte[20]);
        using var output = new MemoryStream();
        var r = await WiiUCrypto.DecryptContentAsync(input, output, TestKey(), 0);
        Assert.IsFalse(r.IsOk);
    }

    [TestMethod]
    public async Task DecryptContentAsync_BadTitleKeyLength_Fails()
    {
        using var input = new MemoryStream(new byte[16]);
        using var output = new MemoryStream();
        var r = await WiiUCrypto.DecryptContentAsync(input, output, new byte[3], 0);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "Title key");
    }
}
