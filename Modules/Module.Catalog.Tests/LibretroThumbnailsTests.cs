/// <summary>
/// Guards the pure libretro-thumbnails naming rules the image cache is built on (epic #122 / M1): the
/// character-substitution rule, the DAT-name → system-folder map, the exact-then-truncated name
/// candidates, and the final CDN URL shape. No network — the host MediaService does the fetch.
/// </summary>
[TestClass]
public class LibretroThumbnailsTests
{
    [TestMethod]
    public void Sanitize_ReplacesEachSpecialCharWithUnderscore()
    {
        // Every char in the libretro rule set → '_'; one per position so the mapping is unambiguous.
        Assert.AreEqual("_ _ _ _ _ _ _ _ _ _ _",
            LibretroThumbnails.Sanitize("& * / : ` < > ? \\ | \""));
    }

    [TestMethod]
    public void Sanitize_PreservesSpacesParensAndOrdinaryText()
    {
        Assert.AreEqual("Mega Man 2 (USA)", LibretroThumbnails.Sanitize("Mega Man 2 (USA)"));
        // Ampersand and colon are replaced; the surrounding spaces and parens stay.
        Assert.AreEqual("Pokemon Red _ Blue (USA, Europe)",
            LibretroThumbnails.Sanitize("Pokemon Red & Blue (USA, Europe)"));
        Assert.AreEqual("Sonic _ Knuckles (USA)", LibretroThumbnails.Sanitize("Sonic & Knuckles (USA)"));
    }

    [TestMethod]
    public void SystemFolder_DefaultsToTheDatName()
    {
        Assert.AreEqual("Nintendo - Nintendo Entertainment System",
            LibretroThumbnails.SystemFolder("Nintendo - Nintendo Entertainment System"));
        Assert.AreEqual("Sony - PlayStation", LibretroThumbnails.SystemFolder("Sony - PlayStation"));
    }

    [TestMethod]
    public void SystemFolder_AppliesKnownOverrides()
    {
        Assert.AreEqual("Philips - Videopac", LibretroThumbnails.SystemFolder("Philips - Videopac+"));
        Assert.AreEqual("Sinclair - ZX Spectrum", LibretroThumbnails.SystemFolder("Sinclair - ZX Spectrum +3"));
    }

    [TestMethod]
    public void SystemFolder_EveryCatalogSystemMapsToANonEmptyFolder()
    {
        // Guards against a future CatalogSystems entry whose name would break URL building.
        foreach (var s in CatalogSystems.All)
            Assert.IsFalse(string.IsNullOrWhiteSpace(LibretroThumbnails.SystemFolder(s.DatName)),
                $"{s.DatName} → empty thumbnail system folder");
    }

    [TestMethod]
    public void NameCandidates_ExactThenTruncatedBeforeFirstParen()
    {
        CollectionAssert.AreEqual(
            new[] { "Mega Man 2 (USA)", "Mega Man 2" },
            LibretroThumbnails.NameCandidates("Mega Man 2 (USA)").ToArray());
    }

    [TestMethod]
    public void NameCandidates_NoParen_SingleCandidate()
    {
        CollectionAssert.AreEqual(
            new[] { "Tetris" },
            LibretroThumbnails.NameCandidates("Tetris").ToArray());
    }

    [TestMethod]
    public void NameCandidates_AreSanitized()
    {
        // The '&' is substituted in both the exact and truncated candidates.
        CollectionAssert.AreEqual(
            new[] { "Sonic _ Knuckles (USA)", "Sonic _ Knuckles" },
            LibretroThumbnails.NameCandidates("Sonic & Knuckles (USA)").ToArray());
    }

    [TestMethod]
    public void IsKnownType_BoxartAndTitle_CaseInsensitive()
    {
        Assert.IsTrue(LibretroThumbnails.IsKnownType("boxart"));
        Assert.IsTrue(LibretroThumbnails.IsKnownType("title"));
        Assert.IsTrue(LibretroThumbnails.IsKnownType("BoxArt"));
        Assert.IsFalse(LibretroThumbnails.IsKnownType("snap"));
        Assert.IsFalse(LibretroThumbnails.IsKnownType(""));
    }

    [TestMethod]
    public void TypeFolder_MapsToCdnFolders()
    {
        Assert.AreEqual("Named_Boxarts", LibretroThumbnails.TypeFolder("boxart"));
        Assert.AreEqual("Named_Titles", LibretroThumbnails.TypeFolder("title"));
    }

    [TestMethod]
    public void Url_EncodesSystemAndName_KeepsTypeFolderLiteral()
    {
        var url = LibretroThumbnails.Url("Nintendo - Nintendo Entertainment System", "Named_Boxarts", "Mega Man 2 (USA)");
        Assert.AreEqual(
            "https://thumbnails.libretro.com/Nintendo%20-%20Nintendo%20Entertainment%20System/Named_Boxarts/Mega%20Man%202%20%28USA%29.png",
            url);
    }

    [TestMethod]
    public void Urls_BuildsOrderedBoxartCandidatesForAGame()
    {
        var urls = LibretroThumbnails.Urls("Nintendo - Nintendo Entertainment System", "boxart", "Mega Man 2 (USA)").ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "https://thumbnails.libretro.com/Nintendo%20-%20Nintendo%20Entertainment%20System/Named_Boxarts/Mega%20Man%202%20%28USA%29.png",
            "https://thumbnails.libretro.com/Nintendo%20-%20Nintendo%20Entertainment%20System/Named_Boxarts/Mega%20Man%202.png",
        }, urls);
    }

    [TestMethod]
    public void Urls_TitleType_UsesTitleFolder()
    {
        var url = LibretroThumbnails.Urls("Sony - PlayStation", "title", "Final Fantasy VII (USA)").First();
        StringAssert.Contains(url, "/Sony%20-%20PlayStation/Named_Titles/");
    }
}
