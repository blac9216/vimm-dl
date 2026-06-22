namespace Module.Catalog;

/// <summary>A console DAT to sync: its libretro-database metadat group + the EmuDeck folder it maps to.</summary>
public sealed record CatalogSystemInfo(string DatName, string Group, string Console);

/// <summary>
/// The consoles synced into the catalog, each mapped to its libretro-database metadat group
/// (<c>no-intro</c> for carts/handhelds, <c>redump</c> for discs) and EmuDeck console folder.
/// DAT names are the exact libretro filenames (verified present against the mirror's
/// <c>metadat/{no-intro,redump}</c> tree); consoles match the EmuDeck / ES-DE folder set (or, for
/// systems ES-DE doesn't carry, the conventional MAME/RetroArch slug) so the catalog lines up with
/// <c>completed/{console}/</c>. Explicit by design — no name-guessing. Goal: every console/handheld
/// that ships a No-Intro/Redump DAT is browsable.
///
/// Coverage is the full console+handheld set on the mirror. DATs that exist there but are
/// deliberately skipped — digital-distribution variants, non-game UMD/update DATs, and hardware
/// variants that collide with an included base system's folder — are listed with reasons in
/// <see cref="ExcludedDats"/> (test-guarded against silent drift). Out of scope entirely (different
/// identity space, per epic #119): arcade boards (mame/fbneo/cps/model2/model3/naomigd/atomiswave/
/// daphne — though disc-based Naomi/Naomi 2 are kept), TOSEC home computers, modern consoles without
/// a DAT here (switch/ps4/android), source-port engines (doom/quake/scummvm/…), and launchers. Wii U
/// is reserved for the clean-room NUS path.
/// </summary>
public static class CatalogSystems
{
    public static readonly IReadOnlyList<CatalogSystemInfo> All =
    [
        // Nintendo
        new("Nintendo - Nintendo Entertainment System", "no-intro", "nes"),
        new("Nintendo - Family Computer Disk System", "no-intro", "fds"),
        new("Nintendo - Super Nintendo Entertainment System", "no-intro", "snes"),
        new("Nintendo - Satellaview", "no-intro", "satellaview"),
        new("Nintendo - Sufami Turbo", "no-intro", "sufami"),
        new("Nintendo - Nintendo 64", "no-intro", "n64"),
        new("Nintendo - Nintendo 64DD", "no-intro", "n64dd"),
        new("Nintendo - Game Boy", "no-intro", "gb"),
        new("Nintendo - Game Boy Color", "no-intro", "gbc"),
        new("Nintendo - Game Boy Advance", "no-intro", "gba"),
        new("Nintendo - Virtual Boy", "no-intro", "virtualboy"),
        new("Nintendo - Pokemon Mini", "no-intro", "pokemini"),
        new("Nintendo - Nintendo DS", "no-intro", "nds"),
        new("Nintendo - Nintendo 3DS", "no-intro", "n3ds"),
        new("Nintendo - GameCube", "redump", "gc"),
        new("Nintendo - Wii", "redump", "wii"),

        // Sega
        new("Sega - SG-1000", "no-intro", "sg-1000"),
        new("Sega - Master System - Mark III", "no-intro", "mastersystem"),
        new("Sega - Mega Drive - Genesis", "no-intro", "genesis"),
        new("Sega - 32X", "no-intro", "sega32x"),
        new("Sega - Game Gear", "no-intro", "gamegear"),
        new("Sega - Mega-CD - Sega CD", "redump", "segacd"),
        new("Sega - Saturn", "redump", "saturn"),
        new("Sega - Dreamcast", "redump", "dreamcast"),
        new("Sega - Naomi", "redump", "naomi"),
        new("Sega - Naomi 2", "redump", "naomi2"),
        new("Sega - PICO", "no-intro", "segapico"),
        new("Sega - Beena", "no-intro", "beena"),

        // Sony
        new("Sony - PlayStation", "redump", "psx"),
        new("Sony - PlayStation 2", "redump", "ps2"),
        new("Sony - PlayStation 3", "redump", "ps3"),
        new("Sony - PlayStation Portable", "redump", "psp"),
        new("Sony - PlayStation Vita", "no-intro", "psvita"),

        // Microsoft
        new("Microsoft - MSX", "no-intro", "msx1"),
        new("Microsoft - MSX2", "no-intro", "msx2"),
        new("Microsoft - Xbox", "redump", "xbox"),
        new("Microsoft - Xbox 360", "redump", "xbox360"),

        // Atari
        new("Atari - 2600", "no-intro", "atari2600"),
        new("Atari - 5200", "no-intro", "atari5200"),
        new("Atari - 7800", "no-intro", "atari7800"),
        new("Atari - 8-bit Family", "no-intro", "atari800"),
        new("Atari - Lynx", "no-intro", "atarilynx"),
        new("Atari - Jaguar", "no-intro", "atarijaguar"),
        new("Atari - Jaguar CD", "redump", "atarijaguarcd"),
        new("Atari - ST", "no-intro", "atarist"),

        // NEC
        new("NEC - PC Engine - TurboGrafx 16", "no-intro", "pcengine"),
        new("NEC - PC Engine SuperGrafx", "no-intro", "supergrafx"),
        new("NEC - PC Engine CD - TurboGrafx-CD", "redump", "pcenginecd"),
        new("NEC - PC-FX", "redump", "pcfx"),
        new("NEC - PC-98", "redump", "pc98"),

        // Commodore
        new("Commodore - 64", "no-intro", "c64"),
        new("Commodore - Plus-4", "no-intro", "c16"),
        new("Commodore - VIC-20", "no-intro", "vic20"),
        new("Commodore - Amiga", "no-intro", "amiga"),
        new("Commodore - CD32", "redump", "amigacd32"),
        new("Commodore - CDTV", "redump", "cdtv"),

        // SNK
        new("SNK - Neo Geo Pocket", "no-intro", "ngp"),
        new("SNK - Neo Geo Pocket Color", "no-intro", "ngpc"),
        new("SNK - Neo Geo CD", "redump", "neogeocd"),

        // Bandai
        new("Bandai - WonderSwan", "no-intro", "wonderswan"),
        new("Bandai - WonderSwan Color", "no-intro", "wonderswancolor"),

        // Other cartridge / handheld systems
        new("Arduboy Inc - Arduboy", "no-intro", "arduboy"),
        new("Benesse - Pocket Challenge V2", "no-intro", "pockchalv2"),
        new("Casio - Loopy", "no-intro", "casloopy"),
        new("Casio - PV-1000", "no-intro", "pv1000"),
        new("Coleco - ColecoVision", "no-intro", "colecovision"),
        new("Emerson - Arcadia 2001", "no-intro", "arcadia"),
        new("Entex - Adventure Vision", "no-intro", "advision"),
        new("Epoch - Super Cassette Vision", "no-intro", "scv"),
        new("Fairchild - Channel F", "no-intro", "channelf"),
        new("Funtech - Super Acan", "no-intro", "supracan"),
        new("GamePark - GP32", "no-intro", "gp32"),
        new("GCE - Vectrex", "no-intro", "vectrex"),
        new("Hartung - Game Master", "no-intro", "gmaster"),
        new("Interton - VC 4000", "no-intro", "vc4000"),
        new("Konami - Picno", "no-intro", "picno"),
        new("LeapFrog - LeapPad", "no-intro", "leappad"),
        new("LeapFrog - Leapster Learning Game System", "no-intro", "leapster"),
        new("Magnavox - Odyssey2", "no-intro", "odyssey2"),
        new("Mattel - Intellivision", "no-intro", "intellivision"),
        new("Philips - Videopac+", "no-intro", "videopac"),
        new("RCA - Studio II", "no-intro", "studio2"),
        new("Sharp - X1", "no-intro", "x1"),
        new("Sharp - X68000", "no-intro", "x68000"),
        new("Sinclair - ZX Spectrum +3", "no-intro", "zxspectrum"),
        new("Tiger - Game.com", "no-intro", "gamecom"),
        new("VTech - V.Smile", "no-intro", "vsmile"),
        new("VTech - CreatiVision", "no-intro", "crvision"),
        new("Watara - Supervision", "no-intro", "supervision"),

        // Mobile
        new("Mobile - J2ME", "no-intro", "j2me"),
        new("Mobile - Palm OS", "no-intro", "palm"),
        new("Mobile - Symbian", "no-intro", "symbian"),
        new("Mobile - Zeebo", "no-intro", "zeebo"),

        // Other disc systems
        new("Philips - CD-i", "redump", "cdimono1"),
        new("The 3DO Company - 3DO", "redump", "3do"),
    ];

