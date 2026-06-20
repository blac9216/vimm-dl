[TestClass]
public class ClrMameProParserTests
{
    // Mirrors the real libretro-database clrmamepro form: a header block, a single-rom
    // No-Intro cart game, and a multi-disc multi-language Redump game with a release sub-block.
    private const string Dat = """
        clrmamepro (
            name "Sony - PlayStation 2"
            description "Sony - PlayStation 2"
            version "2026.05.02"
            homepage "http://github.com/robloach/libretro-dats"
        )
        game (
            name "Advance Wars (USA)"
            region "USA"
            serial "AWRE"
            rom ( name "Advance Wars (USA).gba" size 4194304 crc DBEF116C md5 27F322F5CD535297AB21BC4A41CBFC12 sha1 D0A0A4CFE9B95AC7118F7EF476F014CA0242EB65 serial "AWRE" )
        )
        game (
            name "007 - From Russia with Love (Europe) (En,Fr,De,Es,It)"
            region "Europe"
            serial "SLES-53462"
            release ( name "007" region "EUR" )
            rom ( name "007 - From Russia with Love (Europe) (En,Fr,De,Es,It) (Disc 1).iso" size 1000 crc AAAA1111 )
            rom ( name "007 - From Russia with Love (Europe) (En,Fr,De,Es,It) (Disc 2).iso" size 2000 crc BBBB2222 )
        )
        """;

    private static List<DatGame> ParseAll(string text, out ClrMameProParser parser)
    {
        parser = new ClrMameProParser();
        return parser.Parse(new StringReader(text)).ToList();
    }

    [TestMethod]
    public void Header_NameAndVersion()
    {
        ParseAll(Dat, out var p);
        Assert.IsNotNull(p.Header);
        Assert.AreEqual("Sony - PlayStation 2", p.Header!.Name);
        Assert.AreEqual("2026.05.02", p.Header.Version);
    }

    [TestMethod]
    public void Game_FieldsAndHashes()
    {
        var games = ParseAll(Dat, out _);
        Assert.HasCount(2, games);

        var aw = games[0];
        Assert.AreEqual("Advance Wars (USA)", aw.Name);
        Assert.AreEqual("USA", aw.Region);
        Assert.AreEqual("AWRE", aw.Serial);
        Assert.HasCount(1, aw.Roms);

        var rom = aw.Roms[0];
        Assert.AreEqual("Advance Wars (USA).gba", rom.Name);
        Assert.AreEqual(4194304, rom.Size);
        Assert.AreEqual("DBEF116C", rom.Crc);
        Assert.AreEqual("27F322F5CD535297AB21BC4A41CBFC12", rom.Md5);
        Assert.AreEqual("D0A0A4CFE9B95AC7118F7EF476F014CA0242EB65", rom.Sha1);
        Assert.AreEqual("AWRE", rom.Serial);
    }

    [TestMethod]
    public void MultiDisc_AllRomsPreserved()
    {
        var games = ParseAll(Dat, out _);
        var bond = games[1];
        Assert.HasCount(2, bond.Roms);
        Assert.AreEqual(1000, bond.Roms[0].Size);
        Assert.AreEqual("AAAA1111", bond.Roms[0].Crc);
        Assert.AreEqual("BBBB2222", bond.Roms[1].Crc);
    }

    [TestMethod]
    public void Languages_ParsedFromNameTag()
    {
        var games = ParseAll(Dat, out _);
        CollectionAssert.AreEqual(new[] { "En", "Fr", "De", "Es", "It" }, games[1].Languages.ToArray());
    }

    [TestMethod]
    public void NoLanguageTag_EmptyList()
    {
        var games = ParseAll(Dat, out _);
        Assert.IsEmpty(games[0].Languages); // "Advance Wars (USA)" — region only
    }

    [TestMethod]
    public void NonRomSubBlocks_AreSkipped()
    {
        // The release(...) block in game 2 must not be mistaken for a rom.
        var games = ParseAll(Dat, out _);
        Assert.HasCount(2, games[1].Roms);
    }

    [TestMethod]
    public void QuotedName_PreservesInnerParens()
    {
        var games = ParseAll(Dat, out _);
        StringAssert.Contains(games[1].Name, "(Europe)");
    }

    [TestMethod]
    public void ParseLanguages_IgnoresRegionAndRevisionGroups()
    {
        Assert.IsEmpty(ClrMameProParser.ParseLanguages("Game (USA) (Rev 1)"));
        CollectionAssert.AreEqual(new[] { "En" }, ClrMameProParser.ParseLanguages("Game (Europe) (En)").ToArray());
        CollectionAssert.AreEqual(new[] { "Ja", "En" }, ClrMameProParser.ParseLanguages("Game (Japan) (Ja,En)").ToArray());
        Assert.IsEmpty(ClrMameProParser.ParseLanguages("Plain Name"));
    }

    [TestMethod]
    public void Malformed_PartialGame_DoesNotThrow()
    {
        var games = new ClrMameProParser().Parse(new StringReader("game ( name \"X\" region")).ToList();
        Assert.HasCount(1, games);   // best-effort: the partial game still comes through
        Assert.AreEqual("X", games[0].Name);
    }

    [TestMethod]
    public void EmptyAndJunkInput_YieldNothing()
    {
        Assert.IsEmpty(new ClrMameProParser().Parse(new StringReader("")));
        Assert.IsEmpty(new ClrMameProParser().Parse(new StringReader("garbage tokens ) ) (")));
    }

    [TestMethod]
    public void UnknownTopLevelBlock_Skipped()
    {
        const string dat = """
            clrmamepro ( name "Sys" version "1" )
            skipme ( foo "bar" nested ( x "y" ) )
            game ( name "G" rom ( name "g.rom" size 5 ) )
            """;
        var games = new ClrMameProParser().Parse(new StringReader(dat)).ToList();
        Assert.HasCount(1, games);
        Assert.AreEqual("G", games[0].Name);
        Assert.AreEqual(5, games[0].Roms[0].Size);
    }
}
