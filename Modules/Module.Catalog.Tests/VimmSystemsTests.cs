/// <summary>
/// Guards the Vimm console→vault-code mapping: unique on both sides, every mapped console is a real
/// catalog console (so a Vimm sync targets a folder the catalog actually has), and the lookup helper.
/// </summary>
[TestClass]
public class VimmSystemsTests
{
    [TestMethod]
    public void All_VimmCodes_AreUnique()
    {
        var dupes = VimmSystems.All.GroupBy(s => s.VimmCode).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.IsEmpty(dupes, $"duplicate Vimm codes: {string.Join(", ", dupes)}");
    }

    [TestMethod]
    public void All_Consoles_AreUnique()
    {
        var dupes = VimmSystems.All.GroupBy(s => s.Console).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.IsEmpty(dupes, $"duplicate consoles: {string.Join(", ", dupes)}");
    }

    [TestMethod]
    public void All_Consoles_AreCatalogConsoles()
    {
        var catalog = CatalogSystems.All.Select(s => s.Console).ToHashSet();
        foreach (var s in VimmSystems.All)
            Assert.Contains(s.Console, catalog, $"Vimm maps '{s.Console}' which is not a catalog console");
    }

    [TestMethod]
    public void CodeFor_ReturnsCode_OrNullWhenVimmDoesNotCarryIt()
    {
        Assert.AreEqual("PS1", VimmSystems.CodeFor("psx"));
        Assert.AreEqual("GameCube", VimmSystems.CodeFor("gc"));
        Assert.AreEqual("TG16", VimmSystems.CodeFor("pcengine"));
        Assert.IsNull(VimmSystems.CodeFor("c64"));   // Vimm doesn't host Commodore 64
        Assert.IsNull(VimmSystems.CodeFor("nope"));
    }
}
