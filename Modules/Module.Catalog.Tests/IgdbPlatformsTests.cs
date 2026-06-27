/// <summary>
/// Guards the console → IGDB platform-id map (epic #122 / M2): known mappings (incl. the regional-twin
/// double maps), unmapped consoles returning null, and that every map key is a real CatalogSystems
/// console slug (no typos drift the join silently).
/// </summary>
[TestClass]
public class IgdbPlatformsTests
{
    [TestMethod]
    public void Ids_ReturnsMappedPlatformIds()
    {
        CollectionAssert.AreEqual(new[] { 18, 99 }, IgdbPlatforms.Ids("nes"));   // NES + Famicom
        CollectionAssert.AreEqual(new[] { 19, 58 }, IgdbPlatforms.Ids("snes"));  // SNES + Super Famicom
        CollectionAssert.AreEqual(new[] { 7 }, IgdbPlatforms.Ids("psx"));
        CollectionAssert.AreEqual(new[] { 9 }, IgdbPlatforms.Ids("ps3"));
    }

    [TestMethod]
    public void Ids_UnmappedConsole_ReturnsNull()
    {
        Assert.IsNull(IgdbPlatforms.Ids("satellaview")); // niche, no IGDB platform mapped
        Assert.IsNull(IgdbPlatforms.Ids("not-a-console"));
    }

    [TestMethod]
    public void Map_PlatformIdsArePositive()
    {
        foreach (var (console, ids) in IgdbPlatforms.ByConsole)
        {
            Assert.IsNotEmpty(ids, $"{console} maps to an empty id list");
            Assert.IsTrue(ids.All(id => id > 0), $"{console} has a non-positive IGDB id");
        }
    }

    [TestMethod]
    public void Map_EveryKeyIsARealCatalogConsole()
    {
        var consoles = CatalogSystems.All.Select(s => s.Console).ToHashSet();
        foreach (var console in IgdbPlatforms.ByConsole.Keys)
            Assert.Contains(console, consoles, $"IGDB map key '{console}' is not a CatalogSystems console");
    }
}
