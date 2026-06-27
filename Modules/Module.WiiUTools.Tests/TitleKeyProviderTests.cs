namespace Module.WiiUTools.Tests;

/// <summary>
/// Exercises the <see cref="ITitleKeyProvider"/> seam end-to-end with the crypto core: a host
/// that supplies only the common key lets the pipeline derive the title key from a ticket, while
/// an unconfigured provider returns <c>null</c> (the "keys required" state). No real keys used.
/// </summary>
[TestClass]
public class TitleKeyProviderTests
{
    /// <summary>In-memory test provider — stands in for the user-configured host implementation.</summary>
    sealed class FakeKeyProvider(byte[]? commonKey, Dictionary<string, byte[]>? titleKeys = null) : ITitleKeyProvider
    {
        public byte[]? GetCommonKey() => commonKey;
        public byte[]? GetTitleKey(string titleId) =>
            titleKeys is not null && titleKeys.TryGetValue(titleId, out var k) ? k : null;
    }

    static byte[] Key(byte fill) { var b = new byte[16]; Array.Fill(b, fill); return b; }

    [TestMethod]
    public void Unconfigured_ReturnsNull_ForBothKeys()
    {
        var p = new FakeKeyProvider(commonKey: null);
        Assert.IsNull(p.GetCommonKey());
        Assert.IsNull(p.GetTitleKey("0005000010123400"));
    }

    [TestMethod]
    public void CommonKey_DerivesTitleKey_FromForgedTicket()
    {
        var commonKey = Key(0xA5);
        var realTitleKey = Key(0x3C);
        const ulong titleId = 0x0005000010123400UL;

        // Forge a ticket: encrypt the title key under the common key (what a real cetk holds).
        var encryptedTitleKey = WiiUCrypto.EncryptTitleKey(realTitleKey, titleId, commonKey);

        var provider = new FakeKeyProvider(commonKey);
        // No pre-supplied per-title key → derive it from the ticket via the common key.
        Assert.IsNull(provider.GetTitleKey(titleId.ToString("X16")));

        var supplied = provider.GetCommonKey();
        Assert.IsNotNull(supplied);
        var derived = WiiUCrypto.DecryptTitleKey(encryptedTitleKey, titleId, supplied);

        Assert.IsTrue(derived.IsOk, derived.Error);
        CollectionAssert.AreEqual(realTitleKey, derived.Value);
    }

    [TestMethod]
    public void PreSuppliedTitleKey_TakesPrecedence()
    {
        var titleKey = Key(0x77);
        var provider = new FakeKeyProvider(
            commonKey: null,
            titleKeys: new() { ["0005000010123400"] = titleKey });

        var k = provider.GetTitleKey("0005000010123400");
        Assert.IsNotNull(k);
        CollectionAssert.AreEqual(titleKey, k);
        Assert.IsNull(provider.GetTitleKey("0005000010999999"), "unknown title → null");
    }
}
