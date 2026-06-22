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

    [TestMethod]
    public void Excluded_And_All_AreDisjoint()
    {
        // A DAT is either synced (All) or deliberately skipped (ExcludedDats) — never both, or the
        // exclusion is a lie. ExcludedDats keys are DatNames, so compare on DatName.
        var synced = CatalogSystems.All.Select(s => s.DatName).ToHashSet();
        var overlap = CatalogSystems.ExcludedDats.Keys.Where(synced.Contains).ToList();
        Assert.IsEmpty(overlap, $"DATs both synced and excluded: {string.Join(", ", overlap)}");
    }

    [TestMethod]
    public void Excluded_AllHaveReasons()
    {
        foreach (var (dat, reason) in CatalogSystems.ExcludedDats)
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason), $"{dat} → blank exclusion reason");
    }

    [TestMethod]
    public void All_Covers_ExpandedConsoleHandheldSet()
    {
        // Locks the D1 (#128) coverage expansion: every base console/handheld DAT that was added must
        // stay present. Guards against an accidental revert of the broadened spread.
        string[] mustHave =
        [
            // Sega
            "Sega - Naomi 2", "Sega - PICO", "Sega - Beena",
            // obscure carts / handhelds
            "Benesse - Pocket Challenge V2", "Casio - Loopy", "Entex - Adventure Vision",
            "Epoch - Super Cassette Vision", "Funtech - Super Acan", "GamePark - GP32",
            "Hartung - Game Master", "Interton - VC 4000", "Konami - Picno",
            "LeapFrog - LeapPad", "LeapFrog - Leapster Learning Game System",
            "RCA - Studio II", "Mobile - Zeebo",
        ];
        var synced = CatalogSystems.All.Select(s => s.DatName).ToHashSet();
        var missing = mustHave.Where(d => !synced.Contains(d)).ToList();
        Assert.IsEmpty(missing, $"expanded coverage dropped: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void All_DoesNotSync_DigitalAndVariantDats()
    {
        // The digital / non-game / folder-colliding variants must NOT be synced as their own systems
        // (they belong in ExcludedDats, where the dedup epic #119 D2 will fold them onto physical twins).
        string[] mustNotHave =
        [
            "Sony - PlayStation 3 (PSN)", "Sony - PlayStation Portable (UMD Video)",
            "Nintendo - Wii (Digital)", "Nintendo - Wii U (Digital)", "Nintendo - Nintendo DSi",
            "Nintendo - New Nintendo 3DS", "Nintendo - e-Reader", "Microsoft - Xbox 360 (Digital)",
        ];
        var synced = CatalogSystems.All.Select(s => s.DatName).ToHashSet();
        var leaked = mustNotHave.Where(synced.Contains).ToList();
        Assert.IsEmpty(leaked, $"digital/variant DATs leaked into the synced set: {string.Join(", ", leaked)}");
    }
}
