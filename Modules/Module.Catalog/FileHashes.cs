using System.Security.Cryptography;

namespace Module.Catalog;

/// <summary>
/// Streaming CRC32 + MD5 + SHA1 of a stream in a single pass — the hash triple No-Intro/Redump DATs
/// (and Vimm) key on, so a local file can be matched to a <c>catalog_rom</c> by content regardless of
/// its filename or format (Phase C / C3 owned-by-hash). AOT-safe (managed CRC32 + IncrementalHash).
/// CRC32 is 8-char uppercase hex (DAT form); MD5/SHA1 are lowercase hex.
/// </summary>
public static class FileHashes
{
    /// <summary>A file's content hashes; any field may be null when only a subset is available.</summary>
    public readonly record struct Hashes(string? Crc, string? Md5, string? Sha1);

    /// <summary>Compute CRC32, MD5 and SHA1 over the stream in one read pass.</summary>
    public static Hashes ComputeAll(Stream stream)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var crc = Crc32.Begin();

        var buffer = new byte[1 << 16];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var span = buffer.AsSpan(0, read);
            crc = Crc32.Append(crc, span);
            md5.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
        }

        return new Hashes(
            Crc32.Finish(crc),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant());
    }
}
