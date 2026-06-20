namespace Module.Catalog;

/// <summary>An emulator whose per-game compatibility list is ingested, mapped to a console.</summary>
public sealed record EmulatorInfo(string Id, string Name, string Console);

/// <summary>
/// Emulators with ingested compatibility data. Starts with RPCS3 (PS3); others (PCSX2, Dolphin,
/// Cemu, PPSSPP, …) follow the same pattern — add an entry + an ingest path keyed by serial/title.
/// </summary>
public static class Emulators
{
    public static readonly IReadOnlyList<EmulatorInfo> All =
    [
        new("rpcs3", "RPCS3", "ps3"),
    ];
}
