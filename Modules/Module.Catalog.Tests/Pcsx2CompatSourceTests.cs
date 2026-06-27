[TestClass]
public class Pcsx2CompatSourceTests
{
    private const string Index = """
        # PCSX2 Game Database
        SLUS-20062:
          name: "Final Fantasy X"
          compat: 5
        SCUS-97328:
          name: "God of War"
          compat: 6
        SLES-50001:
          name: "Menu Only"
          compat: 3
        PBPX-95205:
          name: "Untested"
          compat: 0
        SLUS-12345:
          name: "No compat field"
          region: "NTSC-U"
        """;

    [TestMethod]
    public void Parse_MapsRatings_NormalizesSerial_SkipsUnknownAndMissing()
    {
        var map = new Pcsx2CompatSource().Parse(Index).ToDictionary(e => e.MatchKey, e => e.Status);
        Assert.AreEqual("Playable", map["SLUS20062"]); // 5 Playable → Playable, serial dash stripped
        Assert.AreEqual("Playable", map["SCUS97328"]); // 6 Perfect → Playable
        Assert.AreEqual("Intro", map["SLES50001"]);    // 3 Menu → Intro
        Assert.IsFalse(map.ContainsKey("PBPX95205"));  // 0 Unknown → skipped
        Assert.IsFalse(map.ContainsKey("SLUS12345"));  // no compat field → skipped
        Assert.HasCount(3, map);
    }

    [TestMethod]
    public void Source_FeedsRegisteredPs2SerialEmulator()
    {
        var emu = Emulators.ById(new Pcsx2CompatSource().EmulatorId);
        Assert.IsNotNull(emu);
        Assert.AreEqual("ps2", emu!.Console);
        Assert.AreEqual(CompatMatchKind.Serial, emu.MatchKind);
    }
}
