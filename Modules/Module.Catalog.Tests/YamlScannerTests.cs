[TestClass]
public class YamlScannerTests
{
    private const string Doc = """
        # a comment line
        SLUS-20062:
          name: "Final Fantasy: X"
          region: "NTSC-U"
          compat: 5
        SLUS-00067:
          compatibility:
            rating: NoIssues
          controllers:
            - DigitalController
        """;

    [TestMethod]
    public void Scan_TracksIndentKeyValue_SkipsCommentsAndListItems()
    {
        var lines = YamlScanner.Scan(Doc).ToList();
        // The comment and the "- DigitalController" list item never surface as scalar lines.
        Assert.IsFalse(lines.Any(l => l.Key.StartsWith('#') || l.Key.StartsWith('-')));

        var serial = lines[0];
        Assert.AreEqual(0, serial.Indent);
        Assert.AreEqual("SLUS-20062", serial.Key);
        Assert.IsNull(serial.Value);                 // key-only header

        var compat = lines.Single(l => l.Key == "compat");
        Assert.AreEqual(2, compat.Indent);
        Assert.AreEqual("5", compat.Value);

        var rating = lines.Single(l => l.Key == "rating");
        Assert.AreEqual(4, rating.Indent);           // nested under compatibility:
        Assert.AreEqual("NoIssues", rating.Value);

        // The nested-map opener is a key-only line at indent 2.
        var compatibility = lines.Single(l => l.Key == "compatibility");
        Assert.AreEqual(2, compatibility.Indent);
        Assert.IsNull(compatibility.Value);
    }

    [TestMethod]
    public void Scan_StripsQuotes_AndKeepsColonsInsideValues()
    {
        var name = YamlScanner.Scan(Doc).Single(l => l.Key == "name");
        Assert.AreEqual("Final Fantasy: X", name.Value); // surrounding quotes stripped, inner colon kept
    }

    [TestMethod]
    public void Scan_EmptyOrBlank_YieldsNothing()
    {
        Assert.IsEmpty(YamlScanner.Scan(""));
        Assert.IsEmpty(YamlScanner.Scan("\n\n   \n"));
    }
}
