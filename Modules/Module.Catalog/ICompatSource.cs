namespace Module.Catalog;

/// <summary>
/// One parsed compatibility entry: a match key (already normalized for its emulator's
/// <see cref="CompatMatchKind"/>) paired with a normalized status string.
/// </summary>
public readonly record struct CompatEntry(string MatchKey, string Status);

/// <summary>
/// An emulator compatibility source. Declares which emulator it feeds (<see cref="EmulatorId"/>, an
/// id from <see cref="Emulators.All"/>) and where its list lives (<see cref="Url"/>), and parses a
/// fetched payload into normalized entries. Web-free by design — the host fetches <see cref="Url"/>
/// and hands the payload to <see cref="Parse"/>, mirroring how the catalog DAT/Vimm parsers stay
/// off the network.
/// </summary>
public interface ICompatSource
{
    /// <summary>The emulator this source feeds; must match an id in <see cref="Emulators.All"/>.</summary>
    string EmulatorId { get; }

    /// <summary>Where the host fetches the compatibility payload from.</summary>
    string Url { get; }

    /// <summary>Parse a fetched payload into normalized (match key → status) entries.</summary>
    IEnumerable<CompatEntry> Parse(string payload);
}

/// <summary>
/// The registered compatibility sources, one per ingested emulator. The host iterates these, fetches
/// each <see cref="ICompatSource.Url"/>, and stores the parsed entries under the emulator's match kind.
/// F2/F3/F4 append their adapters here.
/// </summary>
public static class CompatSources
{
    public static readonly IReadOnlyList<ICompatSource> All =
    [
        new Rpcs3CompatSource(),
        new Pcsx2CompatSource(),
        new DuckStationCompatSource(),
    ];
}
