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

    /// <summary>
    /// The two Vimm registries mean opposite things and must stay disjoint: a console in
    /// <see cref="VimmSystems"/> is hash-BOUND onto DAT-sourced games, while one in
    /// <see cref="VimmSourceSystems"/> is SEEDED from Vimm. Listing a console in both would run the
    /// bind path against rows Vimm itself created, or scrape a console for zero possible matches.
    /// </summary>
    [TestMethod]
    public void VimmSourceSystems_AndVimmSystems_AreDisjoint()
    {
        var bound = VimmSystems.All.Select(s => s.Console).ToHashSet();
        var overlap = VimmSourceSystems.All.Where(s => bound.Contains(s.Console)).Select(s => s.Console).ToList();
        Assert.IsEmpty(overlap, $"console(s) in both Vimm registries: {string.Join(", ", overlap)}");
    }

    /// <summary>A Vimm-seeded system's dat_name names a scrape, so it must not collide with a real DAT.</summary>
    [TestMethod]
    public void VimmSourceSystems_DatNames_DoNotCollideWithRealDats()
    {
        var dats = CatalogSystems.All.Select(s => s.DatName).ToHashSet();
        foreach (var s in VimmSourceSystems.All)
            Assert.DoesNotContain(s.DatName, dats, $"{s.DatName} collides with a real DAT name");
    }

    [TestMethod]
    public void VimmSourceSystems_ConsolesAreCatalogConsoles()
    {
        var consoles = CatalogSystems.All.Select(s => s.Console).ToHashSet();
        foreach (var s in VimmSourceSystems.All)
            Assert.Contains(s.Console, consoles, $"Vimm-seeded console '{s.Console}' is not a catalog console");
    }

    [TestMethod]
    public void VimmSourceSystems_For_ResolvesOrReturnsNull()
    {
        Assert.AreEqual("WiiU", VimmSourceSystems.For("wiiu")?.VimmCode);
        Assert.IsNull(VimmSourceSystems.For("snes"));   // DAT-sourced, not Vimm-authoritative
    }
}
