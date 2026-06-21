/// <summary>
/// Guards the <see cref="CatalogSystems.All"/> registry invariants: it is a data table that is easy
/// to fat-finger (duplicate console folder, duplicate DAT, wrong group), so these are the cheap
/// compile-time-ish checks the sync relies on.
/// </summary>
[TestClass]
public class CatalogSystemsTests
{
    [TestMethod]
    public void All_ConsoleTags_AreUnique()
    {
        var dupes = CatalogSystems.All
            .GroupBy(s => s.Console)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.IsEmpty(dupes, $"duplicate console folders: {string.Join(", ", dupes)}");
    }

    [TestMethod]
    public void All_DatNames_AreUnique()
    {
        var dupes = CatalogSystems.All
            .GroupBy(s => s.DatName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.IsEmpty(dupes, $"duplicate DAT names: {string.Join(", ", dupes)}");
    }

    [TestMethod]
    public void All_Groups_AreNoIntroOrRedump()
    {
        foreach (var s in CatalogSystems.All)
            Assert.IsTrue(s.Group is "no-intro" or "redump", $"{s.DatName} → bad group '{s.Group}'");
    }

    [TestMethod]
    public void All_HasNoBlankFields()
    {
        foreach (var s in CatalogSystems.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.DatName), "blank DatName");
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Console), $"{s.DatName} → blank Console");
        }
    }
}
