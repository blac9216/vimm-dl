// CertChain.cs
//
// Clean-room, minimal reader for a Wii U certificate chain (the `cert`/cetk certs). Structure only —
// it enumerates the certificates and their identity so the chain can be carried/inspected; signature
// verification is intentionally out of scope (best-effort, not required for decryption).
//
// Implemented solely from public specs: wiibrew "Certificate chain". Each certificate is a signed blob
// (see WiiUSignature) followed by a body:
//   body + 0x00  0x40  Issuer
//   body + 0x40  u32   Public key type (0 = RSA-4096, 1 = RSA-2048, 2 = ECC-B233)
//   body + 0x44  0x40  Name
//   body + 0x84  u32   (key id / expiry)
//   body + 0x88  public key (sized by key type: RSA-4096 0x238, RSA-2048 0x138, ECC 0x78)
// No GPL source was read, copied, or transliterated.

using System.Buffers.Binary;
using System.Text;
using Module.Core;

namespace Module.WiiUTools;

/// <summary>One certificate's identity (structure only — no key material or signature checking).</summary>
public sealed record WiiUCert(string Issuer, string Name, uint KeyType, int TotalSize);

/// <summary>A parsed certificate chain: the ordered list of certificates it contains.</summary>
public sealed class CertChain
{
    private const int IssuerOffset = 0x00;
    private const int KeyTypeOffset = 0x40;
    private const int NameOffset = 0x44;
    private const int NameSize = 0x40;
    private const int PublicKeyOffset = 0x88;

    public IReadOnlyList<WiiUCert> Certs { get; private init; } = [];

    private CertChain() { }

    // Public-key section size (incl. padding) by key type — determines how far to advance to the next cert.
    private static int PublicKeySize(uint keyType) => keyType switch
    {
        0 => 0x238, // RSA-4096: 0x200 modulus + 0x4 exponent + 0x34 padding
        1 => 0x138, // RSA-2048: 0x100 modulus + 0x4 exponent + 0x34 padding
        2 => 0x78,  // ECC-B233: 0x3C key      + 0x3C padding
        _ => -1,
    };

    public static Result<CertChain> Parse(ReadOnlySpan<byte> data)
    {
        var certs = new List<WiiUCert>();
        var offset = 0;
        while (offset < data.Length)
        {
            var remaining = data[offset..];
            var bodyOffset = WiiUSignature.BodyOffset(remaining);
            if (bodyOffset < 0)
                break; // nothing parseable here — empty input, or trailing bytes after the last cert
            if (remaining.Length < bodyOffset + PublicKeyOffset)
                return Result<CertChain>.Fail($"Cert chain: truncated certificate at offset {offset}.");

            var body = remaining[bodyOffset..];
            var keyType = BinaryPrimitives.ReadUInt32BigEndian(body[KeyTypeOffset..]);
            var keySize = PublicKeySize(keyType);
            if (keySize < 0)
                return Result<CertChain>.Fail($"Cert chain: unknown public key type {keyType} at offset {offset}.");

            var totalSize = bodyOffset + PublicKeyOffset + keySize;
            if (remaining.Length < totalSize)
                return Result<CertChain>.Fail($"Cert chain: truncated certificate body at offset {offset}.");

            var issuer = ReadString(body.Slice(IssuerOffset, NameSize));
            var name = ReadString(body.Slice(NameOffset, NameSize));
            certs.Add(new WiiUCert(issuer, name, keyType, totalSize));
            offset += totalSize;
        }

        if (certs.Count == 0)
            return Result<CertChain>.Fail("Cert chain: no valid certificate found.");
        return Result<CertChain>.Ok(new CertChain { Certs = certs });
    }

    private static string ReadString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? bytes[..end] : bytes);
    }
}
