using Module.Core;

[TestClass]
public class ConsoleDirectoriesTests
{
    [DataTestMethod]
    // Real Vimm's Lair section titles
    [DataRow("PlayStation 3", "ps3")]
    [DataRow("PlayStation 2", "ps2")]
    [DataRow("PlayStation", "psx")]
    [DataRow("PlayStation Portable", "psp")]
    [DataRow("GameCube", "gc")]
    [DataRow("Wii", "wii")]
    [DataRow("Wii U", "wiiu")]
    [DataRow("Nintendo 64", "n64")]
    [DataRow("Nintendo DS", "nds")]
    [DataRow("Super Nintendo", "snes")]
    [DataRow("Nintendo", "nes")]
    [DataRow("Game Boy", "gb")]
    [DataRow("Game Boy Color", "gbc")]
    [DataRow("Game Boy Advance", "gba")]
    [DataRow("Genesis", "genesis")]
    [DataRow("Master System", "mastersystem")]
    [DataRow("Game Gear", "gamegear")]
    [DataRow("Sega CD", "segacd")]
    [DataRow("Sega 32X", "sega32x")]
    [DataRow("Saturn", "saturn")]
    [DataRow("Dreamcast", "dreamcast")]
    [DataRow("Xbox", "xbox")]
    [DataRow("Xbox 360", "xbox360")]
    [DataRow("Atari 2600", "atari2600")]
    [DataRow("TurboGrafx-16", "tg16")]
    [DataRow("TurboGrafx-CD", "tg-cd")]
    // Mock-server short codes / aliases
    [DataRow("NES", "nes")]
    [DataRow("SNES", "snes")]
    [DataRow("PS1", "psx")]
    [DataRow("GB", "gb")]
    public void Resolve_KnownPlatform_ReturnsEmuDeckFolder(string platform, string expected)
        => Assert.AreEqual(expected, ConsoleDirectories.Resolve(platform));

    [TestMethod]
    public void Resolve_IsCaseAndPunctuationInsensitive()
    {
        Assert.AreEqual("ps3", ConsoleDirectories.Resolve("playstation 3"));
        Assert.AreEqual("ps3", ConsoleDirectories.Resolve("PLAYSTATION 3"));
        Assert.AreEqual("ps3", ConsoleDirectories.Resolve("PlayStation-3"));
        Assert.AreEqual("ps3", ConsoleDirectories.Resolve("  PlayStation 3  "));
    }

    [TestMethod]
    public void Resolve_DistinguishesSimilarNames()
    {
        Assert.AreEqual("nes", ConsoleDirectories.Resolve("Nintendo"));
        Assert.AreEqual("n64", ConsoleDirectories.Resolve("Nintendo 64"));
        Assert.AreEqual("nds", ConsoleDirectories.Resolve("Nintendo DS"));
        Assert.AreEqual("gb", ConsoleDirectories.Resolve("Game Boy"));
        Assert.AreEqual("gba", ConsoleDirectories.Resolve("Game Boy Advance"));
    }

    [TestMethod]
    public void Resolve_UnknownOrEmpty_ReturnsNull()
    {
        Assert.IsNull(ConsoleDirectories.Resolve("Some Future Console"));
        Assert.IsNull(ConsoleDirectories.Resolve(""));
        Assert.IsNull(ConsoleDirectories.Resolve("   "));
        Assert.IsNull(ConsoleDirectories.Resolve(null));
    }
}
