/// <summary>
/// Covers <see cref="XmlDatParser"/> and the <see cref="DatParsers"/> format sniff (#265). Fixtures are
/// trimmed from the real bundle files — the Logiqx dialect in <c>redump.zip</c> and the DAT-o-MATIC v3
/// dialect in <c>no-intro.zip</c> — including the fields that differ between them (Logiqx has neither
/// region nor serial; datomatic adds <c>game_id</c>, rom <c>serial</c>, <c>sha256</c>, <c>status</c>).
/// </summary>
[TestClass]
public class XmlDatParserTests
{
    // Logiqx (redump.zip) — DOCTYPE present, no region, no serial anywhere.
    private const string LogiqxDat = """
        <?xml version="1.0"?>
        <!DOCTYPE datafile PUBLIC "-//Logiqx//DTD ROM Management Datafile//EN" "http://www.logiqx.com/Dats/datafile.dtd">
        <datafile>
        	<header>
        		<name>Nintendo - Wii</name>
        		<description>Nintendo - Wii - Discs (3780)</description>
        		<version>2026-06-15 03-13-28</version>
        		<author>redump.org</author>
        	</header>
        	<game name="Metal Slug Anthology (USA)">
        		<category>Games</category>
        		<description>Metal Slug Anthology (USA)</description>
        		<rom name="Metal Slug Anthology (USA).iso" size="4699979776" crc="68ed804e" md5="16d7bab53fa02d4ddd61b47da66cef93" sha1="9c793e1283bed848a01b38fcaf64f395c1d2f72d"/>
        	</game>
        	<game name="Mario Strikers Charged Football (Europe) (En,Fr,De)">
        		<category>Games</category>
        		<rom name="disc1.iso" size="100" crc="aaaaaaaa"/>
        		<rom name="disc2.iso" size="200" crc="bbbbbbbb"/>
        	</game>
        </datafile>
        """;

    // DAT-o-MATIC v3 (no-intro.zip) — game_id, rom-level serial, sha256/status attributes.
    private const string DatomaticDat = """
        <?xml version="1.0"?>
        <datafile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        	<header>
        		<id>23</id>
        		<name>Nintendo - Game Boy Advance</name>
        		<version>20260707-143610</version>
        	</header>
        	<game name="007 - Everything or Nothing (USA, Europe) (En,Fr,De)" id="1256">
        		<description>007 - Everything or Nothing (USA, Europe) (En,Fr,De)</description>
        		<game_id>ignored-because-a-rom-serial-wins</game_id>
        		<rom name="007.gba" size="8388608" crc="9d4f1e18" md5="b63b2244edc2385ae1eab9c8ee448c6f" sha1="fc6163f99b71b05c10686a0d29010b31274e1dc4" sha256="45d0003d" status="verified" serial="BJBE"/>
        	</game>
        	<game name="000500001014e100 (Unknown)" id="6642">
        		<description>000500001014e100 (Unknown)</description>
        		<game_id>000500001014e100</game_id>
        		<rom name="00000005.h3" size="20" crc="8b221bb5" md5="b1eaa72bad087b1b8bc910f51d16b20e" sha1="991d0df4c3fb9a5d840ba66ed38dac19cf7aa7ac"/>
        		<rom name="tmd.0" size="5044" crc="794eb779"/>
        	</game>
        </datafile>
        """;

    private static List<DatGame> ParseAll(string dat, out IDatParser parser)
    {
        parser = DatParsers.For(dat);
        return parser.Parse(new StringReader(dat)).ToList();
    }

    // ---- format sniffing ----

    [TestMethod]
    public void For_XmlContent_SelectsXmlParser()
        => Assert.IsInstanceOfType<XmlDatParser>(DatParsers.For(LogiqxDat));

    [TestMethod]
    public void For_ClrMameProContent_SelectsClrMameProParser()
        => Assert.IsInstanceOfType<ClrMameProParser>(
            DatParsers.For("""clrmamepro ( name "X" ) game ( name "Y" )"""));

    [TestMethod]
    [DataRow("<?xml version=\"1.0\"?><datafile/>")]
    [DataRow("\n\n\t  <datafile/>")]              // leading whitespace still sniffs as XML
    [DataRow("<!DOCTYPE datafile><datafile/>")]
    public void LooksLikeXml_True(string content) => Assert.IsTrue(DatParsers.LooksLikeXml(content));

    [TestMethod]
    [DataRow("clrmamepro ( name \"X\" )")]
    [DataRow("")]                                  // empty is not XML — falls back to the legacy parser
    [DataRow("   \n\t ")]
    public void LooksLikeXml_False(string content) => Assert.IsFalse(DatParsers.LooksLikeXml(content));

    // ---- Logiqx dialect ----

