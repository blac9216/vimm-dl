namespace Module.Catalog;

/// <summary>
/// Maps a catalog console slug → its RetroAchievements system id, restricted to the cartridge/handheld
/// consoles whose RA hash is a <b>full-file MD5</b> — i.e. equal to the No-Intro full-file MD5 the
/// catalog stores — so an exact MD5 hash-join works (epic #123 / R2). Consoles absent here are skipped
/// by the RA popularity sync: disc systems (PSX/PS2/Saturn/Sega CD/Dreamcast/PSP) and the custom-hash
/// systems (NES iNES, N64 byte-swap, Atari Lynx/7800 headers, FDS) hash differently, so they wouldn&#39;t
/// match anyway. Because the join is exact-MD5 equality there are no false positives — a borderline or
/// stale id simply yields no matches, never wrong data.
/// </summary>
public static class RaConsoles
{
    // RA system ids (stable; see RetroAchievements GetConsoleIDs). Several catalog slugs intentionally
    // share one RA id (NGP/NGPC = 14, WonderSwan/Color = 53) — each is hash-joined against its own
    // console's catalog ROMs, so they're listed separately.
    public static readonly IReadOnlyDictionary<string, int> ByConsole = new Dictionary<string, int>
    {
        ["genesis"] = 1,          // Mega Drive / Genesis
        ["snes"] = 3,             // SNES (RA strips an SMC copier header if present; No-Intro is headerless → matches)
        ["gb"] = 4,               // Game Boy
        ["gba"] = 5,              // Game Boy Advance
        ["gbc"] = 6,              // Game Boy Color
        ["pcengine"] = 8,         // PC Engine / TurboGrafx-16 (HuCard)
        ["sega32x"] = 10,         // Sega 32X
        ["mastersystem"] = 11,    // Master System
        ["ngp"] = 14,             // Neo Geo Pocket
        ["ngpc"] = 14,            // Neo Geo Pocket Color (same RA system)
        ["gamegear"] = 15,        // Game Gear
        ["colecovision"] = 44,    // ColecoVision
        ["vectrex"] = 46,         // Vectrex
        ["pokemini"] = 24,        // Pokemon Mini
        ["atari2600"] = 25,       // Atari 2600
        ["virtualboy"] = 28,      // Virtual Boy
        ["sg-1000"] = 33,         // SG-1000
        ["wonderswan"] = 53,      // WonderSwan
        ["wonderswancolor"] = 53, // WonderSwan Color (same RA system)
    };

    /// <summary>The RA system id for a console slug, or null when the console isn't RA hash-joinable.</summary>
    public static int? Id(string console) => ByConsole.TryGetValue(console, out var id) ? id : null;
}
