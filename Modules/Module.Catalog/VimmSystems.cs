namespace Module.Catalog;

/// <summary>A catalog console that Vimm's Lair carries, mapped to its vault system code.</summary>
public sealed record VimmSystemInfo(string Console, string VimmCode);

/// <summary>
/// Maps catalog console folders to Vimm's Lair vault system codes (e.g. <c>psx</c>→<c>PS1</c>,
/// <c>gc</c>→<c>GameCube</c>, <c>pcengine</c>→<c>TG16</c>, <c>cdimono1</c>→<c>CDi</c>). Only the
/// consoles Vimm actually hosts are listed — codes verified against vimm.net/vault. Catalog consoles
/// absent here simply have no Vimm source, so they get the "no Vimm match" badge by construction
/// (it's expected there, not an error). Console tags match <see cref="CatalogSystems"/> / EmuDeck.
/// </summary>
public static class VimmSystems
{
    public static readonly IReadOnlyList<VimmSystemInfo> All =
    [
        // Nintendo
        new("nes", "NES"),
        new("snes", "SNES"),
        new("n64", "N64"),
        new("gb", "GB"),
        new("gbc", "GBC"),
        new("gba", "GBA"),
        new("virtualboy", "VB"),
        new("nds", "DS"),
        new("n3ds", "3DS"),
        new("gc", "GameCube"),
        new("wii", "Wii"),

        // Sega
        new("genesis", "Genesis"),
        new("mastersystem", "SMS"),
        new("gamegear", "GG"),
        new("sega32x", "32X"),
        new("segacd", "SegaCD"),
        new("saturn", "Saturn"),
        new("dreamcast", "Dreamcast"),

        // Sony
        new("psx", "PS1"),
        new("ps2", "PS2"),
        new("ps3", "PS3"),
        new("psp", "PSP"),

        // Microsoft
        new("xbox", "Xbox"),
        new("xbox360", "Xbox360"),

        // Atari
        new("atari2600", "Atari2600"),
        new("atari5200", "Atari5200"),
        new("atari7800", "Atari7800"),
        new("atarilynx", "Lynx"),
        new("atarijaguar", "Jaguar"),
        new("atarijaguarcd", "JaguarCD"),

        // NEC
        new("pcengine", "TG16"),
        new("pcenginecd", "TGCD"),

        // Philips
        new("cdimono1", "CDi"),
    ];

    private static readonly Dictionary<string, string> ByConsole =
        All.ToDictionary(s => s.Console, s => s.VimmCode);

    /// <summary>The Vimm vault system code for a catalog console, or null if Vimm doesn't carry it.</summary>
    public static string? CodeFor(string console) => ByConsole.GetValueOrDefault(console);
}
