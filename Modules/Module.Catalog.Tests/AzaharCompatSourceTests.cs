[TestClass]
public class AzaharCompatSourceTests
{
    private const string List = """
        [
          {"compatibility": 0, "title": "Super Mario 3D Land", "releases": [{"id": "0004000000054100"}]},
          {"compatibility": 2, "title": "Mario Kart 7", "releases": [{"id": "0004000000030800"}]},
          {"compatibility": 4, "title": "Some Buggy Game", "releases": [{"id": "0004000000000001"}]},
          {"compatibility": 5, "title": "Wont Boot Game", "releases": [{"id": "0004000000000002"}]},
          {"compatibility": 99, "title": "Untested Game", "releases": [{"id": "0004000000000003"}]}
        ]
        """;

    [TestMethod]
    public void Parse_MapsByName_NormalizesTitle_SkipsUntested()
    {
        var map = new AzaharCompatSource().Parse(List).ToDictionary(e => e.MatchKey, e => e.Status);
        Assert.AreEqual("Playable", map["super mario 3d land"]); // 0 Perfect; title → title_key
        Assert.AreEqual("Ingame", map["mario kart 7"]);          // 2 Okay
        Assert.AreEqual("Intro", map["some buggy game"]);        // 4 Intro/Menu
        Assert.AreEqual("Nothing", map["wont boot game"]);       // 5 Won't Boot
        Assert.IsFalse(map.ContainsKey("untested game"));        // 99 Not Tested → skipped
        Assert.HasCount(4, map);
    }

    [TestMethod]
    [DataRow(0, "Playable")]
    [DataRow(1, "Playable")]
    [DataRow(2, "Ingame")]
    [DataRow(3, "Ingame")]
    [DataRow(4, "Intro")]
    [DataRow(5, "Nothing")]
    public void MapStatus_MapsRatedLevels(int level, string expected)
        => Assert.AreEqual(expected, AzaharCompatSource.MapStatus(level));

    [TestMethod]
    public void MapStatus_NotTestedOrUnknown_Skipped()
    {
        Assert.IsNull(AzaharCompatSource.MapStatus(99));
        Assert.IsNull(AzaharCompatSource.MapStatus(7));
    }

    [TestMethod]
    public void Source_FeedsRegisteredNameKeyed3dsEmulator()
    {
        var emu = Emulators.ById(new AzaharCompatSource().EmulatorId);
        Assert.IsNotNull(emu);
        Assert.AreEqual(CompatMatchKind.Name, emu!.MatchKind);
        CollectionAssert.AreEquivalent(new[] { "n3ds" }, emu.Consoles.ToList());
    }
}
