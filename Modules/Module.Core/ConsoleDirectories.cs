using System.Text;

namespace Module.Core;

/// <summary>
/// Maps a Vimm's Lair platform/system name to an EmuDeck-compatible folder name.
/// Completed downloads are sorted into <c>completed/{folder}/</c> so the library
/// matches the directory layout EmuDeck / EmulationStation expect (e.g. ps3, gc, snes).
///
/// Lookup is forgiving: the platform string is normalised to lowercase alphanumerics
/// before matching, so "PlayStation 3", "playstation 3" and "PS3" all resolve to "ps3".
/// Unknown / empty platforms return null — the caller leaves those files in the
/// <c>completed/</c> root rather than guessing a wrong folder.
/// </summary>
public static class ConsoleDirectories
{
    // Keys are pre-normalised (lowercase, alphanumeric only — see Normalize).
    // Values are EmuDeck folder names. Multiple aliases may map to one folder.
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // --- Sony ---
        ["playstation"] = "psx",
        ["playstation1"] = "psx",
        ["ps1"] = "psx",
        ["psx"] = "psx",
        ["psone"] = "psx",
        ["playstation2"] = "ps2",
        ["ps2"] = "ps2",
        ["playstation3"] = "ps3",
        ["ps3"] = "ps3",
        ["playstationportable"] = "psp",
        ["psp"] = "psp",
        ["playstationvita"] = "psvita",
        ["psvita"] = "psvita",
        ["vita"] = "psvita",

        // --- Nintendo ---
        ["nintendo"] = "nes",
        ["nintendoentertainmentsystem"] = "nes",
        ["nes"] = "nes",
        ["famicom"] = "nes",
        ["supernintendo"] = "snes",
        ["supernintendoentertainmentsystem"] = "snes",
        ["snes"] = "snes",
        ["superfamicom"] = "snes",
        ["nintendo64"] = "n64",
        ["n64"] = "n64",
        ["gamecube"] = "gc",
        ["nintendogamecube"] = "gc",
        ["gc"] = "gc",
        ["ngc"] = "gc",
        ["wii"] = "wii",
        ["wiiu"] = "wiiu",
        ["switch"] = "switch",
        ["nintendoswitch"] = "switch",
        ["gameboy"] = "gb",
        ["gb"] = "gb",
        ["gameboycolor"] = "gbc",
        ["gbc"] = "gbc",
        ["gameboyadvance"] = "gba",
        ["gba"] = "gba",
        ["nintendods"] = "nds",
        ["nds"] = "nds",
        ["nintendodsi"] = "nds",
        ["nintendo3ds"] = "n3ds",
        ["3ds"] = "n3ds",
        ["virtualboy"] = "virtualboy",

        // --- Sega ---
        ["genesis"] = "genesis",
        ["segagenesis"] = "genesis",
        ["megadrive"] = "genesis",
        ["segamegadrive"] = "genesis",
        ["mastersystem"] = "mastersystem",
        ["segamastersystem"] = "mastersystem",
        ["sms"] = "mastersystem",
        ["gamegear"] = "gamegear",
        ["segagamegear"] = "gamegear",
        ["segacd"] = "segacd",
        ["megacd"] = "segacd",
        ["sega32x"] = "sega32x",
        ["32x"] = "sega32x",
        ["genesis32x"] = "sega32x",
        ["saturn"] = "saturn",
        ["segasaturn"] = "saturn",
        ["dreamcast"] = "dreamcast",
        ["segadreamcast"] = "dreamcast",
        ["sg1000"] = "sg-1000",

        // --- Microsoft ---
        ["xbox"] = "xbox",
        ["xbox360"] = "xbox360",

        // --- Atari ---
        ["atari2600"] = "atari2600",
        ["2600"] = "atari2600",
        ["atari5200"] = "atari5200",
        ["5200"] = "atari5200",
        ["atari7800"] = "atari7800",
        ["7800"] = "atari7800",
        ["atarilynx"] = "atarilynx",
        ["lynx"] = "atarilynx",
        ["atarijaguar"] = "atarijaguar",
        ["jaguar"] = "atarijaguar",
        ["atarijaguarcd"] = "atarijaguarcd",
        ["jaguarcd"] = "atarijaguarcd",

        // --- NEC ---
        ["turbografx16"] = "tg16",
        ["turbografx"] = "tg16",
        ["tg16"] = "tg16",
        ["pcengine"] = "tg16",
        ["turbografxcd"] = "tg-cd",
        ["turbografx16cd"] = "tg-cd",
        ["tgcd"] = "tg-cd",
        ["pcenginecd"] = "tg-cd",
        ["supergrafx"] = "supergrafx",
        ["pcfx"] = "pcfx",

        // --- Other ---
        ["3do"] = "3do",
        ["neogeo"] = "neogeo",
        ["neogeocd"] = "neogeocd",
        ["neogeopocket"] = "ngp",
        ["ngp"] = "ngp",
        ["neogeopocketcolor"] = "ngpc",
        ["ngpc"] = "ngpc",
        ["wonderswan"] = "wonderswan",
        ["wonderswancolor"] = "wonderswancolor",
        ["colecovision"] = "colecovision",
        ["intellivision"] = "intellivision",
        ["vectrex"] = "vectrex",
        ["odyssey2"] = "odyssey2",
        ["commodore64"] = "c64",
        ["c64"] = "c64",
        ["amiga"] = "amiga",
        ["msx"] = "msx",
        ["atarist"] = "atarist",
        ["x68000"] = "x68000",
    };

    /// <summary>
    /// Resolve the EmuDeck folder for a platform name, or null if the platform is
    /// unknown / empty (caller should leave the file in the completed root).
    /// </summary>
    public static string? Resolve(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;
        return Map.TryGetValue(Normalize(platform), out var dir) ? dir : null;
    }

    /// <summary>Lowercase and strip everything except letters and digits.</summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
