namespace Module.Catalog;

/// <summary>
/// The canonical compatibility status vocabulary (RPCS3-derived), best → worst. Per-emulator adapters
/// normalize their native ratings into these so the Library badges + status filter stay uniform across
/// emulators. RPCS3's own export already uses these spellings; other adapters map onto them.
/// </summary>
public static class CompatStatuses
{
    public const string Playable = "Playable";
    public const string Ingame = "Ingame";
    public const string Intro = "Intro";
    public const string Loadable = "Loadable";
    public const string Nothing = "Nothing";
}
