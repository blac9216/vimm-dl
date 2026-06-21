namespace Module.Catalog;

/// <summary>A console DAT to sync: its libretro-database metadat group + the EmuDeck folder it maps to.</summary>
public sealed record CatalogSystemInfo(string DatName, string Group, string Console);

/// <summary>
/// The consoles synced into the catalog, each mapped to its libretro-database metadat group
/// (<c>no-intro</c> for carts/handhelds, <c>redump</c> for discs) and EmuDeck console folder.
/// DAT names are the exact libretro filenames (verified present); consoles match the EmuDeck /
/// ES-DE folder set so the catalog lines up with <c>completed/{console}/</c>. Explicit by design —
/// no name-guessing. Goal: every game on every console that ships a No-Intro/Redump DAT is browsable.
///
/// Not listed here (no No-Intro/Redump DAT to drive them): arcade boards (arcade/mame/fbneo/cps/
/// neogeo/model2/model3/naomigd/atomiswave/daphne), most home computers (dos/amstradcpc/apple2/
/// macintosh/pc88/Thomson/etc.), modern consoles without a DAT here (switch/ps4/android), source-port
/// engines (doom/quake/scummvm/openbor/…), and launchers (steam/kodi/ports/…). Wii U is intentionally
/// reserved for the clean-room NUS path.
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
        new("Coleco - ColecoVision", "no-intro", "colecovision"),
        new("Emerson - Arcadia 2001", "no-intro", "arcadia"),
        new("Fairchild - Channel F", "no-intro", "channelf"),
        new("GCE - Vectrex", "no-intro", "vectrex"),
        new("Magnavox - Odyssey2", "no-intro", "odyssey2"),
        new("Mattel - Intellivision", "no-intro", "intellivision"),
        new("Philips - Videopac+", "no-intro", "videopac"),
        new("Sharp - X1", "no-intro", "x1"),
        new("Sharp - X68000", "no-intro", "x68000"),
        new("Sinclair - ZX Spectrum +3", "no-intro", "zxspectrum"),
        new("VTech - V.Smile", "no-intro", "vsmile"),
        new("VTech - CreatiVision", "no-intro", "crvision"),
        new("Watara - Supervision", "no-intro", "supervision"),
        new("Arduboy Inc - Arduboy", "no-intro", "arduboy"),
        new("Casio - PV-1000", "no-intro", "pv1000"),
        new("Tiger - Game.com", "no-intro", "gamecom"),

        // Mobile
        new("Mobile - J2ME", "no-intro", "j2me"),
        new("Mobile - Palm OS", "no-intro", "palm"),
        new("Mobile - Symbian", "no-intro", "symbian"),

        // Other disc systems
        new("Philips - CD-i", "redump", "cdimono1"),
        new("The 3DO Company - 3DO", "redump", "3do"),
    ];
}
