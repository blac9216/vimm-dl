// Fst.cs
//
// Clean-room parser for the Wii U FST (file system table) that lives in a title's meta content and maps
// the title's content sections to files. Enumerates the sections and the file/dir entries so the pipeline
// can place each decrypted file.
//
// Implemented solely from public specs: retroreversing "Wii U File formats" / wiiubrew. Layout:
//   header (0x20):
//     0x00 u32 magic = 0x46535400 ('FST\0')
//     0x04 u32 offset factor (block size; multiply a file's offset by this for its byte offset)
//     0x08 u32 number of sections (secondary headers)
//   secondary headers (0x20 each):
//     +0x00 u32 offset, +0x04 u32 size, +0x08 u64 owner title id, +0x10 u32 group id, +0x14 u8 hash mode
//   entry table (0x10 each), after the secondary headers:
//     +0x00 u8  type (bit 0: 1 = directory)
//     +0x01 u24 name offset (into the string pool)
//     +0x04 u32 offset (file: data offset in offset-factor units; dir: parent index)
//     +0x08 u32 size   (file: byte size; dir: index of the first entry past this dir)
//     +0x0C u16 flags
//     +0x0E u16 secondary index (which content section)
//   The root entry (index 0) is a directory whose size is the total entry count; the string pool follows
//   the entry table. No GPL source was read, copied, or transliterated.

using System.Buffers.Binary;
using Module.Core;

namespace Module.WiiUTools;

/// <summary>A content section ("secondary header") the FST maps files into.</summary>
public sealed record FstSection(uint Offset, uint Size, ulong OwnerTitleId, uint GroupId);

/// <summary>
/// One FST entry. For files, <see cref="Offset"/> is in <see cref="Fst.OffsetFactor"/> units (byte
/// offset = <c>Offset * OffsetFactor</c>); <see cref="SecondaryIndex"/> selects the content section.
/// </summary>
public sealed record FstEntry(string Path, bool IsDirectory, uint Offset, uint Size, ushort SecondaryIndex);

/// <summary>A parsed Wii U FST: the offset factor, content sections, and flattened file/dir entries.</summary>
public sealed class Fst
{
    private const uint Magic = 0x46535400;
    private const int HeaderSize = 0x20;
    private const int SectionSize = 0x20;
    private const int EntrySize = 0x10;

    public uint OffsetFactor { get; private init; }
    public IReadOnlyList<FstSection> Sections { get; private init; } = [];
    public IReadOnlyList<FstEntry> Entries { get; private init; } = [];

    private Fst() { }

    public static Result<Fst> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            return Result<Fst>.Fail("FST: truncated header.");
        if (BinaryPrimitives.ReadUInt32BigEndian(data) != Magic)
            return Result<Fst>.Fail("FST: bad magic.");

        var offsetFactor = BinaryPrimitives.ReadUInt32BigEndian(data[0x04..]);
        var sectionCount = BinaryPrimitives.ReadUInt32BigEndian(data[0x08..]);

        var entryTableStart = HeaderSize + (long)sectionCount * SectionSize;
        if (entryTableStart + EntrySize > data.Length)
            return Result<Fst>.Fail($"FST: section table out of range ({sectionCount} sections).");

        var sections = new FstSection[sectionCount];
        for (var i = 0; i < sectionCount; i++)
        {
            var off = HeaderSize + i * SectionSize;
            sections[i] = new FstSection(
                BinaryPrimitives.ReadUInt32BigEndian(data[off..]),
                BinaryPrimitives.ReadUInt32BigEndian(data[(off + 0x04)..]),
                BinaryPrimitives.ReadUInt64BigEndian(data[(off + 0x08)..]),
                BinaryPrimitives.ReadUInt32BigEndian(data[(off + 0x10)..]));
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(data[(int)(entryTableStart + 0x08)..]);
        if (entryCount == 0)
            return Result<Fst>.Fail("FST: zero entries.");

        var entriesEnd = entryTableStart + (long)entryCount * EntrySize;
        if (entriesEnd > data.Length)
            return Result<Fst>.Fail($"FST: entry table out of range ({entryCount} entries).");
        var stringPoolStart = (int)entriesEnd;

        var entries = new List<FstEntry>((int)entryCount - 1);
        var dirStack = new List<(string Name, uint End)> { ("", entryCount) };

        for (uint i = 1; i < entryCount; i++)
        {
            while (dirStack.Count > 1 && i >= dirStack[^1].End)
                dirStack.RemoveAt(dirStack.Count - 1);

            var off = (int)entryTableStart + (int)i * EntrySize;
            var type = data[off];
            var nameOffset = SpanRead.U24BigEndian(data, off + 1);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(data[(off + 0x04)..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(data[(off + 0x08)..]);
            var secondaryIndex = BinaryPrimitives.ReadUInt16BigEndian(data[(off + 0x0E)..]);

            var name = SpanRead.CString(data, stringPoolStart + (int)nameOffset);
            var dirPath = string.Join('/', dirStack.Skip(1).Select(d => d.Name));
            var path = dirPath.Length == 0 ? name : $"{dirPath}/{name}";

            var isDir = (type & 1) != 0;
            if (isDir)
            {
                entries.Add(new FstEntry(path, true, 0, 0, secondaryIndex));
                dirStack.Add((name, size));
            }
            else
            {
                entries.Add(new FstEntry(path, false, offset, size, secondaryIndex));
            }
        }

        return Result<Fst>.Ok(new Fst { OffsetFactor = offsetFactor, Sections = sections, Entries = entries });
    }
}
