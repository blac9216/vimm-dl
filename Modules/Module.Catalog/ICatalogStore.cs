namespace Module.Catalog;

/// <summary>
/// Host-implemented persistence sink for the catalog — the seam between the web-free
/// <see cref="CatalogSyncService"/> and the host's SQLite store (mirrors the role of
/// <c>IDownloadItemProvider</c> in Module.Download).
/// </summary>
public interface ICatalogStore
{
    /// <summary>Ensure a <c>catalog_system</c> row exists for this DAT; return its id.</summary>
    Task<long> UpsertSystemAsync(string datName, string console, string source, CancellationToken ct);

    /// <summary>
    /// Replace every game (and its roms) for a system with <paramref name="games"/>, recording the
    /// DAT version. Re-syncing a system must not leave duplicates — implementations replace, not append.
    /// </summary>
    Task ReplaceSystemGamesAsync(long systemId, IReadOnlyList<DatGame> games, string? datVersion, CancellationToken ct);
}
