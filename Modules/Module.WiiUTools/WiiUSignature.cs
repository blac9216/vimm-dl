// WiiUSignature.cs
//
// Shared helper for the signed-blob prefix that begins a TMD, a ticket (cetk), and each certificate.
//
// Clean-room from public specs: wiibrew "Signing" / "Title metadata" / "Ticket". A signed blob starts
// with a big-endian u32 signature type; the signature bytes + padding that follow are sized by type and
// padded so the signed body that follows starts on a 0x40 boundary. No GPL source was consulted.

using System.Buffers.Binary;

namespace Module.WiiUTools;

internal static class WiiUSignature
{
    /// <summary>
    /// Total size (in bytes) of the signed-blob prefix for a signature type — i.e. the offset at which
    /// the signed body begins — or -1 for an unknown type. Values are 4 (type) + signature + padding:
    /// RSA-4096 → 0x240, RSA-2048 → 0x140, ECDSA → 0x80 (both the SHA-1 and SHA-256 variants).
    /// </summary>
    public static int BlobSize(uint signatureType) => signatureType switch
    {
        0x00010000 or 0x00010003 => 0x240, // RSA-4096 (512 sig + 60 pad + 4 type)
        0x00010001 or 0x00010004 => 0x140, // RSA-2048 (256 sig + 60 pad + 4 type)
        0x00010002 or 0x00010005 => 0x80,  // ECDSA    (60 sig  + 64 pad + 4 type)
        _ => -1,
    };

    /// <summary>
    /// Read the signature type at the start of <paramref name="data"/> and return the offset of the body
    /// (the blob size). Returns -1 if the buffer is too short or the type is unknown.
    /// </summary>
    public static int BodyOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return -1;
        return BlobSize(BinaryPrimitives.ReadUInt32BigEndian(data));
    }
}
