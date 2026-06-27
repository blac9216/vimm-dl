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

    [TestMethod]
    public void SelectParent_AvoidsKiosk_NowTreatedAsBadDump()
    {
        // (Kiosk) joined the non-final category set, so a kiosk build never wins over a retail release.
        var games = new (string, string?)[] { ("Game (USA) (Kiosk)", "USA"), ("Game (Europe)", "Europe") };
        Assert.AreEqual(1, Dedup.SelectParent(games));
    }

    [TestMethod]
    public void IsExcludedVariant_DetectsNonFinalCategories()
    {
        Assert.IsTrue(Dedup.IsExcludedVariant("Game (USA) (Demo)"));
        Assert.IsTrue(Dedup.IsExcludedVariant("Game (Europe) (Beta)"));
        Assert.IsTrue(Dedup.IsExcludedVariant("Game (Japan) (Proto)"));
        Assert.IsTrue(Dedup.IsExcludedVariant("Game (World) (Prototype)")); // "(proto" covers "(prototype)"
        Assert.IsTrue(Dedup.IsExcludedVariant("Game (USA) (Sample)"));
        Assert.IsTrue(Dedup.IsExcludedVariant("Game (USA) (Kiosk)"));
    }

    [TestMethod]
    public void IsExcludedVariant_AllowsRetailReleases()
    {
        Assert.IsFalse(Dedup.IsExcludedVariant("Game (USA)"));
        Assert.IsFalse(Dedup.IsExcludedVariant("Game (Europe) (Rev 1)"));
    }

    [TestMethod]
    public void IsEnglish_TrueForWesternRegion()
    {
        Assert.IsTrue(Dedup.IsEnglish("USA", null, "Game (USA)"));
        Assert.IsTrue(Dedup.IsEnglish("Europe", null, "Game (Europe)"));
        Assert.IsTrue(Dedup.IsEnglish("World", null, "Game (World)"));
        Assert.IsTrue(Dedup.IsEnglish("Australia", null, "Game (Australia)"));
    }

    [TestMethod]
    public void IsEnglish_TrueWhenLanguagesIncludeEn_EvenForJapanRegion()
        => Assert.IsTrue(Dedup.IsEnglish("Japan", "En,Ja", "Game (Japan) (En,Ja)"));

    [TestMethod]
    public void IsEnglish_FalseForJapanOnly()
    {
        Assert.IsFalse(Dedup.IsEnglish("Japan", "Ja", "Game (Japan)"));
        Assert.IsFalse(Dedup.IsEnglish("Japan", null, "Game (Japan)"));
    }

    [TestMethod]
    public void IsEnglish_FallsBackToNameRegionTag_WhenRegionEmpty()
        => Assert.IsTrue(Dedup.IsEnglish(null, null, "Game (USA)"));

    [TestMethod]
    public void IsEnglish_RegionEmpty_DoesNotMatchUkBigramInsideJapaneseName()
    {
        // #197: "uk" lives inside these real Japanese titles, not as a "(UK)" region tag — the
        // boundary-aware match must not classify them English when the region column is empty.
        Assert.IsFalse(Dedup.IsEnglish(null, null, "Yuukyuu Gensoukyoku (Japan)")); // "uk" in "Yuukyuu"
        Assert.IsFalse(Dedup.IsEnglish(null, null, "Sukeban Deka (Japan)"));        // "uk" in "Sukeban"
    }

    [TestMethod]
    public void IsEnglish_RegionEmpty_StillMatchesGenuineUkTag()
        => Assert.IsTrue(Dedup.IsEnglish(null, null, "Some Game (UK)")); // a real (UK) release still counts

    [TestMethod]
    public void SelectParent_RegionEmptyJapaneseName_NotMisrankedAsEuropeanByUkBigram()
    {
        // #197: the Japan variant has an empty region and "uk" inside "Sukeban"; without boundary-aware
        // matching its name-fallback rank was Europe (1), tying and keeping it over the real Europe
        // release. It must now rank as Japan (2), so the Europe variant wins 1G1R.
        var games = new (string, string?)[] { ("Sukeban Deka (Japan)", null), ("Sukeban Deka (Europe)", "Europe") };
        Assert.AreEqual(1, Dedup.SelectParent(games));
    }

    [TestMethod]
    public void IsEnglishAndRegionRank_AgreeOnWesternTokens()
    {
        // #201: new zealand / ireland / scandinavia are accepted by IsEnglish, so RegionRank must also
        // treat them as Western (rank 1) — i.e. ahead of a Japan variant (rank 2) in 1G1R selection.
        foreach (var region in new[] { "New Zealand", "Ireland", "Scandinavia" })
        {
            Assert.IsTrue(Dedup.IsEnglish(region, null, $"Game ({region})"), $"{region} should be English");
            var games = new (string, string?)[] { ("Game (Japan)", "Japan"), ($"Game ({region})", region) };
            Assert.AreEqual(1, Dedup.SelectParent(games), $"{region} should win 1G1R over Japan");
        }
    }
}
