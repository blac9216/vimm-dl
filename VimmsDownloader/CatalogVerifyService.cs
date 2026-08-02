using System.IO.Compression;
using Module.Catalog;
using Module.Core;

/// <summary>
/// Hash-based owned detection (Phase C / C3): walks <c>completed/{console}/</c>, hashes each file, and
/// marks a catalog game owned when the file's SHA1/MD5/CRC32 matches one of its <c>catalog_rom</c> rows
/// — regardless of the file's name or format. Priority SHA1 → MD5 → CRC32 (mirrors the Vimm binding).
/// Raw roms/ISOs are streamed once for all three hashes; <c>.zip</c> contributes its entry CRC (no
/// decompress); <c>.7z</c> and compressed disc-image formats (<see cref="FileExtensions.IsCompressedImage"/>
/// — <c>.wux</c>/<c>.rvz</c>/<c>.wbfs</c>/<c>.chd</c>) can't be hashed without extracting/decoding, so
/// they're counted as <b>unverifiable</b> rather than silently falling through to a raw-byte hash that
/// could never match (#284) — the count is surfaced via <paramref name="report"/> below, distinct from
/// "checked, no match". Runs in the background verify job — multi-GB ISOs are streamed, never buffered.
/// Unreadable files are left unmatched (and not counted as unverifiable).
/// </summary>
class CatalogVerifyService(CatalogRepository catalog, QueueRepository queue, ILogger<CatalogVerifyService> log)
{
    /// <summary>
    /// <paramref name="report"/>, when given, is called once per console folder (message = console,
    /// current = 1-based folder index, total = folder count) plus a terminal call carrying the run's
    /// matched/unverifiable counts — the L1 Jobs API progress checkpoint.
    /// </summary>
    public async Task<int> VerifyAsync(Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var completedDir = Path.Combine(queue.GetDownloadPath(), "completed");
        var matched = new Dictionary<long, (string Path, string Hash)>();
        var unverifiable = 0;

        if (Directory.Exists(completedDir))
        {
            var consoleDirs = Directory.GetDirectories(completedDir);
            for (var i = 0; i < consoleDirs.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var consoleDir = consoleDirs[i];
                var console = Path.GetFileName(consoleDir);
                report?.Invoke(console, i + 1, consoleDirs.Length);
                var index = await catalog.GetVimmHashIndexAsync(console, ct);

                foreach (var path in Directory.EnumerateFiles(consoleDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var hashes = TryComputeHashes(path, out var isUnverifiable);
                    if (isUnverifiable) { unverifiable++; continue; }
                    if (hashes is not { } h) continue; // unreadable → leave unmatched

                    var hit = MatchByHash(index, h);
                    if (hit is { } m && !matched.ContainsKey(m.GameId))
                        matched[m.GameId] = (path, m.Kind);
                }
            }
        }

        await catalog.MarkOwnedByHashAsync(matched, ct);
        log.LogInformation("Verify: {Matched} catalog games confirmed owned by hash, {Unverifiable} unverifiable",
            matched.Count, unverifiable);
        report?.Invoke($"Done: {matched.Count} matched, {unverifiable} unverifiable (compressed image formats)", null, null);
        return matched.Count;
    }

    public Task<int> VerifyAsync(CancellationToken ct) => VerifyAsync(null, ct);

    /// <summary>Match a file's hashes to a game in the console's rom index, SHA1 → MD5 → CRC32.</summary>
    private static (long GameId, string Kind)? MatchByHash(CatalogRepository.VimmHashIndex index, FileHashes.Hashes h)
    {
        if (h.Sha1 is { Length: > 0 } sha1 && index.BySha1.TryGetValue(sha1.ToLowerInvariant(), out var gs)) return (gs, "sha1");
        if (h.Md5 is { Length: > 0 } md5 && index.ByMd5.TryGetValue(md5.ToLowerInvariant(), out var gm)) return (gm, "md5");
        if (h.Crc is { Length: > 0 } crc && index.ByCrc.TryGetValue(crc.ToLowerInvariant(), out var gc)) return (gc, "crc");
        return null;
    }

    /// <summary>
    /// Computes hashes for a hashable file. <paramref name="unverifiable"/> is true for a format that
    /// is known to be un-hashable as-is (<c>.7z</c> or a compressed disc image, #284) — distinct from a
    /// plain read failure, so callers can count it separately instead of silently leaving it unmatched.
    /// </summary>
    private static FileHashes.Hashes? TryComputeHashes(string path, out bool unverifiable)
    {
        unverifiable = false;
        try
        {
            if (!File.Exists(path)) return null;
            if (path.EndsWith(FileExtensions.SevenZip, StringComparison.OrdinalIgnoreCase) ||
                FileExtensions.IsCompressedImage(path))
            {
                // Can't hash without extracting/decoding — the container bytes never equal the
                // canonical uncompressed image's hash, so this is a known gap, not "no match".
                unverifiable = true;
                return null;
            }
            if (path.EndsWith(FileExtensions.Zip, StringComparison.OrdinalIgnoreCase))
            {
                // The rom lives inside the zip; its CRC is in the central directory (no decompress).
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.Entries.FirstOrDefault(e => e.Length > 0);
                return entry is null ? null : new FileHashes.Hashes(Crc32.ToHex(entry.Crc32), null, null);
            }
            using var fs = File.OpenRead(path);
            return FileHashes.ComputeAll(fs);
        }
        catch
        {
            return null;
        }
    }
}
