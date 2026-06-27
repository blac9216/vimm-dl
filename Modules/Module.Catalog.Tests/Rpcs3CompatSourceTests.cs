[TestClass]
public class Rpcs3CompatSourceTests
{
    private const string Export = """
        {
          "return_code": 0,
          "results": {
            "BLES00932": { "title": "Demon's Souls", "status": "Playable", "date": "2020-05-04" },
            "BCJS30022": { "title": "Demon's Souls", "status": "Ingame" },
            "NOSTATUS":  { "title": "Broken" }
          }
        }
        """;

    [TestMethod]
    public void Parse_YieldsMatchKeyStatusPairs()
    {
        var map = new Rpcs3CompatSource().Parse(Export).ToDictionary(x => x.MatchKey, x => x.Status);
        Assert.HasCount(2, map);                 // the entry with no status is skipped
        Assert.AreEqual("Playable", map["BLES00932"]);
        Assert.AreEqual("Ingame", map["BCJS30022"]);
    }

    [TestMethod]
    public void Parse_EmptyOrNoResults_YieldsNothing()
    {
        Assert.IsEmpty(new Rpcs3CompatSource().Parse("{}"));
        Assert.IsEmpty(new Rpcs3CompatSource().Parse("""{"return_code":1,"results":[]}"""));
    }

    [TestMethod]
    public void Source_FeedsARegisteredSerialKeyedEmulator()
    {
        var source = new Rpcs3CompatSource();
        Assert.AreEqual("rpcs3", source.EmulatorId);
        var emu = Emulators.ById(source.EmulatorId);
        Assert.IsNotNull(emu);                                   // every source maps to a registered emulator
        Assert.AreEqual(CompatMatchKind.Serial, emu!.MatchKind); // RPCS3 joins by serial
        Assert.AreEqual("serial", Emulators.Token(emu.MatchKind));
    }

    [TestMethod]
    public void CompatSources_AllMapToRegisteredEmulators()
    {
        Assert.IsNotEmpty(CompatSources.All);
        foreach (var source in CompatSources.All)
            Assert.IsNotNull(Emulators.ById(source.EmulatorId), $"source {source.EmulatorId} has no registered emulator");
    }

    [TestMethod]
    [DataRow("BLES-00932", "BLES00932")]
    [DataRow("blus 30443", "BLUS30443")]
    [DataRow("BCAS20016", "BCAS20016")]
    public void NormalizeSerial_StripsPunctuationAndUppercases(string input, string expected)
        => Assert.AreEqual(expected, CompatKeys.NormalizeSerial(input));
}
