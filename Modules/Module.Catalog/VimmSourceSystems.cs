namespace Module.Catalog;

/// <summary>
/// A console for which Vimm's Lair is the <b>authoritative catalog source</b> — it supplies the game
/// list itself, not just a download link bound onto a DAT-sourced game.
/// </summary>
/// <param name="Console">Catalog console folder (EmuDeck), shared with any DAT-sourced system for the
/// same hardware — e.g. Wii U discs and Wii U digital both live under <c>wiiu</c>.</param>
/// <param name="VimmCode">Vimm vault system code, as in <c>?p=list&amp;system={code}</c>.</param>
/// <param name="DatName">The <c>catalog_system.dat_name</c> these games are filed under. Must not
/// collide with a real DAT name in <see cref="CatalogSystems"/> — it names a scrape, not a datfile.</param>
public sealed record VimmSourceSystemInfo(string Console, string VimmCode, string DatName);

/// <summary>
/// Consoles seeded <i>from</i> Vimm rather than from a No-Intro/Redump DAT.
///
/// <para>This is the deliberate exception to the catalog's DAT-first identity model, and it exists only
/// where there is no DAT to be had. Vimm publishes the <b>canonical Redump hash triple</b> — verified
/// byte-identical to the Redump DAT on a console where both exist (vault 17719 vs
/// <c>Nintendo - Wii</c>: CRC/MD5/SHA1 all match, even though Vimm ships a compressed .wbfs/.rvz and
/// the DAT describes the 4.7 GB .iso) — so the hashes stored from a scrape carry the same meaning as
/// DAT-sourced ones, and a future DAT can be reconciled against them by hash.</para>
///
/// <para>What differs is <b>provenance, not semantics</b>: these rows never faced an independent
/// cross-check, because Vimm is both the source and the only witness. They are therefore filed under
/// their own <c>catalog_system</c> with <c>source = "vimm"</c> and carry
/// <c>catalog_game_source.origin = "vimm"</c>, so they stay distinguishable from DAT-backed rows for
/// as long as they exist — and can be superseded in place if the DAT ever becomes public.</para>
///
/// <para>Distinct from <see cref="VimmSystems"/>, which lists consoles where Vimm is merely <i>bound
/// onto</i> an existing DAT-sourced game by hash. A console must not appear in both.</para>
/// </summary>
public static class VimmSourceSystems
{
    /// <summary>Marker stored in <c>catalog_system.source</c> and <c>catalog_game_source.origin</c>.</summary>
    public const string Origin = "vimm";

    public static readonly IReadOnlyList<VimmSourceSystemInfo> All =
    [
        // Wii U discs. Redump has the set (Vimm states it syncs nightly and shows 541/541) but does not
        // publish the datfile: it is absent from redump.org's downloads page, from the public system
        // list, and from both daily bundles. Wii U *digital* is a separate, DAT-backed system on the
        // same console — see CatalogSystems "Nintendo - Wii U (Digital) (CDN)".
        new("wiiu", "WiiU", "Nintendo - Wii U (Discs)"),
    ];

    /// <summary>The Vimm-authoritative system for a console, or null if it is DAT-sourced.</summary>
    public static VimmSourceSystemInfo? For(string console)
    {
        foreach (var s in All)
            if (s.Console == console) return s;
        return null;
    }
}
