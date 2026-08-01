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

    /// <summary>List downloadable files in a set, optionally filtered by name (case-insensitive). The
    /// result must be the set's COMPLETE file list (subject to the filter) — callers index it whole
    /// (#289) or search it for one file, so an implementation must not silently truncate.</summary>
    Task<Result<IReadOnlyList<CatalogFile>>> ListFilesAsync(string setId, string? filter, HttpClient http, CancellationToken ct);
}

/// <summary>A browsable collection/set (e.g. an archive.org item holding many ROMs).</summary>
public record CatalogSet(string Id, string Title, string? Platform);

/// <summary>
/// A single downloadable file within a set; <see cref="DownloadUrl"/> is queueable as-is.
/// <see cref="Crc32"/>/<see cref="Md5"/>/<see cref="Sha1"/> come from the source's own metadata when it
/// publishes them (archive.org's <c>/metadata/&lt;id&gt;</c> does, for original — non-derivative — files)
/// so a caller can hash-match a file to a catalog game instead of only by name (#289); null when the
/// source doesn't carry a given hash for that file.
/// </summary>
public record CatalogFile(string Name, long Size, string DownloadUrl,
    string? Crc32 = null, string? Md5 = null, string? Sha1 = null);
