[TestClass]
public class DedupTests
{
    [TestMethod]
    public void TitleKey_GroupsRegionalAndRevisionVariants()
    {
        var a = Dedup.TitleKey("Advance Wars (USA) (Rev 1)");
        var b = Dedup.TitleKey("Advance Wars (Europe)");
        var c = Dedup.TitleKey("Advance Wars (Japan) [b]");
        Assert.AreEqual(a, b);
        Assert.AreEqual(a, c);
        Assert.AreEqual("advance wars", a);
    }

    [TestMethod]
    public void TitleKey_PlainNameNormalized()
        => Assert.AreEqual("tony hawk s pro skater 2", Dedup.TitleKey("Tony Hawk's Pro Skater 2"));

    [TestMethod]
    public void SelectParent_PrefersUsaOverJapan()
    {
        var games = new (string, string?)[] { ("Game (Japan)", "Japan"), ("Game (USA)", "USA") };
        Assert.AreEqual(1, Dedup.SelectParent(games));
    }

    [TestMethod]
    public void SelectParent_PrefersHigherRevision_SameRegion()
    {
        var games = new (string, string?)[] { ("Game (USA)", "USA"), ("Game (USA) (Rev 2)", "USA") };
        Assert.AreEqual(1, Dedup.SelectParent(games));
    }

    [TestMethod]
    public void SelectParent_AvoidsBetaUnlessOnlyOption()
    {
        var games = new (string, string?)[] { ("Game (USA) (Beta)", "USA"), ("Game (Europe)", "Europe") };
        Assert.AreEqual(1, Dedup.SelectParent(games)); // the non-beta Europe release wins over a USA beta
    }

    [TestMethod]
    public void SelectParent_OnlyBeta_StillSelected()
    {
        var games = new (string, string?)[] { ("Game (USA) (Proto)", "USA") };
        Assert.AreEqual(0, Dedup.SelectParent(games));
    }

    [TestMethod]
    public void SelectParent_UsesRegionField_WhenNamesAmbiguous()
    {
        var games = new (string, string?)[] { ("Game", "Japan"), ("Game", "USA") };
        Assert.AreEqual(1, Dedup.SelectParent(games));
    }
}
