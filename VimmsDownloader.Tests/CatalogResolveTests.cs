namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises CatalogResolveService.ArchiveIdentifier (host-internal, reachable via InternalsVisibleTo)
/// — the parser that turns a set link into an archive.org item id (or null for non-archive links).
/// </summary>
[TestClass]
public class CatalogResolveTests
{
    [TestMethod]
    [DataRow("https://archive.org/download/sony_playstation_part1", "sony_playstation_part1")]
    [DataRow("https://archive.org/download/tp-roms_0/TeknoParrot/", "tp-roms_0")] // trailing subpath ignored
    [DataRow("https://archive.org/details/some_item", "some_item")]
    [DataRow("https://ia600407.us.archive.org/view_archive.php?archive=/x.zip", null)] // archive host but not a download/details path
    [DataRow("https://lolroms.com/Nintendo%20-%20DS", null)]                            // non-archive host → skipped
    [DataRow("https://minerva-archive.org/browse/Redump/", null)]                       // look-alike host, not archive.org
    [DataRow("sony_playstation_part1", "sony_playstation_part1")]                       // bare identifier
    [DataRow("  https://archive.org/download/abc  ", "abc")]                            // trimmed
    public void ArchiveIdentifier_ParsesArchiveLinks(string link, string? expected)
        => Assert.AreEqual(expected, CatalogResolveService.ArchiveIdentifier(link));
}
