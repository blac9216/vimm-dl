using Module.Core;

namespace Module.Download.Sources;

/// <summary>
/// Optional capability for sources whose catalog can be browsed in-app: search for
/// "sets" (collections) and list the downloadable files within a set. A source only
/// implements this if it supports browsing (e.g. <see cref="ArchiveSource"/>).
/// </summary>
public interface ICatalogSource
{
    /// <summary>Search the source for sets/collections matching a free-text query.</summary>
    Task<Result<IReadOnlyList<CatalogSet>>> SearchSetsAsync(string query, HttpClient http, CancellationToken ct);

    /// <summary>List downloadable files in a set, optionally filtered by name (case-insensitive).</summary>
    Task<Result<IReadOnlyList<CatalogFile>>> ListFilesAsync(string setId, string? filter, HttpClient http, CancellationToken ct);
}

/// <summary>A browsable collection/set (e.g. an archive.org item holding many ROMs).</summary>
public record CatalogSet(string Id, string Title, string? Platform);

/// <summary>A single downloadable file within a set; <see cref="DownloadUrl"/> is queueable as-is.</summary>
public record CatalogFile(string Name, long Size, string DownloadUrl);
