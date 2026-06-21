namespace Module.Catalog;

/// <summary>A console DAT to sync: its libretro-database metadat group + the EmuDeck folder it maps to.</summary>
public sealed record CatalogSystemInfo(string DatName, string Group, string Console);

/// <summary>
/// The consoles synced into the catalog, each mapped to its libretro-database metadat group
/// (<c>no-intro</c> for carts/handhelds, <c>redump</c> for discs) and EmuDeck console folder.
/// DAT names are the exact libretro filenames (verified present); consoles match
/// <c>Module.Core.ConsoleDirectories</c> so the catalog lines up with <c>completed/{console}/</c>.
/// Explicit by design — no name-guessing.
/// </summary>
public static class CatalogSystems
{
    public static readonly IReadOnlyList<CatalogSystemInfo> All =
    [
        new("Nintendo - Nintendo Entertainment System", "no-intro", "nes"),
        new("Nintendo - Super Nintendo Entertainment System", "no-intro", "snes"),
        new("Nintendo - Nintendo 64", "no-intro", "n64"),
        new("Nintendo - Game Boy", "no-intro", "gb"),
        new("Nintendo - Game Boy Color", "no-intro", "gbc"),
        new("Nintendo - Game Boy Advance", "no-intro", "gba"),
        new("Sega - Mega Drive - Genesis", "no-intro", "genesis"),
        new("Sega - Master System - Mark III", "no-intro", "mastersystem"),
        new("Sega - Game Gear", "no-intro", "gamegear"),
        new("Sony - PlayStation", "redump", "psx"),
        new("Sony - PlayStation 2", "redump", "ps2"),
        new("Sony - PlayStation 3", "redump", "ps3"),
        new("Sony - PlayStation Portable", "redump", "psp"),
        new("Nintendo - GameCube", "redump", "gc"),
        new("Nintendo - Wii", "redump", "wii"),
        new("Sega - Dreamcast", "redump", "dreamcast"),
        new("Sega - Saturn", "redump", "saturn"),
        new("Microsoft - Xbox", "redump", "xbox"),
        new("Microsoft - Xbox 360", "redump", "xbox360"),
        new("Nintendo - Nintendo DS", "no-intro", "nds"),
        new("Nintendo - Nintendo 3DS", "no-intro", "n3ds"),
    ];
}
