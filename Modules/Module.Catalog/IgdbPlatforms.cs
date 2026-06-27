namespace Module.Catalog;

/// <summary>
/// Maps a catalog console slug → the IGDB platform id(s) its games live under, so the IGDB description
/// sync can pull the right platform's games and join them to the catalog by name. A handful of consoles
/// map to two IGDB platforms because No-Intro lumps regional twins into one DAT (NES + Famicom, SNES +
/// Super Famicom). Consoles absent here have no IGDB platform mapping and are simply skipped by the sync
/// (graceful — they get no IGDB descriptions). IDs are IGDB's stable platform ids.
/// </summary>
public static class IgdbPlatforms
{
    public static readonly IReadOnlyDictionary<string, int[]> ByConsole = new Dictionary<string, int[]>
    {
        // Nintendo
        ["nes"] = [18, 99],   // NES + Family Computer (Famicom)
        ["fds"] = [51],       // Family Computer Disk System
        ["snes"] = [19, 58],  // SNES + Super Famicom
        ["n64"] = [4],
        ["gb"] = [33],
        ["gbc"] = [22],
        ["gba"] = [24],
        ["virtualboy"] = [87],
        ["pokemini"] = [166],
        ["nds"] = [20],
        ["n3ds"] = [37],
        ["gc"] = [21],
        ["wii"] = [5],

        // Sega
        ["sg-1000"] = [84],
        ["mastersystem"] = [64],
        ["genesis"] = [29],
        ["sega32x"] = [30],
        ["gamegear"] = [35],
        ["segacd"] = [78],
        ["saturn"] = [32],
        ["dreamcast"] = [23],

        // Sony
        ["psx"] = [7],
        ["ps2"] = [8],
        ["ps3"] = [9],
        ["psp"] = [38],
        ["psvita"] = [46],

        // Microsoft
        ["msx1"] = [27],
        ["msx2"] = [53],
        ["xbox"] = [11],
        ["xbox360"] = [12],

        // Atari
        ["atari2600"] = [59],
        ["atari5200"] = [66],
        ["atari7800"] = [60],
        ["atari800"] = [65],
        ["atarilynx"] = [61],
        ["atarijaguar"] = [62],
        ["atarist"] = [63],

        // NEC
        ["pcengine"] = [86],
        ["supergrafx"] = [128],
        ["pcenginecd"] = [150],
        ["pc98"] = [149],

        // Commodore
        ["c64"] = [15],
        ["c16"] = [94],       // Commodore Plus/4
        ["vic20"] = [71],
        ["amiga"] = [16],
        ["amigacd32"] = [114],
        ["cdtv"] = [158],

        // SNK
        ["ngp"] = [119],
        ["ngpc"] = [120],
        ["neogeocd"] = [136],

        // Bandai
        ["wonderswan"] = [57],
        ["wonderswancolor"] = [123],

        // Other
        ["colecovision"] = [68],
        ["channelf"] = [127],
        ["vectrex"] = [70],
        ["vc4000"] = [138],
        ["x1"] = [77],
        ["x68000"] = [121],
        ["zxspectrum"] = [26],
        ["cdimono1"] = [117], // Philips CD-i
        ["3do"] = [50],
    };

    /// <summary>The IGDB platform id(s) for a console slug, or null when the console has no IGDB mapping.</summary>
    public static int[]? Ids(string console) => ByConsole.TryGetValue(console, out var ids) ? ids : null;
}
