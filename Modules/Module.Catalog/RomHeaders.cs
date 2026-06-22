namespace Module.Catalog;

/// <summary>
/// Detects the fixed-size headers some dumpers prepend to a ROM file. No-Intro stores the hash of the
/// <i>headerless</i> ROM for these systems, so a locally-headered file won't match <c>catalog_rom</c>
/// on its as-is bytes — the import path (#118 / L1) hashes the file both as-is and with the header
/// stripped, and a match on either wins. Detection is by magic bytes (not extension), so a
/// mis-named file is still handled.
///
/// <para>Known headers: iNES <c>.nes</c> ("NES\x1A", 16 bytes), FDS <c>.fds</c> ("FDS\x1A", 16 bytes),
/// Atari Lynx <c>.lnx</c> ("LYNX", 64 bytes), Atari 7800 <c>.a78</c> ("ATARI7800" at offset 1,
/// 128 bytes).</para>
/// </summary>
public static class RomHeaders
{
    /// <summary>Longest header we recognise — the caller only needs to read this many leading bytes.</summary>
    public const int MaxHeaderLength = 128;

    /// <summary>
    /// Return the length of a recognised leading header in <paramref name="head"/> (the file's first
    /// bytes), or 0 if none is present. Only returns a length the header magic actually confirms <i>and</i>
    /// for which enough bytes were supplied — a truncated read never reports a header.
    /// </summary>
    public static int DetectHeaderLength(ReadOnlySpan<byte> head)
    {
        // iNES (.nes): "NES" + EOF marker, then a 16-byte header.
        if (head.Length >= 16 && head[0] == 'N' && head[1] == 'E' && head[2] == 'S' && head[3] == 0x1A)
            return 16;
        // FDS (.fds): fwNES "FDS" + EOF marker, then a 16-byte header.
        if (head.Length >= 16 && head[0] == 'F' && head[1] == 'D' && head[2] == 'S' && head[3] == 0x1A)
            return 16;
        // Atari Lynx (.lnx): "LYNX" magic, then a 64-byte header.
        if (head.Length >= 64 && head[0] == 'L' && head[1] == 'Y' && head[2] == 'N' && head[3] == 'X')
            return 64;
        // Atari 7800 (.a78): a version byte, then the "ATARI7800" magic at offset 1, in a 128-byte header.
        if (head.Length >= 128 && StartsWithAscii(head[1..], "ATARI7800"))
            return 128;
        return 0;
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string ascii)
    {
        if (data.Length < ascii.Length) return false;
        for (int i = 0; i < ascii.Length; i++)
            if (data[i] != (byte)ascii[i]) return false;
        return true;
    }
}
