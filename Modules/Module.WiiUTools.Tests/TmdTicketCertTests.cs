using System.Buffers.Binary;
using System.Text;

namespace Module.WiiUTools.Tests;

/// <summary>
/// Fixture/synthetic-parse tests for the TMD, ticket, and cert-chain parsers. The fixture builders below
/// hard-code the public-spec offsets (wiiubrew "Title metadata", wiibrew "Ticket" / "Certificate chain")
/// independently of the parsers' own constants, so a drifted offset fails the round-trip. No real Wii U
/// data or keys are used.
/// </summary>
[TestClass]
public class TmdTicketCertTests
{
    // Signed-blob size by signature type (hard-coded per spec, mirrors WiiUSignature for the fixtures).
    static int SigBlob(uint t) => t switch
    {
        0x00010000 or 0x00010003 => 0x240,
        0x00010001 or 0x00010004 => 0x140,
        0x00010002 or 0x00010005 => 0x80,
        _ => throw new ArgumentException($"bad sig type {t}"),
    };

    static byte[] Hash(byte fill, int len = 20) { var b = new byte[len]; Array.Fill(b, fill); return b; }

    // ---- TMD ----

    static byte[] BuildTmd(ulong titleId, ushort version, ushort bootIndex,
        (uint id, ushort idx, ushort type, ulong size, byte[] sha1)[] contents, uint sigType = 0x00010004)
    {
        var blob = SigBlob(sigType);
        const int recordsOffset = 0x9A4;
        const int recordSize = 0x24;
        var buf = new byte[blob + recordsOffset + contents.Length * recordSize];
        BinaryPrimitives.WriteUInt32BigEndian(buf, sigType);
        var h = buf.AsSpan(blob);
        BinaryPrimitives.WriteUInt64BigEndian(h[0x4C..], titleId);
        BinaryPrimitives.WriteUInt16BigEndian(h[0x9C..], version);
        BinaryPrimitives.WriteUInt16BigEndian(h[0x9E..], (ushort)contents.Length);
        BinaryPrimitives.WriteUInt16BigEndian(h[0xA0..], bootIndex);
        for (var i = 0; i < contents.Length; i++)
        {
            var r = h[(recordsOffset + i * recordSize)..];
            BinaryPrimitives.WriteUInt32BigEndian(r, contents[i].id);
            BinaryPrimitives.WriteUInt16BigEndian(r[0x4..], contents[i].idx);
            BinaryPrimitives.WriteUInt16BigEndian(r[0x6..], contents[i].type);
            BinaryPrimitives.WriteUInt64BigEndian(r[0x8..], contents[i].size);
            contents[i].sha1.CopyTo(r[0x10..]);
        }
        return buf;
    }

    [TestMethod]
    public void Tmd_Parse_ExtractsTitleAndContents()
    {
        var tmd = BuildTmd(0x0005000010123400UL, version: 0x0010, bootIndex: 0,
        [
            (0x00000000u, 0, 0x0001, 100UL, Hash(0x11)),   // encrypted, not hashed
            (0x00000001u, 1, 0x2003, 2048UL, Hash(0x22)),  // encrypted + hashed (.h3)
        ]);

        var r = Tmd.Parse(tmd);
        Assert.IsTrue(r.IsOk, r.Error);
        var t = r.Value!;
        Assert.AreEqual(0x0005000010123400UL, t.TitleId);
        Assert.AreEqual("0005000010123400", t.TitleIdHex);
        Assert.AreEqual((ushort)0x0010, t.TitleVersion);
        Assert.AreEqual((ushort)0, t.BootIndex);
        Assert.HasCount(2, t.Contents);

        Assert.IsTrue(t.Contents[0].IsEncrypted);
        Assert.IsFalse(t.Contents[0].IsHashed);

        var c = t.Contents[1];
        Assert.AreEqual(0x00000001u, c.ContentId);
        Assert.AreEqual("00000001", c.ContentIdHex);
        Assert.AreEqual((ushort)1, c.Index);
        Assert.AreEqual(2048UL, c.Size);
        Assert.IsTrue(c.IsEncrypted);
        Assert.IsTrue(c.IsHashed);
        CollectionAssert.AreEqual(Hash(0x22), c.Sha1Hash);
    }

    [TestMethod]
    public void Tmd_Parse_Rsa4096Signature_HandlesLargerBlob()
    {
        var tmd = BuildTmd(0x0005000010000001UL, 1, 0,
            [(0u, 0, 0x0001, 10UL, Hash(1))], sigType: 0x00010003);
        var r = Tmd.Parse(tmd);
        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(0x0005000010000001UL, r.Value!.TitleId);
    }

    [TestMethod]
    public void Tmd_Parse_Empty_Fails() => Assert.IsFalse(Tmd.Parse([]).IsOk);

