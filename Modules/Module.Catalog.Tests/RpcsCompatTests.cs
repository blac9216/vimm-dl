[TestClass]
public class RpcsCompatTests
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
    public void Parse_YieldsSerialStatusPairs()
    {
        var map = RpcsCompat.Parse(Export).ToDictionary(x => x.Serial, x => x.Status);
        Assert.HasCount(2, map);                 // the entry with no status is skipped
        Assert.AreEqual("Playable", map["BLES00932"]);
        Assert.AreEqual("Ingame", map["BCJS30022"]);
    }

    [TestMethod]
    public void Parse_EmptyOrNoResults_YieldsNothing()
    {
        Assert.IsEmpty(RpcsCompat.Parse("{}"));
        Assert.IsEmpty(RpcsCompat.Parse("""{"return_code":1,"results":[]}"""));
    }

    [TestMethod]
    [DataRow("BLES-00932", "BLES00932")]
    [DataRow("blus 30443", "BLUS30443")]
    [DataRow("BCAS20016", "BCAS20016")]
    public void NormalizeSerial_StripsPunctuationAndUppercases(string input, string expected)
        => Assert.AreEqual(expected, RpcsCompat.NormalizeSerial(input));
}