    /// <summary>
    /// No-Intro/Redump DATs that exist on the mirror's <c>metadat/{no-intro,redump}</c> tree but are
    /// intentionally NOT synced, each mapped to the reason. They fall into three buckets: (a)
    /// digital-distribution variants whose physical release is already synced (PSN / eShop / WiiWare /
    /// Games-on-Demand), (b) non-game DATs (UMD Music/Video, Title Updates, DS Download Play, e-Reader
    /// dot-code card data), and (c) hardware variants that share an emulator + <c>completed/{console}/</c>
    /// folder with an included base system (DSi → NDS, New 3DS → 3DS). Wii U digital is reserved for the
    /// clean-room NUS path. Folding a digital release onto its physical twin by hash is the dedup epic's
    /// job (#119 D2), not coverage — so they are excluded here, explicitly and test-guarded, rather than
    /// silently dropped. Not included: the two cross-group name collisions <c>Microsoft - Xbox 360</c> and
    /// <c>Sony - PlayStation Portable</c> (both exist under no-intro AND redump) — the Redump disc DAT is
    /// the one in <see cref="All"/>; the no-intro digital twin is superseded by that, not listed here.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ExcludedDats = new Dictionary<string, string>
    {
        ["Microsoft - XBOX 360 (Games on Demand)"] = "digital (Games on Demand) — discs are the Redump 'Microsoft - Xbox 360' DAT",
        ["Microsoft - XBOX 360 (Title Updates)"] = "title updates/patches, not games",
        ["Microsoft - Xbox 360 (Digital)"] = "digital — discs are the Redump 'Microsoft - Xbox 360' DAT",
        ["Nintendo - New Nintendo 3DS"] = "New 3DS variant — same Azahar/Citra emulator + n3ds folder as the synced 3DS DAT",
        ["Nintendo - New Nintendo 3DS (Digital)"] = "New 3DS digital (eShop) variant",
        ["Nintendo - Nintendo 3DS (Digital)"] = "digital (eShop) — cartridges are the synced 'Nintendo - Nintendo 3DS' DAT",
        ["Nintendo - Nintendo DS (Download Play)"] = "download-play demos, not standalone releases",
        ["Nintendo - Nintendo DSi"] = "DSi (DSiWare digital + enhanced) — same emulator + nds folder as the synced DS DAT",
        ["Nintendo - Wii (Digital)"] = "WiiWare/digital — discs are the Redump 'Nintendo - Wii' DAT",
        ["Nintendo - Wii U (Digital)"] = "Wii U reserved for the clean-room NUS download path",
        ["Nintendo - e-Reader"] = "GBA e-Reader dot-code card data, not standalone games",
        ["Sony - PlayStation 3 (PSN)"] = "digital (PSN) — discs are the Redump 'Sony - PlayStation 3' DAT",
        ["Sony - PlayStation Portable (PSN)"] = "digital (PSN) — UMDs are the Redump 'Sony - PlayStation Portable' DAT",
        ["Sony - PlayStation Portable (PSX2PSP)"] = "PS1-on-PSP repackages, not native PSP releases",
        ["Sony - PlayStation Portable (UMD Music)"] = "non-game (music UMDs)",
        ["Sony - PlayStation Portable (UMD Video)"] = "non-game (video UMDs)",
        ["Sony - PlayStation Vita (PSN)"] = "digital (PSN) — gamecards are the synced 'Sony - PlayStation Vita' DAT",
    };
}
