namespace Module.Core;

/// <summary>
/// File extension helpers shared across all modules.
/// </summary>
public static class FileExtensions
{
    public const string DecIso = ".dec.iso";
    public const string Iso = ".iso";
    public const string SevenZip = ".7z";
    public const string Zip = ".zip";
    public const string Rar = ".rar";

    /// <summary>Wii U (CDecrypt-style) compressed disc image — the only format Vimm offers for Wii U.</summary>
    public const string Wux = ".wux";
    /// <summary>Dolphin's compressed GameCube/Wii image format.</summary>
    public const string Rvz = ".rvz";
    /// <summary>Wii "scrubbed" disc image format (also produced by Dolphin).</summary>
    public const string Wbfs = ".wbfs";
    /// <summary>MAME/hashed-CHD compressed disc image, used for PSX/PS2/Saturn/… redump sets.</summary>
    public const string Chd = ".chd";

    private static readonly string[] ArchiveExts = [SevenZip, Zip, Rar];

    /// <summary>
    /// Alternative encodings of a disc image (not archives) whose bytes never equal the canonical
    /// uncompressed image's hash — hashing the container is a false negative, not a mismatch (#284).
    /// </summary>
    private static readonly string[] CompressedImageExts = [Wux, Rvz, Wbfs, Chd];

    public static bool IsArchive(string filename) =>
        ArchiveExts.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsDecIso(string filename) =>
        filename.EndsWith(DecIso, StringComparison.OrdinalIgnoreCase);

    public static bool IsIso(string filename) =>
        filename.EndsWith(Iso, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for a compressed/alternative disc-image encoding (.wux/.rvz/.wbfs/.chd) whose canonical
    /// hash cannot be recovered by hashing the file's own bytes (#284).
    /// </summary>
    public static bool IsCompressedImage(string filename) =>
        CompressedImageExts.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Known platform identifiers from Vimm's Lair.
/// Add new platforms here as support is added.
/// </summary>
public static class Platforms
{
    public const string PS3 = "PlayStation 3";
    public const string PS2 = "PlayStation 2";
    public const string PSP = "PlayStation Portable";
    public const string Wii = "Wii";
    public const string WiiU = "Wii U";
    public const string GameCube = "GameCube";
    public const string Xbox360 = "Xbox 360";

    public static bool IsPS3(string? platform) =>
        PS3.Equals(platform, StringComparison.OrdinalIgnoreCase);

    public static bool IsWiiU(string? platform) =>
        WiiU.Equals(platform, StringComparison.OrdinalIgnoreCase);
}
