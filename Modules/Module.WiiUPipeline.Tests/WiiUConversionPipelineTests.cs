using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Pipeline;
using Module.Core.Testing;
using Module.WiiUPipeline.Bridge;
using Module.WiiUTools;

namespace Module.WiiUPipeline.Tests;

/// <summary>
/// End-to-end tests for the Wii U pipeline: a synthetic downloaded WUP set (title.tmd + title.tik +
/// encrypted content) decrypts through Fetching → Decrypting → Extracting → Packaging → Done, U8 content
/// is extracted, and the no-keys path stops at a clear "keys required" state. Synthetic keys/data only —
/// no real Wii U material. The test builds the encrypted set with raw AES-128-CBC matching W1's IV scheme.
/// </summary>
[TestClass]
public class WiiUConversionPipelineTests
{
    const ulong TitleId = 0x0005000010123456UL;
    const string TitleHex = "0005000010123456";

    static byte[] Key(byte fill) { var b = new byte[16]; Array.Fill(b, fill); return b; }
    static byte[] TitleKeyIv(ulong id) { var iv = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(iv, id); return iv; }
    static byte[] ContentIv(ushort idx) { var iv = new byte[16]; BinaryPrimitives.WriteUInt16BigEndian(iv, idx); return iv; }
    static byte[] PadTo16(byte[] d) { var b = new byte[(d.Length + 15) & ~15]; d.CopyTo(b, 0); return b; }

