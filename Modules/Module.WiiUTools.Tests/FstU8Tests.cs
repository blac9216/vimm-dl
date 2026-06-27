using System.Buffers.Binary;
using System.Text;

namespace Module.WiiUTools.Tests;

/// <summary>
/// Synthetic-fixture tests for the U8 and FST parsers. The builders hard-code the public-spec layout
/// (wiiubrew "U8 archive", retroreversing "Wii U File formats") independently of the parsers, so a drifted
/// offset fails the round-trip. No real Wii U data is used.
/// </summary>
[TestClass]
public class FstU8Tests
{
    static void WriteU24(Span<byte> b, int offset, int value)
    {
        b[offset] = (byte)(value >> 16);
        b[offset + 1] = (byte)(value >> 8);
        b[offset + 2] = (byte)value;
    }

    // ---- U8 ----

    static byte[] BuildU8((byte type, string name, uint dataOff, uint size)[] nodes)
    {
        const int rootNodeOffset = 0x20;
        var strBytes = new List<byte>();
        var nameOffsets = new int[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            nameOffsets[i] = strBytes.Count;
            strBytes.AddRange(Encoding.ASCII.GetBytes(nodes[i].name));
            strBytes.Add(0);
        }
        var nodeTableSize = nodes.Length * 12;
        var stringTableStart = rootNodeOffset + nodeTableSize;
        var buf = new byte[stringTableStart + strBytes.Count];

        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x55AA382D);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x04), rootNodeOffset);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x08), (uint)(nodeTableSize + strBytes.Count));
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x0C), (uint)buf.Length);
        for (var i = 0; i < nodes.Length; i++)
        {
            var off = rootNodeOffset + i * 12;
            buf[off] = nodes[i].type;
            WriteU24(buf, off + 1, nameOffsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 4), nodes[i].dataOff);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 8), nodes[i].size);
        }
        strBytes.ToArray().CopyTo(buf, stringTableStart);
        return buf;
    }

    [TestMethod]
    public void U8_Parse_NestedDir_EnumeratesWithPaths()
    {
        var u8 = BuildU8(
        [
            (1, "", 0, 4),            // root, total node count = 4
            (1, "content", 0, 4),     // dir, exclusive end index = 4 (children: nodes 2,3)
            (0, "a.txt", 0x100, 10),
            (0, "b.bin", 0x200, 20),
        ]);

        var r = U8Archive.Parse(u8);
        Assert.IsTrue(r.IsOk, r.Error);
        var e = r.Value!.Entries;
        Assert.HasCount(3, e); // excludes the root

        Assert.AreEqual("content", e[0].Path);
        Assert.IsTrue(e[0].IsDirectory);
        Assert.AreEqual("content/a.txt", e[1].Path);
        Assert.IsFalse(e[1].IsDirectory);
        Assert.AreEqual(0x100u, e[1].Offset);
        Assert.AreEqual(10u, e[1].Size);
        Assert.AreEqual("content/b.bin", e[2].Path);
        Assert.AreEqual(0x200u, e[2].Offset);
        Assert.AreEqual(20u, e[2].Size);
    }

    [TestMethod]
    public void U8_Parse_RootFileAndSubdir_PathsCorrect()
    {
        var u8 = BuildU8(
        [
            (1, "", 0, 4),               // root, count = 4
            (0, "top.bin", 0x50, 5),     // file at root
            (1, "sub", 0, 4),            // dir, end = 4 (child: node 3)
            (0, "inner.dat", 0x60, 6),   // file inside sub
        ]);

        var e = U8Archive.Parse(u8).Value!.Entries;
        Assert.HasCount(3, e);
        Assert.AreEqual("top.bin", e[0].Path);
        Assert.AreEqual("sub", e[1].Path);
        Assert.IsTrue(e[1].IsDirectory);
        Assert.AreEqual("sub/inner.dat", e[2].Path);
        Assert.AreEqual(0x60u, e[2].Offset);
        Assert.AreEqual(6u, e[2].Size);
    }

    [TestMethod]
    public void U8_Parse_SiblingAfterNestedDir_PopsBackToRoot()
    {
        // A root-level file *after* a nested directory's children forces the directory-stack to pop
        // (dirA's exclusive end = 3, reached at node 3), so top.bin must land at the root, not under dirA.
        var u8 = BuildU8(
        [
            (1, "", 0, 4),              // root, count = 4
            (1, "dirA", 0, 3),          // dir, exclusive end = 3 (child: node 2 only)
            (0, "child.bin", 0x100, 5), // file inside dirA
            (0, "top.bin", 0x200, 6),   // root-level sibling after dirA's children → pop
        ]);

        var e = U8Archive.Parse(u8).Value!.Entries;
        Assert.HasCount(3, e);
        Assert.AreEqual("dirA", e[0].Path);
        Assert.IsTrue(e[0].IsDirectory);
        Assert.AreEqual("dirA/child.bin", e[1].Path);
        Assert.AreEqual("top.bin", e[2].Path); // popped back to root — no "dirA/" prefix
        Assert.IsFalse(e[2].IsDirectory);
        Assert.AreEqual(0x200u, e[2].Offset);
    }

    [TestMethod]
    public void U8_Parse_BadMagic_Fails()
    {
        var buf = new byte[0x40];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0xDEADBEEF);
        var r = U8Archive.Parse(buf);
        Assert.IsFalse(r.IsOk);
        StringAssert.Contains(r.Error, "magic");
    }

    [TestMethod]
    public void U8_Parse_TruncatedHeader_Fails() => Assert.IsFalse(U8Archive.Parse(new byte[8]).IsOk);

    [TestMethod]
    public void U8_Parse_NodeTableOutOfRange_Fails()
    {
        var buf = new byte[0x20 + 12]; // room for root node only
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x55AA382D);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x04), 0x20);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x20 + 8), 999); // claims 999 nodes
        Assert.IsFalse(U8Archive.Parse(buf).IsOk);
    }

    // ---- FST ----

    static byte[] BuildFst(uint offsetFactor,
        (uint off, uint size, ulong owner, uint group)[] sections,
        (byte type, string name, uint off, uint size, ushort secIdx)[] entries)
    {
        const int headerSize = 0x20;
        var sectionsSize = sections.Length * 0x20;
        var entryTableStart = headerSize + sectionsSize;
        var strBytes = new List<byte>();
        var nameOffsets = new int[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            nameOffsets[i] = strBytes.Count;
            strBytes.AddRange(Encoding.ASCII.GetBytes(entries[i].name));
            strBytes.Add(0);
        }
        var entryTableSize = entries.Length * 0x10;
        var stringPoolStart = entryTableStart + entryTableSize;
        var buf = new byte[stringPoolStart + strBytes.Count];

        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x46535400);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x04), offsetFactor);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x08), (uint)sections.Length);
        for (var i = 0; i < sections.Length; i++)
        {
            var off = headerSize + i * 0x20;
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off), sections[i].off);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 0x04), sections[i].size);
            BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(off + 0x08), sections[i].owner);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 0x10), sections[i].group);
        }
        for (var i = 0; i < entries.Length; i++)
        {
            var off = entryTableStart + i * 0x10;
            buf[off] = entries[i].type;
            WriteU24(buf, off + 1, nameOffsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 0x04), entries[i].off);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(off + 0x08), entries[i].size);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(off + 0x0E), entries[i].secIdx);
        }
        strBytes.ToArray().CopyTo(buf, stringPoolStart);
        return buf;
    }

    [TestMethod]
    public void Fst_Parse_SectionsAndEntries()
    {
        var fst = BuildFst(0x20,
            sections: [(0x0u, 0x1000u, 0x0005000010123400UL, 7u)],
            entries:
            [
                (1, "", 0, 3, 0),               // root, entry count = 3
                (0, "code", 0x10, 0x800, 0),
                (0, "content", 0x20, 0x400, 0),
            ]);

        var r = Fst.Parse(fst);
        Assert.IsTrue(r.IsOk, r.Error);
        var f = r.Value!;
        Assert.AreEqual(0x20u, f.OffsetFactor);

        Assert.HasCount(1, f.Sections);
        Assert.AreEqual(0x1000u, f.Sections[0].Size);
        Assert.AreEqual(0x0005000010123400UL, f.Sections[0].OwnerTitleId);
        Assert.AreEqual(7u, f.Sections[0].GroupId);

        Assert.HasCount(2, f.Entries); // excludes the root
        Assert.AreEqual("code", f.Entries[0].Path);
        Assert.IsFalse(f.Entries[0].IsDirectory);
        Assert.AreEqual(0x10u, f.Entries[0].Offset);
        Assert.AreEqual(0x800u, f.Entries[0].Size);
        Assert.AreEqual("content", f.Entries[1].Path);
        Assert.AreEqual(0x400u, f.Entries[1].Size);
    }

    [TestMethod]
    public void Fst_Parse_NestedDir_PathsAndSection()
    {
        var fst = BuildFst(0x8000,
            sections: [(0u, 0u, 0UL, 0u), (0x100u, 0x2000u, 0x0005000010ABCDEFUL, 1u)],
            entries:
            [
                (1, "", 0, 4, 0),                  // root, count = 4
                (1, "data", 0, 4, 0),              // dir, end = 4
                (0, "movie.bin", 0x5, 0x1000, 1),  // file in section 1
                (0, "map.dat", 0x9, 0x200, 1),
            ]);

        var f = Fst.Parse(fst).Value!;
        Assert.AreEqual(0x8000u, f.OffsetFactor);
        Assert.HasCount(2, f.Sections);
        Assert.HasCount(3, f.Entries);
        Assert.AreEqual("data", f.Entries[0].Path);
        Assert.IsTrue(f.Entries[0].IsDirectory);
        Assert.AreEqual("data/movie.bin", f.Entries[1].Path);
        Assert.AreEqual((ushort)1, f.Entries[1].SecondaryIndex);
        Assert.AreEqual("data/map.dat", f.Entries[2].Path);
    }

    [TestMethod]
    public void Fst_Parse_SiblingAfterNestedDir_PopsBackToRoot()
    {
        // A root-level entry after a nested directory's children forces the directory-stack to pop
        // (dirA's exclusive end = 3, reached at entry 3), so root.bin must land at the root, not under dirA.
        var fst = BuildFst(0x20,
            sections: [(0u, 0u, 0UL, 0u)],
            entries:
            [
                (1, "", 0, 4, 0),                  // root, count = 4
                (1, "dirA", 0, 3, 0),              // dir, exclusive end = 3 (child: entry 2 only)
                (0, "child.bin", 0x4, 0x100, 0),   // file inside dirA
                (0, "root.bin", 0x8, 0x200, 0),    // root-level sibling after dirA's children → pop
            ]);

        var f = Fst.Parse(fst).Value!;
        Assert.HasCount(3, f.Entries);
        Assert.AreEqual("dirA", f.Entries[0].Path);
        Assert.IsTrue(f.Entries[0].IsDirectory);
        Assert.AreEqual("dirA/child.bin", f.Entries[1].Path);
        Assert.AreEqual("root.bin", f.Entries[2].Path); // popped back to root — no "dirA/" prefix
        Assert.IsFalse(f.Entries[2].IsDirectory);
    }

    [TestMethod]
    public void Fst_Parse_BadMagic_Fails()
    {
        var buf = new byte[0x40];
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x12345678);
        Assert.IsFalse(Fst.Parse(buf).IsOk);
    }

    [TestMethod]
    public void Fst_Parse_Empty_Fails() => Assert.IsFalse(Fst.Parse([]).IsOk);

    [TestMethod]
    public void Fst_Parse_EntryTableOutOfRange_Fails()
    {
        var buf = new byte[0x20 + 0x10]; // header + room for the root entry only
        BinaryPrimitives.WriteUInt32BigEndian(buf, 0x46535400);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x08), 0); // no sections
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0x20 + 0x08), 500); // claims 500 entries
        Assert.IsFalse(Fst.Parse(buf).IsOk);
    }
}
