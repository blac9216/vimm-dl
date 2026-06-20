namespace Module.Download;

/// <summary>
/// An item to download from the queue. <see cref="Url"/> is the source-specific
/// identifier (for Vimm: the vault URL); <see cref="Source"/> selects which
/// <see cref="Sources.IDownloadSource"/> resolves it (defaults to Vimm).
/// </summary>
public record DownloadItem(int Id, string Url, int Format)
{
    public string Source { get; init; } = "vimm";
}

/// <summary>
/// Interface for the host to provide queue items. The module doesn't
/// know about databases — it just asks for the next item and reports completion.
/// </summary>
public interface IDownloadItemProvider
{
    Task<DownloadItem?> GetNextAsync();
    Task CompleteAsync(int id, string url, string filename, string filepath, int format);
    Task RemoveAsync(int id);
}