    static byte[] AesCbc(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, iv, PaddingMode.None);
    }

    // Minimal synthetic TMD (RSA-2048 SHA-256 → 0x140 blob), per wiiubrew "Title metadata".
    static byte[] BuildTmd(params (uint id, ushort idx, ushort type, ulong size)[] contents)
    {
        const int blob = 0x140, recordsOffset = 0x9A4;
        var buf = new byte[blob + recordsOffset + contents.Length * 0x24];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        var h = buf.AsSpan(blob);
        BinaryPrimitives.WriteUInt64BigEndian(h[0x4C..], TitleId);
        BinaryPrimitives.WriteUInt16BigEndian(h[0x9E..], (ushort)contents.Length);
        for (var i = 0; i < contents.Length; i++)
        {
            var r = h[(recordsOffset + i * 0x24)..];
            BinaryPrimitives.WriteUInt32BigEndian(r, contents[i].id);
            BinaryPrimitives.WriteUInt16BigEndian(r[0x4..], contents[i].idx);
            BinaryPrimitives.WriteUInt16BigEndian(r[0x6..], contents[i].type);
            BinaryPrimitives.WriteUInt64BigEndian(r[0x8..], contents[i].size);
        }
        return buf;
    }

    // Minimal synthetic ticket (per wiibrew "Ticket"): encrypted title key @0x7F, title id @0x9C.
    static byte[] BuildTicket(byte[] encryptedTitleKey)
    {
        const int blob = 0x140, bodyLen = 0x2A4;
        var buf = new byte[blob + bodyLen];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x00010004);
        var b = buf.AsSpan(blob);
        encryptedTitleKey.CopyTo(b[0x7F..]);
        BinaryPrimitives.WriteUInt64BigEndian(b[0x9C..], TitleId);
        return buf;
    }

    // Minimal U8 archive: root dir + one file. Per wiiubrew "U8 archive".
    static byte[] BuildU8OneFile(string name, byte[] fileData)
    {
        const int rootNodeOffset = 0x20, nodeCount = 2;
        var str = new List<byte> { 0 };               // root name ""
        var fileNameOff = str.Count;
        str.AddRange(Encoding.ASCII.GetBytes(name)); str.Add(0);
        var nodeTableSize = nodeCount * 12;
        var stringTableStart = rootNodeOffset + nodeTableSize;
        var dataStart = stringTableStart + str.Count;
        var buf = new byte[dataStart + fileData.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x55AA382D);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), rootNodeOffset);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8), (uint)(nodeTableSize + str.Count));
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(12), (uint)dataStart);
        WriteNode(buf, rootNodeOffset, 1, 0, 0, nodeCount);                        // root dir, size = node count
        WriteNode(buf, rootNodeOffset + 12, 0, fileNameOff, (uint)dataStart, (uint)fileData.Length);
        str.ToArray().CopyTo(buf, stringTableStart);
        fileData.CopyTo(buf, dataStart);
        return buf;
    }

    static void WriteNode(byte[] buf, int off, byte type, int nameOff, uint dataOff, uint size)
    {
        buf[off] = type;
        buf[off + 1] = (byte)(nameOff >> 16); buf[off + 2] = (byte)(nameOff >> 8); buf[off + 3] = (byte)nameOff;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 4), dataOff);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 8), size);
    }

    // Minimal synthetic FST: root → "content" dir → one file pointing at section `fileSecIdx`.
    // Per retroreversing "Wii U File formats" (the W3 parser's spec).
    static byte[] BuildFstOneFile(uint offsetFactor, ushort fileSecIdx, uint fileOffset, uint fileSize, string fileName)
    {
        const int headerSize = 0x20, sectionCount = 1, entryCount = 3;
        var entryTableStart = headerSize + sectionCount * 0x20;
        var str = new List<byte> { 0 };              // root name ""
        var dirNameOff = str.Count; str.AddRange(Encoding.ASCII.GetBytes("content")); str.Add(0);
        var fileNameOff = str.Count; str.AddRange(Encoding.ASCII.GetBytes(fileName)); str.Add(0);
        var stringPoolStart = entryTableStart + entryCount * 0x10;
        var buf = new byte[stringPoolStart + str.Count];

        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x46535400);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), offsetFactor);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8), sectionCount);
        WriteFstEntry(buf, entryTableStart + 0 * 0x10, 1, 0, 0, entryCount, 0);             // root dir
        WriteFstEntry(buf, entryTableStart + 1 * 0x10, 1, dirNameOff, 0, entryCount, 0);    // "content" dir, end = 3
        WriteFstEntry(buf, entryTableStart + 2 * 0x10, 0, fileNameOff, fileOffset, fileSize, fileSecIdx);
        str.ToArray().CopyTo(buf, stringPoolStart);
        return buf;
    }

    static void WriteFstEntry(byte[] buf, int off, byte type, int nameOff, uint offset, uint size, ushort secIdx)
    {
        buf[off] = type;
        buf[off + 1] = (byte)(nameOff >> 16); buf[off + 2] = (byte)(nameOff >> 8); buf[off + 3] = (byte)nameOff;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 4), offset);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 8), size);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(off + 0xE), secIdx);
    }

    /// <summary>Write a synthetic encrypted WUP set into a folder and return the folder + title key.</summary>
    static (string Folder, byte[] TitleKey) WriteSet(string root, byte[] commonKey,
        byte[] plaintextContent, ushort index = 0, ushort type = 0x0001)
    {
        var folder = Path.Combine(root, "completed", "wiiu", TitleHex);
        Directory.CreateDirectory(folder);

        var titleKey = Key(0x3C);
        var encTitleKey = AesCbc(titleKey, commonKey, TitleKeyIv(TitleId));
        File.WriteAllBytes(Path.Combine(folder, "title.tmd"),
            BuildTmd((0x00000000u, index, type, (ulong)plaintextContent.Length)));
        File.WriteAllBytes(Path.Combine(folder, "title.tik"), BuildTicket(encTitleKey));
        File.WriteAllBytes(Path.Combine(folder, "00000000.app"),
            AesCbc(PadTo16(plaintextContent), titleKey, ContentIv(index)));
        return (folder, titleKey);
    }

    sealed class FakeKeyProvider(byte[]? commonKey, byte[]? titleKey = null) : ITitleKeyProvider
    {
        public byte[]? GetCommonKey() => commonKey;
        public byte[]? GetTitleKey(string titleId) => titleKey;
    }

    sealed class FakeBridgeImpl : FakeBridge<PipelineStatusEvent>, IWiiUPipelineBridge;

    static WiiUConversionPipeline NewPipeline(ITitleKeyProvider keys, out FakeBridgeImpl bridge)
    {
        bridge = new FakeBridgeImpl();
        var p = new WiiUConversionPipeline(bridge, keys, NullLogger<WiiUConversionPipeline>.Instance);
        p.Configure(1);
        return p;
    }

    static async Task<PipelineStatusEvent> WaitForTerminalAsync(WiiUConversionPipeline p, string key, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var s = p.GetStatuses().FirstOrDefault(e => e.ItemName == key);
            if (s != null && PipelinePhase.IsTerminal(s.Phase)) return s;
            await Task.Delay(20);
        }
        Assert.Fail($"Timed out waiting for a terminal status for {key}");
        return null!; // unreachable
    }

    [TestMethod]
    public async Task Decrypts_WithCommonKey_ToDecryptedFolder()
    {
        using var tmp = new TempDirectory("WiiUPipe");
        var content = Encoding.ASCII.GetBytes("DECRYPTED WII U CONTENT PAYLOAD!"); // 31 bytes (not 16-aligned)
        var (folder, _) = WriteSet(tmp.Root, Key(0xA5), content);

        var pipeline = NewPipeline(new FakeKeyProvider(Key(0xA5)), out var bridge);
        Assert.IsTrue(pipeline.Enqueue(folder));
        var terminal = await WaitForTerminalAsync(pipeline, TitleHex);

        Assert.AreEqual(WiiUPhase.Done, terminal.Phase, terminal.Message);
        var decPath = Path.Combine(folder, "decrypted", "00000000.app");
        Assert.IsTrue(File.Exists(decPath));
        CollectionAssert.AreEqual(content, File.ReadAllBytes(decPath)); // trimmed to the TMD size
        Assert.IsTrue(pipeline.IsConverted(TitleHex));

        // Progressed through all phases.
        var phases = bridge.AllEvents.OfType<PipelineStatusEvent>().Select(e => e.Phase).ToList();
        foreach (var ph in new[] { WiiUPhase.Fetching, WiiUPhase.Decrypting, WiiUPhase.Extracting, WiiUPhase.Packaging, WiiUPhase.Done })
            CollectionAssert.Contains(phases, ph);
    }

    [TestMethod]
    public async Task Decrypts_U8Content_ExtractsFiles()
    {
        using var tmp = new TempDirectory("WiiUPipeU8");
        var rpx = Encoding.ASCII.GetBytes("BOOT-RPX-BYTES");
        var u8 = BuildU8OneFile("boot.rpx", rpx);
        var (folder, _) = WriteSet(tmp.Root, Key(0xA5), u8);

        var pipeline = NewPipeline(new FakeKeyProvider(Key(0xA5)), out _);
        pipeline.Enqueue(folder);
        var terminal = await WaitForTerminalAsync(pipeline, TitleHex);

        Assert.AreEqual(WiiUPhase.Done, terminal.Phase, terminal.Message);
        var extracted = Path.Combine(folder, "decrypted", "00000000", "boot.rpx");
        Assert.IsTrue(File.Exists(extracted), "U8 file should be extracted");
        CollectionAssert.AreEqual(rpx, File.ReadAllBytes(extracted));
    }

    [TestMethod]
    public async Task FstLayout_AssemblesFileFromAnotherContent()
    {
        // Two contents: content 0 is an FST that maps "content/data.bin" into content 1 (secondary index 1)
        // at offset 16; content 1 holds 16 bytes of padding followed by the file's 16 bytes. The pipeline
        // should lay the file out at decrypted/content/data.bin, byte-correct (#223).
        using var tmp = new TempDirectory("WiiUPipeFst");
        var commonKey = Key(0xA5);
        var titleKey = Key(0x3C);

        var fileData = Encoding.ASCII.GetBytes("FST-FILE-DATA-16"); // 16 bytes
        var content1 = new byte[32];
        fileData.CopyTo(content1, 16);                              // file lives at offset 16 in content 1
        var fst = BuildFstOneFile(offsetFactor: 1, fileSecIdx: 1, fileOffset: 16, fileSize: (uint)fileData.Length, "data.bin");

        var folder = Path.Combine(tmp.Root, "completed", "wiiu", TitleHex);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "title.tmd"), BuildTmd(
            (0x00000000u, 0, 0x0001, (ulong)fst.Length),
            (0x00000001u, 1, 0x0001, (ulong)content1.Length)));
        File.WriteAllBytes(Path.Combine(folder, "title.tik"), BuildTicket(AesCbc(titleKey, commonKey, TitleKeyIv(TitleId))));
        File.WriteAllBytes(Path.Combine(folder, "00000000.app"), AesCbc(PadTo16(fst), titleKey, ContentIv(0)));
        File.WriteAllBytes(Path.Combine(folder, "00000001.app"), AesCbc(content1, titleKey, ContentIv(1)));

        var pipeline = NewPipeline(new FakeKeyProvider(commonKey), out _);
        pipeline.Enqueue(folder);
        var terminal = await WaitForTerminalAsync(pipeline, TitleHex);

        Assert.AreEqual(WiiUPhase.Done, terminal.Phase, terminal.Message);
        var outFile = Path.Combine(folder, "decrypted", "content", "data.bin");
        Assert.IsTrue(File.Exists(outFile), "FST-mapped file should be assembled at its real path");
        CollectionAssert.AreEqual(fileData, File.ReadAllBytes(outFile));
    }

    [TestMethod]
    public async Task PreSuppliedTitleKey_BypassesTicket()
    {
        using var tmp = new TempDirectory("WiiUPipeTk");
        var content = PadTo16(Encoding.ASCII.GetBytes("RAW"));
        // Common key here is wrong/null; the provider supplies the title key directly.
        var (folder, titleKey) = WriteSet(tmp.Root, Key(0xA5), content);

        var pipeline = NewPipeline(new FakeKeyProvider(commonKey: null, titleKey: titleKey), out _);
        pipeline.Enqueue(folder);
        var terminal = await WaitForTerminalAsync(pipeline, TitleHex);

        Assert.AreEqual(WiiUPhase.Done, terminal.Phase, terminal.Message);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(folder, "decrypted", "00000000.app")));
    }

    [TestMethod]
    public async Task NoKeys_StopsAtKeysRequired()
    {
        using var tmp = new TempDirectory("WiiUPipeNoKey");
        var (folder, _) = WriteSet(tmp.Root, Key(0xA5), PadTo16(Encoding.ASCII.GetBytes("X")));

        var pipeline = NewPipeline(new FakeKeyProvider(commonKey: null), out _);
        pipeline.Enqueue(folder);
        var terminal = await WaitForTerminalAsync(pipeline, TitleHex);

        Assert.AreEqual(WiiUPhase.Error, terminal.Phase);
        StringAssert.Contains(terminal.Message, "keys required");
        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "decrypted")), "no decryption should occur without keys");
        Assert.IsFalse(pipeline.IsConverted(TitleHex));
    }

    [TestMethod]
    public async Task MissingMetadata_Errors()
    {
        using var tmp = new TempDirectory("WiiUPipeBad");
        var folder = Path.Combine(tmp.Root, "completed", "wiiu", TitleHex);
        Directory.CreateDirectory(folder); // empty — no title.tmd/title.tik

        var pipeline = NewPipeline(new FakeKeyProvider(Key(0xA5)), out _);
        pipeline.Enqueue(folder);
        var terminal = await WaitForTerminalAsync(pipeline, TitleHex);

        Assert.AreEqual(WiiUPhase.Error, terminal.Phase);
        StringAssert.Contains(terminal.Message, "Missing");
    }

    [TestMethod]
    public void BuildFlow_FourSteps_StatusesMapByPhase()
    {
        var pipeline = NewPipeline(new FakeKeyProvider(Key(0xA5)), out _);

        var decrypting = pipeline.BuildFlow(WiiUPhase.Decrypting, "Decrypting…", true)!;
        Assert.AreEqual("wiiu", decrypting.PipelineType);
        Assert.HasCount(4, decrypting.Steps);
        Assert.AreEqual("Fetch", decrypting.Steps[0].Name);
        Assert.AreEqual("done", decrypting.Steps[0].Status);     // Fetch precedes Decrypt
        Assert.AreEqual("active", decrypting.Steps[1].Status);   // Decrypt is current
        Assert.AreEqual("pending", decrypting.Steps[2].Status);  // Extract not reached
        CollectionAssert.Contains(decrypting.Actions, "abort");

        var done = pipeline.BuildFlow(WiiUPhase.Done, "Decrypted", true)!;
        Assert.IsTrue(done.Steps.All(s => s.Status == "done"));

        var error = pipeline.BuildFlow(WiiUPhase.Error, "boom", true)!;
        CollectionAssert.Contains(error.Actions, "retry");
        Assert.AreEqual("error", error.Steps[^1].Status);

        Assert.IsNull(pipeline.BuildFlow(null, null, fileExists: false));
    }

    [TestMethod]
    public void CheckDuplicate_DecryptedFolder_IsDuplicate()
    {
        using var tmp = new TempDirectory("WiiUPipeDup");
        var console = Path.Combine(tmp.Root, "wiiu");
        var titleDir = Path.Combine(console, TitleHex);
        Directory.CreateDirectory(Path.Combine(titleDir, WiiUConversionPipeline.DecryptedDirName));
        File.WriteAllText(Path.Combine(titleDir, "title.tmd"), "x");

        var pipeline = NewPipeline(new FakeKeyProvider(null), out _);
        var dup = pipeline.CheckDuplicate(console, TitleHex, null, "done");
        Assert.IsNotNull(dup);
        StringAssert.Contains(dup.Reason, "decrypted");

        // Nothing on disk → not a duplicate.
        Assert.IsNull(pipeline.CheckDuplicate(console, "0005000099999999", null, null));
    }

    [TestMethod]
    public void MarkConverted_MarksItem()
    {
        var pipeline = NewPipeline(new FakeKeyProvider(null), out _);
        Assert.IsFalse(pipeline.IsConverted(TitleHex));
        pipeline.MarkConverted(TitleHex);
        Assert.IsTrue(pipeline.IsConverted(TitleHex));
    }
}