    [TestMethod]
    public void Logiqx_ParsesHeaderAndGames()
    {
        var games = ParseAll(LogiqxDat, out var parser);

        Assert.AreEqual("Nintendo - Wii", parser.Header?.Name);
        Assert.AreEqual("2026-06-15 03-13-28", parser.Header?.Version);
        Assert.HasCount(2, games);
        Assert.AreEqual("Metal Slug Anthology (USA)", games[0].Name);
    }

    [TestMethod]
    public void Logiqx_ParsesRomHashesAndSize()
    {
        var rom = ParseAll(LogiqxDat, out _)[0].Roms.Single();

        Assert.AreEqual("Metal Slug Anthology (USA).iso", rom.Name);
        Assert.AreEqual(4699979776L, rom.Size);
        Assert.AreEqual("68ed804e", rom.Crc);
        Assert.AreEqual("16d7bab53fa02d4ddd61b47da66cef93", rom.Md5);
        Assert.AreEqual("9c793e1283bed848a01b38fcaf64f395c1d2f72d", rom.Sha1);
    }

    [TestMethod]
    public void Logiqx_MultiDiscGame_KeepsEveryRom()
        => Assert.HasCount(2, ParseAll(LogiqxDat, out _)[1].Roms);

    /// <summary>
    /// Logiqx carries neither field. Region stays null on purpose — Dedup falls back to the name tags —
    /// and the missing serial is an unrecoverable fidelity loss vs the clrmamepro mirror, asserted here
    /// so the trade-off is visible rather than discovered later.
    /// </summary>
    [TestMethod]
    public void Logiqx_HasNoRegionOrSerial()
    {
        var g = ParseAll(LogiqxDat, out _)[0];
        Assert.IsNull(g.Region);
        Assert.IsNull(g.Serial);
    }

    [TestMethod]
    public void Logiqx_LanguagesStillParsedFromName()
        => CollectionAssert.AreEqual(new[] { "En", "Fr", "De" }, ParseAll(LogiqxDat, out _)[1].Languages.ToArray());

    // ---- DAT-o-MATIC dialect ----

    [TestMethod]
    public void Datomatic_ParsesHeaderAndGames()
    {
        var games = ParseAll(DatomaticDat, out var parser);

        Assert.AreEqual("Nintendo - Game Boy Advance", parser.Header?.Name);
        Assert.AreEqual("20260707-143610", parser.Header?.Version);
        Assert.HasCount(2, games);
    }

    [TestMethod]
    public void Datomatic_RomSerial_WinsOverGameId()
    {
        var g = ParseAll(DatomaticDat, out _)[0];
        Assert.AreEqual("BJBE", g.Serial);
        Assert.AreEqual("BJBE", g.Roms.Single().Serial);
    }

    /// <summary>
    /// The Wii U digital/CDN shape (#266): no rom serial anywhere, so the 16-hex title id in
    /// <c>&lt;game_id&gt;</c> becomes the game's structured identifier.
    /// </summary>
    [TestMethod]
    public void Datomatic_GameId_BecomesSerial_WhenNoRomSerial()
    {
        var g = ParseAll(DatomaticDat, out _)[1];
        Assert.AreEqual("000500001014e100", g.Serial);
        Assert.HasCount(2, g.Roms);
    }

    [TestMethod]
    public void Datomatic_UnknownAttributes_DoNotBreakParsing()
    {
        var rom = ParseAll(DatomaticDat, out _)[0].Roms.Single();
        Assert.AreEqual("fc6163f99b71b05c10686a0d29010b31274e1dc4", rom.Sha1);   // sha256/status ignored
    }

    // ---- robustness ----

    [TestMethod]
    public void EmptyDatafile_YieldsNoGames()
        => Assert.IsEmpty(ParseAll("""<?xml version="1.0"?><datafile><header><name>X</name></header></datafile>""", out _));

    [TestMethod]
    public void GameWithNoRoms_StillYieldsTheGame()
    {
        var games = ParseAll("""
            <?xml version="1.0"?>
            <datafile><game name="Bare (USA)"></game></datafile>
            """, out _);

        Assert.HasCount(1, games);
        Assert.IsEmpty(games[0].Roms);
    }

    [TestMethod]
    public void SelfClosingGame_IsHandled()
        => Assert.HasCount(1, ParseAll("""<?xml version="1.0"?><datafile><game name="Empty (USA)"/></datafile>""", out _));

    [TestMethod]
    public void MissingSize_DefaultsToZero()
    {
        var rom = ParseAll("""
            <?xml version="1.0"?>
            <datafile><game name="X"><rom name="x.bin" crc="dead"/></game></datafile>
            """, out _)[0].Roms.Single();

        Assert.AreEqual(0L, rom.Size);
        Assert.AreEqual("dead", rom.Crc);
    }
}
