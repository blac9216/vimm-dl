using Module.Core;
using Module.Extractor;

/// <summary>
/// Extracts an archive's contents into a directory. A thin seam over <see cref="ZipExtract"/>'s 7z
/// shell-out so the import flow (epic #118 / L3) can be unit-tested with a fake — the real 7z path is
/// covered by Module.Extractor's own container tests.
/// </summary>
interface IArchiveExtractor
{
    Task<Result<bool>> ExtractAsync(string archivePath, string outputDir, CancellationToken ct);
}

/// <summary>Production extractor: 7z via Module.Extractor (handles .zip/.7z/.rar).</summary>
sealed class SevenZipArchiveExtractor : IArchiveExtractor
{
    public Task<Result<bool>> ExtractAsync(string archivePath, string outputDir, CancellationToken ct)
        => ZipExtract.ExtractAsync(archivePath, outputDir, onProgress: null, ct);
}
