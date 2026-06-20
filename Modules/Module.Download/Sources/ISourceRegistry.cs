namespace Module.Download.Sources;

/// <summary>Lookup of registered download sources by their <see cref="IDownloadSource.Id"/>.</summary>
public interface ISourceRegistry
{
    /// <summary>Returns the source for an id, or null if none is registered.</summary>
    IDownloadSource? Get(string sourceId);

    /// <summary>All registered sources (for the UI source picker).</summary>
    IReadOnlyCollection<IDownloadSource> All { get; }
}

/// <summary>
/// Default registry built from the set of <see cref="IDownloadSource"/> registered in DI.
/// Lookup is case-insensitive on the source id.
/// </summary>
public sealed class SourceRegistry : ISourceRegistry
{
    private readonly Dictionary<string, IDownloadSource> _byId;

    public SourceRegistry(IEnumerable<IDownloadSource> sources)
        => _byId = sources.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    public IDownloadSource? Get(string sourceId)
        => _byId.GetValueOrDefault(sourceId);

    public IReadOnlyCollection<IDownloadSource> All => _byId.Values;
}
