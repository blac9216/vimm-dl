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
    /// Merge <paramref name="games"/> into a system on behalf of one data-source <paramref name="origin"/>
    /// (e.g. "libretro" / "daily-bundle"), recording the DAT version. Implementations <b>accumulate +
    /// dedup by canonical_key</b> (D2b / #162): a game whose content key already exists in the system gains
    /// this origin without duplicating the row; games only this origin no longer lists lose this origin and,
    /// if left with no origins, are removed. Re-syncing one origin must never drop another origin's games.
    /// </summary>
    Task MergeSystemGamesAsync(long systemId, string origin, IReadOnlyList<DatGame> games, string? datVersion, CancellationToken ct);
}
