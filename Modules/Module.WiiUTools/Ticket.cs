// Ticket.cs
//
// Clean-room parser for the Wii U ticket (cetk) — a signed blob followed by the ticket body that carries
// the encrypted title key and the title ID.
//
// Implemented solely from public specs: wiibrew "Ticket". Offsets below are relative to the start of the
// body (i.e. after the signed-blob prefix, see WiiUSignature):
//   body + 0x040  0x3C  ECDH data
//   body + 0x07C  1     ticket format version
//   body + 0x07F  0x10  Encrypted title key
//   body + 0x09C  u64   Title ID
//   body + 0x0B1  1     Common key index
// No GPL source was read, copied, or transliterated. The decrypted title key is produced by pairing the
// encrypted key here with the common key from ITitleKeyProvider (see WiiUCrypto.DecryptTitleKey).

using System.Buffers.Binary;
using Module.Core;

namespace Module.WiiUTools;

/// <summary>Parsed Wii U ticket: the encrypted title key plus the title identity it belongs to.</summary>
public sealed class Ticket
{
    // Body field offsets (relative to the start of the body, after the signed blob).
    private const int EncryptedTitleKeyOffset = 0x07F;
    private const int TitleIdOffset = 0x09C;
    private const int CommonKeyIndexOffset = 0x0B1;
    private const int KeySize = 16;
    private const int MinBodyLength = CommonKeyIndexOffset + 1;

    public ulong TitleId { get; private init; }

    /// <summary>The 16-byte AES-128-CBC-encrypted title key (decrypt with the common key).</summary>
    public byte[] EncryptedTitleKey { get; private init; } = [];

    /// <summary>Which common key this ticket's title key is encrypted under (Wii U retail is index 0).</summary>
    public byte CommonKeyIndex { get; private init; }

    /// <summary>Title ID as the 16-hex-character string used in NUS paths and key lookups.</summary>
    public string TitleIdHex => TitleId.ToString("X16");

    private Ticket() { }

    public static Result<Ticket> Parse(ReadOnlySpan<byte> data)
    {
        var bodyOffset = WiiUSignature.BodyOffset(data);
        if (bodyOffset < 0)
            return Result<Ticket>.Fail("Ticket: missing or unknown signature type.");
        if (data.Length < bodyOffset + MinBodyLength)
            return Result<Ticket>.Fail($"Ticket: truncated body ({data.Length} bytes).");

        var body = data[bodyOffset..];
        return Result<Ticket>.Ok(new Ticket
        {
            EncryptedTitleKey = body.Slice(EncryptedTitleKeyOffset, KeySize).ToArray(),
            TitleId = BinaryPrimitives.ReadUInt64BigEndian(body[TitleIdOffset..]),
            CommonKeyIndex = body[CommonKeyIndexOffset],
        });
    }
}
