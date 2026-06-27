namespace Module.Catalog;

/// <summary>
/// One parsed compatibility entry: a match key (already normalized for its emulator's
/// <see cref="CompatMatchKind"/>) paired with a normalized status string.
/// </summary>
public readonly record struct CompatEntry(string MatchKey, string Status);

/// <summary>An HTTP GET supplied by the host (keeps the compat sources web-free).</summary>
public delegate Task<string> CompatFetch(string url, CancellationToken ct);

/// <summary>
/// An emulator compatibility source. Declares which emulator it feeds (<see cref="EmulatorId"/>, an id
/// from <see cref="Emulators.All"/>) and loads its full list of normalized entries. Web-free by design:
/// <see cref="LoadAsync"/> is handed a <see cref="CompatFetch"/> delegate by the host rather than owning
/// an HttpClient, so a source can drive a single GET (see <see cref="SingleUrlCompatSource"/>) or several
/// requests (a paginated API) without pulling networking into the module.
/// </summary>
public interface ICompatSource
{
    /// <summary>The emulator this source feeds; must match an id in <see cref="Emulators.All"/>.</summary>
    string EmulatorId { get; }

    /// <summary>Load every normalized (match key → status) entry, fetching via <paramref name="fetch"/>.</summary>
    Task<IReadOnlyList<CompatEntry>> LoadAsync(CompatFetch fetch, CancellationToken ct);
}

/// <summary>
/// Base for sources whose entire list comes from a single GET of <see cref="Url"/> plus a pure
/// <see cref="Parse"/> (RPCS3 JSON, PCSX2/DuckStation YAML). Multi-request sources implement
/// <see cref="ICompatSource"/> directly.
/// </summary>
public abstract class SingleUrlCompatSource : ICompatSource
{
    public abstract string EmulatorId { get; }

    /// <summary>Where the host fetches the compatibility payload from.</summary>
    public abstract string Url { get; }

    /// <summary>Parse a fetched payload into normalized (match key → status) entries.</summary>
    public abstract IEnumerable<CompatEntry> Parse(string payload);

    public async Task<IReadOnlyList<CompatEntry>> LoadAsync(CompatFetch fetch, CancellationToken ct)
        => Parse(await fetch(Url, ct)).ToList();
}

/// <summary>
/// The registered compatibility sources, one per ingested emulator. The host iterates these, calls
/// <see cref="ICompatSource.LoadAsync"/>, and stores the entries under the emulator's match kind.
/// </summary>
public static class CompatSources
{
    public static readonly IReadOnlyList<ICompatSource> All =
    [
        new Rpcs3CompatSource(),
        new Pcsx2CompatSource(),
        new DuckStationCompatSource(),
        new DolphinCompatSource(),
        new AzaharCompatSource(),
    ];
}
