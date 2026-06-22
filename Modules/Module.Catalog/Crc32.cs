namespace Module.Catalog;

/// <summary>
/// CRC32 (IEEE, poly 0xEDB88320) — the checksum No-Intro/Redump DATs key on. Pure managed,
/// AOT-safe, no external package. Produces 8-char uppercase hex matching the DAT <c>crc</c> form.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>Stream a source and return its CRC32 as 8-char uppercase hex.</summary>
    public static string ComputeHex(Stream stream)
    {
        uint crc = 0xFFFFFFFF;
        Span<byte> buffer = stackalloc byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            for (int i = 0; i < read; i++)
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        return ToHex(crc ^ 0xFFFFFFFF);
    }

    /// <summary>Format a raw CRC32 value (e.g. from a zip central directory) as 8-char uppercase hex.</summary>
    public static string ToHex(uint crc) => crc.ToString("X8");

    // Incremental API — lets callers fold CRC32 into a single streaming pass alongside other hashes
    // (e.g. FileHashes computing CRC32 + MD5 + SHA1 in one read) without exposing the lookup table.

    /// <summary>Start an incremental CRC32 accumulation.</summary>
    public static uint Begin() => 0xFFFFFFFF;

    /// <summary>Fold a chunk of bytes into a running CRC32 from <see cref="Begin"/>.</summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>Finalize a running CRC32 to 8-char uppercase hex.</summary>
    public static string Finish(uint crc) => ToHex(crc ^ 0xFFFFFFFF);
}
