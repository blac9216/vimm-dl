// WiiUCrypto.cs
//
// Clean-room AES-128-CBC crypto core for Wii U title-key and content decryption.
//
// Implemented solely from public specifications:
//   - wiiubrew, "Encryption keys" — common-key usage; the title-key IV is the title ID
//     padded with zeros to 16 bytes.
//   - wiiubrew, "Title metadata"  — content layout / index; the content IV is the content
//     index padded with zeros to 16 bytes.
//   - wiibrew,  "Ticket"          — the 16-byte encrypted title key carried by a ticket.
//
// No GPL-licensed source (e.g. WiiUDownloader) was read, copied, or transliterated; the
// project stays MIT. Key material is NEVER embedded here or anywhere in the repository —
// callers supply it at runtime (see ITitleKeyProvider). Every function operates purely
// over byte buffers / streams so it is unit-testable with throwaway synthetic keys.

using System.Buffers.Binary;
using System.Security.Cryptography;
using Module.Core;

namespace Module.WiiUTools;

/// <summary>
/// Pure AES-128-CBC primitives for Wii U decryption. Stateless; no embedded keys.
/// </summary>
public static class WiiUCrypto
{
    /// <summary>AES-128 key length, in bytes.</summary>
    public const int KeySize = 16;

    /// <summary>AES block size, in bytes.</summary>
    public const int BlockSize = 16;

    // ---- IV derivation (clean-room, per wiiubrew) ----

    /// <summary>
    /// IV for decrypting a ticket's title key: the 8-byte big-endian title ID followed by
    /// 8 zero bytes (wiiubrew "Encryption keys"). Exposed internally for round-trip tests.
    /// </summary>
    internal static byte[] TitleKeyIv(ulong titleId)
    {
        var iv = new byte[BlockSize];
        BinaryPrimitives.WriteUInt64BigEndian(iv, titleId);
        return iv;
    }

    /// <summary>
    /// IV for decrypting a content (.app): the 2-byte big-endian content index followed by
    /// 14 zero bytes (wiiubrew "Title metadata"). Exposed internally for round-trip tests.
    /// </summary>
    internal static byte[] ContentIv(ushort contentIndex)
    {
        var iv = new byte[BlockSize];
        BinaryPrimitives.WriteUInt16BigEndian(iv, contentIndex);
        return iv;
    }

    // ---- Title key ----

    /// <summary>
    /// Decrypt a ticket's 16-byte encrypted title key under the 16-byte common key —
    /// AES-128-CBC, IV derived from the title ID — yielding the 16-byte title key.
    /// </summary>
    public static Result<byte[]> DecryptTitleKey(ReadOnlySpan<byte> encryptedTitleKey, ulong titleId, ReadOnlySpan<byte> commonKey)
    {
        if (commonKey.Length != KeySize)
            return Result<byte[]>.Fail($"Common key must be {KeySize} bytes, got {commonKey.Length}.");
        if (encryptedTitleKey.Length != KeySize)
            return Result<byte[]>.Fail($"Encrypted title key must be {KeySize} bytes, got {encryptedTitleKey.Length}.");

        try
        {
            using var aes = Aes.Create();
            aes.Key = commonKey.ToArray();
            return Result<byte[]>.Ok(aes.DecryptCbc(encryptedTitleKey, TitleKeyIv(titleId), PaddingMode.None));
        }
        catch (CryptographicException ex)
        {
            return Result<byte[]>.Fail($"Title key decryption failed: {ex.Message}");
        }
    }

    /// <summary>Internal inverse of <see cref="DecryptTitleKey"/> — used only by tests to forge synthetic tickets.</summary>
    internal static byte[] EncryptTitleKey(ReadOnlySpan<byte> titleKey, ulong titleId, ReadOnlySpan<byte> commonKey)
    {
        using var aes = Aes.Create();
        aes.Key = commonKey.ToArray();
        return aes.EncryptCbc(titleKey, TitleKeyIv(titleId), PaddingMode.None);
    }

    // ---- Content ----

    /// <summary>
    /// Decrypt an in-memory content buffer under the title key — AES-128-CBC, IV derived
    /// from the content index. The ciphertext length must be a multiple of the AES block
    /// size. For multi-GB content, prefer <see cref="DecryptContentAsync"/>.
    /// </summary>
    public static Result<byte[]> DecryptContent(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> titleKey, ushort contentIndex)
    {
        if (titleKey.Length != KeySize)
            return Result<byte[]>.Fail($"Title key must be {KeySize} bytes, got {titleKey.Length}.");
        if (ciphertext.Length % BlockSize != 0)
            return Result<byte[]>.Fail($"Content length {ciphertext.Length} is not a multiple of the {BlockSize}-byte AES block size.");

        try
        {
            using var aes = Aes.Create();
            aes.Key = titleKey.ToArray();
            return Result<byte[]>.Ok(aes.DecryptCbc(ciphertext, ContentIv(contentIndex), PaddingMode.None));
        }
        catch (CryptographicException ex)
        {
            return Result<byte[]>.Fail($"Content decryption failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stream-decrypt a content under the title key — AES-128-CBC, IV derived from the
    /// content index — copying plaintext to <paramref name="output"/> without buffering the
    /// whole file (suitable for multi-GB content). The input length must be block-aligned.
    /// Both streams are left open for the caller to dispose.
    /// </summary>
    public static async Task<Result<bool>> DecryptContentAsync(
        Stream input, Stream output, byte[] titleKey, ushort contentIndex, CancellationToken ct = default)
    {
        if (titleKey.Length != KeySize)
            return Result.Fail($"Title key must be {KeySize} bytes, got {titleKey.Length}.");

        // Pre-validate alignment when the length is known (mirrors the buffer overload's clear error),
        // rather than relying on CryptoStream throwing a vaguer "incomplete block" at the end (#216).
        if (input.CanSeek)
        {
            var remaining = input.Length - input.Position;
            if (remaining % BlockSize != 0)
                return Result.Fail($"Content length {remaining} is not a multiple of the {BlockSize}-byte AES block size.");
        }

        try
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            using var transform = aes.CreateDecryptor(titleKey, ContentIv(contentIndex));
            using var crypto = new CryptoStream(input, transform, CryptoStreamMode.Read, leaveOpen: true);
            await crypto.CopyToAsync(output, ct);
            return Result.Ok();
        }
        catch (CryptographicException ex)
        {
            // Non-seekable, non-block-aligned input surfaces here (e.g. a network stream).
            return Result.Fail($"Content decryption failed: {ex.Message}");
        }
    }

    /// <summary>Internal inverse of content decryption — used only by tests to forge synthetic content.</summary>
    internal static byte[] EncryptContent(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> titleKey, ushort contentIndex)
    {
        using var aes = Aes.Create();
        aes.Key = titleKey.ToArray();
        return aes.EncryptCbc(plaintext, ContentIv(contentIndex), PaddingMode.None);
    }
}
