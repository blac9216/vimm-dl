// Tmd.cs
//
// Clean-room parser for the Wii U TMD (Title Metadata) — a signed blob followed by a big-endian header
// and per-content chunk records.
//
// Implemented solely from public specs: wiiubrew "Title metadata". Offsets below are relative to the
// start of the header (i.e. after the signed-blob prefix, see WiiUSignature):
//   header + 0x04C  u64  Title ID
//   header + 0x09C  u16  Title version
//   header + 0x09E  u16  Number of contents
//   header + 0x0A0  u16  Boot index
//   header + 0x0A4  64 x 0x24  Content info records (group hashes — skipped here)
//   header + 0x9A4  N  x 0x24  Content chunk records:
//                     +0x00 u32 Content ID, +0x04 u16 Index, +0x06 u16 Type,
//                     +0x08 u64 Size, +0x10 20-byte SHA-1 hash
// No GPL source was read, copied, or transliterated.

using System.Buffers.Binary;
using Module.Core;

namespace Module.WiiUTools;

/// <summary>One content (.app) entry from a TMD's chunk records.</summary>
public sealed record TmdContent(uint ContentId, ushort Index, ushort Type, ulong Size, byte[] Sha1Hash)
{
    /// <summary>wiiubrew: type bit 0x0001 marks encrypted content (the common case for NUS).</summary>
    public bool IsEncrypted => (Type & 0x0001) != 0;

    /// <summary>wiiubrew: type bit 0x0002 marks hashed content — it has a companion <c>.h3</c> file.</summary>
    public bool IsHashed => (Type & 0x0002) != 0;

    /// <summary>Lowercase hex content id, as used in NUS paths (e.g. <c>00000001</c>).</summary>
    public string ContentIdHex => ContentId.ToString("x8");
}

/// <summary>Parsed Wii U title metadata: title identity plus the list of content chunk records.</summary>
public sealed class Tmd
{
    // Header field offsets (relative to the start of the header, after the signed blob).
    private const int TitleIdOffset = 0x04C;
    private const int TitleVersionOffset = 0x09C;
    private const int ContentCountOffset = 0x09E;
    private const int BootIndexOffset = 0x0A0;
    private const int ContentRecordsOffset = 0x9A4;
    private const int ContentRecordSize = 0x24;
    private const int HashSize = 20;

    public ulong TitleId { get; private init; }
    public ushort TitleVersion { get; private init; }
    public ushort BootIndex { get; private init; }
    public IReadOnlyList<TmdContent> Contents { get; private init; } = [];

    /// <summary>Title ID as the 16-hex-character string used in NUS paths and key lookups.</summary>
    public string TitleIdHex => TitleId.ToString("X16");

    private Tmd() { }

    public static Result<Tmd> Parse(ReadOnlySpan<byte> data)
    {
        var bodyOffset = WiiUSignature.BodyOffset(data);
        if (bodyOffset < 0)
            return Result<Tmd>.Fail("TMD: missing or unknown signature type.");
        if (data.Length < bodyOffset + ContentRecordsOffset)
            return Result<Tmd>.Fail($"TMD: truncated header ({data.Length} bytes).");

        var header = data[bodyOffset..];
        var contentCount = BinaryPrimitives.ReadUInt16BigEndian(header[ContentCountOffset..]);

        var recordsStart = ContentRecordsOffset;
        var needed = bodyOffset + recordsStart + contentCount * ContentRecordSize;
        if (data.Length < needed)
            return Result<Tmd>.Fail(
                $"TMD: truncated content records (need {needed} bytes for {contentCount} contents, have {data.Length}).");

        var contents = new TmdContent[contentCount];
        for (var i = 0; i < contentCount; i++)
        {
            var rec = header[(recordsStart + i * ContentRecordSize)..];
            var id = BinaryPrimitives.ReadUInt32BigEndian(rec);
            var index = BinaryPrimitives.ReadUInt16BigEndian(rec[0x04..]);
            var type = BinaryPrimitives.ReadUInt16BigEndian(rec[0x06..]);
            var size = BinaryPrimitives.ReadUInt64BigEndian(rec[0x08..]);
            var sha1 = rec.Slice(0x10, HashSize).ToArray();
            contents[i] = new TmdContent(id, index, type, size, sha1);
        }

        return Result<Tmd>.Ok(new Tmd
        {
            TitleId = BinaryPrimitives.ReadUInt64BigEndian(header[TitleIdOffset..]),
            TitleVersion = BinaryPrimitives.ReadUInt16BigEndian(header[TitleVersionOffset..]),
            BootIndex = BinaryPrimitives.ReadUInt16BigEndian(header[BootIndexOffset..]),
            Contents = contents,
        });
    }
}
