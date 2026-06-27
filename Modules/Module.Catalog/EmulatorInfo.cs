namespace Module.Catalog;

/// <summary>
/// How an emulator's compatibility list is joined to a catalog game. Different emulators expose
/// different stable keys: the PlayStation family by disc <see cref="Serial"/>, the Nintendo systems
/// by 16-hex <see cref="TitleId"/>, and a few (Dolphin) only by normalized <see cref="Name"/>.
/// </summary>
public enum CompatMatchKind { Serial, TitleId, Name }

/// <summary>
/// An emulator whose per-game compatibility list is ingested, mapped to a console and the join key
/// (<see cref="CompatMatchKind"/>) its data exposes.
/// </summary>
public sealed record EmulatorInfo(string Id, string Name, string Console, CompatMatchKind MatchKind);

/// <summary>
/// The emulators with ingested compatibility data. Drives the per-emulator badges + the Library
/// emulator/status filter, and (via <see cref="EmulatorInfo.MatchKind"/>) how each emulator's statuses
/// join to a catalog game. RPCS3 came first because of the PS3 pipeline; PCSX2, DuckStation, Azahar,
/// Dolphin, … follow the same pattern — add an entry here plus an <see cref="ICompatSource"/> in
/// <see cref="CompatSources"/>.
/// </summary>
public static class Emulators
{
    public static readonly IReadOnlyList<EmulatorInfo> All =
    [
        new("rpcs3", "RPCS3", "ps3", CompatMatchKind.Serial),
        new("pcsx2", "PCSX2", "ps2", CompatMatchKind.Serial),
        new("duckstation", "DuckStation", "psx", CompatMatchKind.Serial),
    ];

    /// <summary>Look up an emulator by its id (case-insensitive); null when unknown.</summary>
    public static EmulatorInfo? ById(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The DB token for a match kind (stored in <c>catalog_compat.match_kind</c>).</summary>
    public static string Token(CompatMatchKind kind) => kind switch
    {
        CompatMatchKind.Serial => "serial",
        CompatMatchKind.TitleId => "title_id",
        CompatMatchKind.Name => "name",
        _ => "serial",
    };
}
