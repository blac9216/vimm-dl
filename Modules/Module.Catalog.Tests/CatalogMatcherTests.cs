[TestClass]
public class CatalogMatcherTests
{
    [TestMethod]
    [DataRow("Advance Wars (USA)", "advance wars usa")]
    [DataRow("Game: The - Sequel!!", "game the sequel")]
    [DataRow("  Spaced   Out  ", "spaced out")]
    [DataRow("Pokémon FireRed", "pokémon firered")]
    public void Normalize_StripsPunctuationAndCollapses(string input, string expected)
        => Assert.AreEqual(expected, CatalogMatcher.Normalize(input));

    private static readonly (long, string, string)[] Games =
    [
        (1, "gba", "Advance Wars (USA)"),
        (2, "snes", "Advance Wars (USA)"),     // same name, different console
        (3, "ps3", "Heavy Title (USA)"),
    ];

    [TestMethod]
    public void Match_ExactName_SameConsole()
    {
        var owned = CatalogMatcher.Match(Games, [("gba", "/dl/gba/Advance Wars (USA).gba")]);
        Assert.HasCount(1, owned);
        Assert.AreEqual("/dl/gba/Advance Wars (USA).gba", owned[1]);
    }

    [TestMethod]
    public void Match_IgnoresExtension_ArchiveOrRom()
    {
        var owned = CatalogMatcher.Match(Games, [("gba", "/dl/gba/Advance Wars (USA).zip")]);
        Assert.IsTrue(owned.ContainsKey(1));
    }

    [TestMethod]
    public void Match_TolerantOfPunctuation()
    {
        // "Advance Wars - USA" normalizes to the same key as "Advance Wars (USA)".
        var owned = CatalogMatcher.Match(Games, [("gba", "/dl/gba/Advance Wars - USA.7z")]);
        Assert.IsTrue(owned.ContainsKey(1));
    }

    [TestMethod]
    public void Match_ConsoleScoped_NoCrossConsole()
    {
        // A gba file matches only the gba game (1), never the identically-named snes game (2).
        var owned = CatalogMatcher.Match(Games, [("gba", "/dl/gba/Advance Wars (USA).gba")]);
        Assert.IsTrue(owned.ContainsKey(1));
        Assert.IsFalse(owned.ContainsKey(2));
    }

    [TestMethod]
    public void Match_UnknownName_NotOwned()
    {
        var owned = CatalogMatcher.Match(Games, [("ps3", "/dl/ps3/Some Other Game (Europe).iso")]);
        Assert.IsEmpty(owned);
    }

    [TestMethod]
    public void Match_RootFileNoConsole_NotOwned()
    {
        // Files with no console (legacy completed/ root) can't match a console-keyed game.
        var owned = CatalogMatcher.Match(Games, [("", "/dl/Advance Wars (USA).gba")]);
        Assert.IsEmpty(owned);
    }

    [TestMethod]
    public void Match_MultipleFiles_MatchesEachConsole()
    {
        var owned = CatalogMatcher.Match(Games,
        [
            ("gba", "/dl/gba/Advance Wars (USA).gba"),
            ("ps3", "/dl/ps3/Heavy Title (USA).iso"),
            ("snes", "/dl/snes/Advance Wars (USA).sfc"),
        ]);
        Assert.HasCount(3, owned); // game 1 (gba), 3 (ps3), 2 (snes)
        Assert.IsTrue(owned.ContainsKey(2));
        Assert.IsTrue(owned.ContainsKey(3));
    }
}
