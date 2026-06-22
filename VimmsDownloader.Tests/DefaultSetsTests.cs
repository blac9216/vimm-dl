namespace VimmsDownloader.Tests;

/// <summary>
/// Sanity-checks the default download sets ported from RomGoGetter (host-internal, via
/// InternalsVisibleTo): the four Sony sets, their link counts, that every console is one the
/// catalog actually syncs, and that the built links are valid archive.org /download/ URLs.
/// </summary>
[TestClass]
public class DefaultSetsTests
{
    [TestMethod]
    public void All_HasTheFourSonySetsWithExpectedLinkCounts()
    {
        Assert.HasCount(8, DefaultSets.All);
        var byName = DefaultSets.All.ToDictionary(s => s.Name);
        Assert.HasCount(5, byName["PS1 Archive"].Items);
        Assert.HasCount(32, byName["PS2 Archive"].Items);
        Assert.HasCount(66, byName["PS3 Archive"].Items);
        Assert.HasCount(2, byName["PSP Archive"].Items);
        Assert.HasCount(34, byName["Xbox Archive"].Items);
        Assert.HasCount(39, byName["Xbox 360 Archive"].Items);
        Assert.HasCount(2, byName["3DS Encrypted Archive"].Items);
        Assert.HasCount(1, byName["NDS Archive"].Items);
    }

    [TestMethod]
    public void All_ConsolesAreCatalogConsoles()
    {
        var consoles = Module.Catalog.CatalogSystems.All.Select(s => s.Console).ToHashSet();
        foreach (var set in DefaultSets.All)
            Assert.Contains(set.Console, consoles, $"{set.Name} → non-catalog console '{set.Console}'");
    }

    [TestMethod]
    public void Links_BuildsArchiveDownloadUrls_NoDuplicates_AllResolvable()
    {
        foreach (var set in DefaultSets.All)
        {
            var links = DefaultSets.Links(set.Items);
            Assert.HasCount(set.Items.Length, links);
            Assert.IsTrue(links.All(l => l.StartsWith("https://archive.org/download/")), set.Name);
            Assert.HasCount(links.Length, links.Distinct().ToList());                      // no dup links in a set
            Assert.IsTrue(links.All(l => CatalogResolveService.ArchiveIdentifier(l) != null), set.Name);
        }
    }
}