    [TestMethod]
    public void Tmd_Parse_UnknownSigType_Fails()
    {
        var buf = new byte[0x1000];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0xDEADBEEF);
        var r = Tmd.Parse(buf);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "signature");
    }

    [TestMethod]
    public void Tmd_Parse_TruncatedHeader_Fails()
    {
        var buf = new byte[0x140 + 0x100];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        Assert.IsFalse(Tmd.Parse(buf).IsOk);
    }

    [TestMethod]
    public void Tmd_Parse_TruncatedContentRecords_Fails()
    {
        var buf = new byte[0x140 + 0x9A4]; // header only, but claims 3 contents
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0x140 + 0x9E), 3);
        var r = Tmd.Parse(buf);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "content records");
    }

    // ---- Ticket ----

    static byte[] BuildTicket(ulong titleId, byte[] encTitleKey, byte commonKeyIndex = 0, uint sigType = 0x00010004)
    {
        var blob = SigBlob(sigType);
        const int bodyLen = 0x2A4; // a comfortably full ticket body
        var buf = new byte[blob + bodyLen];
        BinaryPrimitives.WriteUInt32BigEndian(buf, sigType);
        var b = buf.AsSpan(blob);
        encTitleKey.CopyTo(b[0x7F..]);
        BinaryPrimitives.WriteUInt64BigEndian(b[0x9C..], titleId);
        b[0xB1] = commonKeyIndex;
        return buf;
    }

    [TestMethod]
    public void Ticket_Parse_ExtractsKeyAndTitleId()
    {
        var key = new byte[16];
        for (var i = 0; i < 16; i++) key[i] = (byte)(0xF0 + i);
        var r = Ticket.Parse(BuildTicket(0x0005000010123400UL, key, commonKeyIndex: 0));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(0x0005000010123400UL, r.Value!.TitleId);
        Assert.AreEqual("0005000010123400", r.Value.TitleIdHex);
        CollectionAssert.AreEqual(key, r.Value.EncryptedTitleKey);
        Assert.AreEqual((byte)0, r.Value.CommonKeyIndex);
    }

    [TestMethod]
    public void Ticket_PlusCommonKey_DecryptsTitleKey()
    {
        // End-to-end W2→W1: a parsed ticket's encrypted key + the common key recovers the real title key.
        var commonKey = new byte[16]; Array.Fill(commonKey, (byte)0xA5);
        var realTitleKey = new byte[16]; Array.Fill(realTitleKey, (byte)0x3C);
        const ulong titleId = 0x0005000010123400UL;

        var encKey = WiiUCrypto.EncryptTitleKey(realTitleKey, titleId, commonKey);
        var ticket = Ticket.Parse(BuildTicket(titleId, encKey)).Value!;

        var dec = WiiUCrypto.DecryptTitleKey(ticket.EncryptedTitleKey, ticket.TitleId, commonKey);
        Assert.IsTrue(dec.IsOk, dec.Error);
        CollectionAssert.AreEqual(realTitleKey, dec.Value);
    }

    [TestMethod]
    public void Ticket_Parse_Empty_Fails() => Assert.IsFalse(Ticket.Parse([]).IsOk);

    [TestMethod]
    public void Ticket_Parse_Truncated_Fails()
    {
        var buf = new byte[0x140 + 0x50];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        Assert.IsFalse(Ticket.Parse(buf).IsOk);
    }

    // ---- Cert chain ----

    static byte[] BuildCert(string issuer, string name, uint keyType, uint sigType = 0x00010004)
    {
        var blob = SigBlob(sigType);
        var keySize = keyType switch { 0 => 0x238, 1 => 0x138, 2 => 0x78, _ => throw new ArgumentException() };
        var buf = new byte[blob + 0x88 + keySize];
        BinaryPrimitives.WriteUInt32BigEndian(buf, sigType);
        var b = buf.AsSpan(blob);
        Encoding.ASCII.GetBytes(issuer).CopyTo(b[0x00..]);
        BinaryPrimitives.WriteUInt32BigEndian(b[0x40..], keyType);
        Encoding.ASCII.GetBytes(name).CopyTo(b[0x44..]);
        return buf;
    }

    [TestMethod]
    public void CertChain_Parse_TwoCerts_EnumeratesBoth()
    {
        var chain = BuildCert("Root-CA00000003", "CP0000000b", keyType: 1)
            .Concat(BuildCert("Root-CA00000003-CP0000000b", "CP0000000b-XS", keyType: 2))
            .ToArray();

        var r = CertChain.Parse(chain);
        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(2, r.Value!.Certs);
        Assert.AreEqual("Root-CA00000003", r.Value.Certs[0].Issuer);
        Assert.AreEqual("CP0000000b", r.Value.Certs[0].Name);
        Assert.AreEqual(1u, r.Value.Certs[0].KeyType);
        Assert.AreEqual("CP0000000b-XS", r.Value.Certs[1].Name);
        Assert.AreEqual(2u, r.Value.Certs[1].KeyType);
    }

    [TestMethod]
    public void CertChain_Parse_Empty_Fails() => Assert.IsFalse(CertChain.Parse([]).IsOk);

    [TestMethod]
    public void CertChain_Parse_UnknownKeyType_Fails()
    {
        var buf = new byte[0x140 + 0x88 + 0x100];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x140 + 0x40), 99); // bad key type
        Assert.IsFalse(CertChain.Parse(buf).IsOk);
    }

    [TestMethod]
    public void CertChain_Parse_TrailingZeros_StopCleanly()
    {
        var chain = BuildCert("Root", "CP", keyType: 1).Concat(new byte[64]).ToArray();
        var r = CertChain.Parse(chain);
        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(1, r.Value!.Certs);
    }
}
