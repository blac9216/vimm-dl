[TestClass]
public class DuckStationCompatSourceTests
{
    private const string GameDb = """
        SLUS-00067:
          name: "Ridge Racer"
          compatibility:
            rating: NoIssues
            versionTested: "0.1-1308"
          controllers:
            - DigitalController
        SCUS-94900:
          name: "Crash Glitchy"
          compatibility:
            rating: GraphicalAudioIssues
        SLPS-00001:
          name: "Crashes Early"
          compatibility:
            rating: CrashesInIntro
        SLES-00001:
          name: "Dead On Boot"
          compatibility:
            rating: DoesntBoot
        SLUS-99999:
          name: "Untested"
          compatibility:
            rating: Unknown
        SLUS-11111:
          name: "No compat block"
          region: "NTSC-U"
        """;

    [TestMethod]
    public void Parse_MapsRatings_NormalizesSerial_SkipsUnknownAndMissing()
    {
        var map = new DuckStationCompatSource().Parse(GameDb).ToDictionary(e => e.MatchKey, e => e.Status);
        Assert.AreEqual("Playable", map["SLUS00067"]);  // NoIssues → Playable
        Assert.AreEqual("Ingame", map["SCUS94900"]);    // GraphicalAudioIssues → Ingame
        Assert.AreEqual("Intro", map["SLPS00001"]);     // CrashesInIntro → Intro
        Assert.AreEqual("Nothing", map["SLES00001"]);   // DoesntBoot → Nothing
        Assert.IsFalse(map.ContainsKey("SLUS99999"));   // Unknown → skipped
        Assert.IsFalse(map.ContainsKey("SLUS11111"));   // no compatibility block → skipped
        Assert.HasCount(4, map);
    }

    [TestMethod]
    public void Parse_OnlyTakesRatingNestedUnderCompatibility_NotOtherFields()
    {
        // versionTested + the controllers list must never be mistaken for a rating.
        var entries = new DuckStationCompatSource().Parse(GameDb).ToList();
        Assert.IsTrue(entries.All(e => e.Status is "Playable" or "Ingame" or "Intro" or "Loadable" or "Nothing"));
    }

    [TestMethod]
    public void Source_FeedsRegisteredPs1SerialEmulator()
    {
        var emu = Emulators.ById(new DuckStationCompatSource().EmulatorId);
        Assert.IsNotNull(emu);
        Assert.AreEqual("psx", emu!.Console);
        Assert.AreEqual(CompatMatchKind.Serial, emu.MatchKind);
    }
}
