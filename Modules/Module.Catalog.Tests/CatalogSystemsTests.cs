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
        //
        // Wii U is the one deliberate exception, asserted separately below: its digital CDN DAT IS
        // synced because the physical twin it would fold onto does not exist in the catalog — Redump's
        // Wii U disc set has no public DAT — so excluding it would leave Wii U with no identity layer
        // at all rather than deduplicating it against something (#266).
        string[] mustNotHave =
        [
            "Sony - PlayStation 3 (PSN)", "Sony - PlayStation Portable (UMD Video)",
            "Nintendo - Wii (Digital)", "Nintendo - Nintendo DSi",
            "Nintendo - New Nintendo 3DS", "Nintendo - e-Reader", "Microsoft - Xbox 360 (Digital)",
        ];
        var synced = CatalogSystems.All.Select(s => s.DatName).ToHashSet();
        var leaked = mustNotHave.Where(synced.Contains).ToList();
        Assert.IsEmpty(leaked, $"digital/variant DATs leaked into the synced set: {string.Join(", ", leaked)}");
    }

    /// <summary>
    /// The Wii U digital exception is intentional and load-bearing for the NUS path — pin it, so
    /// removing it is a deliberate act rather than a quiet regression. Its dev/lotcheck/deprecated
    /// siblings stay excluded.
    /// </summary>
    [TestMethod]
    public void All_Syncs_WiiUDigitalCdn_ButNotItsVariants()
    {
        var synced = CatalogSystems.All.Select(s => s.DatName).ToHashSet();

        Assert.Contains("Nintendo - Wii U (Digital) (CDN)", synced);
        Assert.AreEqual("wiiu", CatalogSystems.All.Single(s => s.DatName.StartsWith("Nintendo - Wii U")).Console);

        foreach (var variant in new[]
                 {
                     "Nintendo - Wii U (Digital) (CDN) (Dev)",
                     "Nintendo - Wii U (Digital) (CDN) (Lotcheck)",
                     "Nintendo - Wii U (Development Kit Hard Drives)",
                     "Non-Redump - Nintendo - Wii U",
                     "Unofficial - Nintendo - Wii U (Digital) (Deprecated)",
                 })
        {
            Assert.DoesNotContain(variant, synced, $"{variant} should not be synced");
            Assert.Contains(variant, CatalogSystems.ExcludedDats.Keys, $"{variant} needs an exclusion reason");
        }
    }

    // ---- Display names (#286) ------------------------------------------------------------------

    [TestMethod]
    public void DisplayNameFor_EveryConsoleSlug_ResolvesToNonEmptyName()
    {
        // Every slug CatalogSystems.All can emit must have a friendly name — no raw slugs surfaced in
        // the UI (issue #286 acceptance criteria).
        foreach (var s in CatalogSystems.All)
        {
            var name = CatalogSystems.DisplayNameFor(s.Console);
            Assert.IsFalse(string.IsNullOrWhiteSpace(name), $"{s.Console} → blank display name");
        }
    }

    [TestMethod]
    public void DisplayNameFor_SpotChecksCommonConsoles()
    {
        Assert.AreEqual("Game Boy", CatalogSystems.DisplayNameFor("gb"));
        Assert.AreEqual("GameCube", CatalogSystems.DisplayNameFor("gc"));
        Assert.AreEqual("PlayStation 1", CatalogSystems.DisplayNameFor("psx"));
        Assert.AreEqual("PlayStation 2", CatalogSystems.DisplayNameFor("ps2"));
        Assert.AreEqual("PlayStation 3", CatalogSystems.DisplayNameFor("ps3"));
        Assert.AreEqual("Super Nintendo", CatalogSystems.DisplayNameFor("snes"));
        Assert.AreEqual("Sega Genesis", CatalogSystems.DisplayNameFor("genesis"));
        Assert.AreEqual("TurboGrafx-16", CatalogSystems.DisplayNameFor("pcengine"));
        Assert.AreEqual("Neo Geo Pocket", CatalogSystems.DisplayNameFor("ngp"));
        Assert.AreEqual("WonderSwan", CatalogSystems.DisplayNameFor("wonderswan"));
        Assert.AreEqual("Atari 2600", CatalogSystems.DisplayNameFor("atari2600"));
        Assert.AreEqual("Wii U", CatalogSystems.DisplayNameFor("wiiu"));
    }

    [TestMethod]
    public void DisplayNameFor_UnknownSlug_FallsBackToSlugItself()
    {
        Assert.AreEqual("totally-made-up-slug", CatalogSystems.DisplayNameFor("totally-made-up-slug"));
    }

    [TestMethod]
    public void DisplayNames_HasNoBlankValues()
    {
        foreach (var (slug, name) in CatalogSystems.DisplayNames)
            Assert.IsFalse(string.IsNullOrWhiteSpace(name), $"{slug} → blank display name entry");
    }
}
