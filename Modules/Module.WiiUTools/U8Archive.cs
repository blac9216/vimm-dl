// U8Archive.cs
//
// Clean-room parser for the Nintendo U8 archive that appears inside decrypted Wii U content (the same
// container Wii/GameCube use). Enumerates the entries (full path, file/dir, data offset, size) so the
// pipeline can extract the real game files.
//
// Implemented solely from public specs: wiiubrew "U8 archive". Layout (offsets from the U8 start):
//   0x00 u32 magic = 0x55AA382D
//   0x04 u32 rootNodeOffset  (start of the node table; usually 0x20)
//   0x08 u32 headerSize      (node table + string table)
//   0x0C u32 dataOffset      (start of file data)
//   nodes (12 bytes each, big-endian):
//     +0x00 u8  type (0 = file, 1 = directory)
//     +0x01 u24 name offset (into the string table)
//     +0x04 u32 data offset (file: absolute offset to data; dir: parent node index)
//     +0x08 u32 size        (file: byte size; dir: index of the first node past this dir)
//   The root node (index 0) is a directory whose size is the total node count; the string table follows
//   the node table. No GPL source was read, copied, or transliterated.

using System.Buffers.Binary;
using Module.Core;

namespace Module.WiiUTools;

/// <summary>One entry in a U8 archive. <see cref="Offset"/>/<see cref="Size"/> are 0 for directories.</summary>
public sealed record U8Entry(string Path, bool IsDirectory, uint Offset, uint Size);

/// <summary>A parsed U8 archive: the flattened list of entries with full (slash-joined) paths.</summary>
public sealed class U8Archive
{
    private const uint Magic = 0x55AA382D;
    private const int NodeSize = 12;

    public IReadOnlyList<U8Entry> Entries { get; private init; } = [];

    private U8Archive() { }

    public static Result<U8Archive> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x20)
            return Result<U8Archive>.Fail("U8: truncated header.");
        if (BinaryPrimitives.ReadUInt32BigEndian(data) != Magic)
            return Result<U8Archive>.Fail("U8: bad magic.");

        var rootNodeOffset = BinaryPrimitives.ReadUInt32BigEndian(data[0x04..]);
        if (rootNodeOffset + (long)NodeSize > data.Length)
            return Result<U8Archive>.Fail("U8: root node out of range.");

        var nodeCount = BinaryPrimitives.ReadUInt32BigEndian(data[(int)(rootNodeOffset + 0x08)..]);
        if (nodeCount == 0)
            return Result<U8Archive>.Fail("U8: zero nodes.");

        var nodesEnd = rootNodeOffset + (long)nodeCount * NodeSize;
        if (nodesEnd > data.Length)
            return Result<U8Archive>.Fail($"U8: node table out of range ({nodeCount} nodes).");
        var stringTableStart = (int)nodesEnd;

        var entries = new List<U8Entry>((int)nodeCount - 1);
        // Directory stack: each level remembers its name and the exclusive end index of its children.
        var dirStack = new List<(string Name, uint End)> { ("", nodeCount) };

        for (uint i = 1; i < nodeCount; i++)
        {
            while (dirStack.Count > 1 && i >= dirStack[^1].End)
                dirStack.RemoveAt(dirStack.Count - 1);

            var nodeOff = (int)rootNodeOffset + (int)i * NodeSize;
            var type = data[nodeOff];
            var nameOffset = SpanRead.U24BigEndian(data, nodeOff + 1);
            var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data[(nodeOff + 4)..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(data[(nodeOff + 8)..]);

            var name = SpanRead.CString(data, stringTableStart + (int)nameOffset);
            var dirPath = string.Join('/', dirStack.Skip(1).Select(d => d.Name));
            var path = dirPath.Length == 0 ? name : $"{dirPath}/{name}";

            var isDir = (type & 1) != 0;
            if (isDir)
            {
                entries.Add(new U8Entry(path, true, 0, 0));
                dirStack.Add((name, size)); // size = exclusive end index of this directory's children
            }
            else
            {
                entries.Add(new U8Entry(path, false, dataOffset, size));
            }
        }

        return Result<U8Archive>.Ok(new U8Archive { Entries = entries });
    }
}
