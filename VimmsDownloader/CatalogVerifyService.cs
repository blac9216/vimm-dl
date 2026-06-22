using System.IO.Compression;
using Module.Catalog;

/// <summary>
/// Hash-based owned detection (Phase C / C3): walks <c>completed/{console}/</c>, hashes each file, and
/// marks a catalog game owned when the file's SHA1/MD5/CRC32 matches one of its <c>catalog_rom</c> rows
/// — regardless of the file's name or format. Priority SHA1 → MD5 → CRC32 (mirrors the Vimm binding).
/// Raw roms/ISOs are streamed once for all three hashes; <c>.zip</c> contributes its entry CRC (no
/// decompress); <c>.7z</c> is skipped (can't hash without extracting). Runs in the background verify
/// job — multi-GB ISOs are streamed, never buffered. Unreadable files are left unmatched.
/// </summary>
class CatalogVerifyService(CatalogRepository catalog, QueueRepository queue, ILogger<CatalogVerifyService> log)
{
    public async Task<int> VerifyAsync(CancellationToken ct)
    {
        var completedDir = Path.Combine(queue.GetDownloadPath(), "completed");
        var matched = new Dictionary<long, (string Path, string Hash)>();

        if (Directory.Exists(completedDir))
        {
            foreach (var consoleDir in Directory.EnumerateDirectories(completedDir))
            {
                ct.ThrowIfCancellationRequested();
                var console = Path.GetFileName(consoleDir);
                var index = await catalog.GetVimmHashIndexAsync(console, ct);

                foreach (var path in Directory.EnumerateFiles(consoleDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var hashes = TryComputeHashes(path);
                    if (hashes is not { } h) continue; // unreadable / unsupported → leave unmatched

                    var hit = MatchByHash(index, h);
                    if (hit is { } m && !matched.ContainsKey(m.GameId))
                        matched[m.GameId] = (path, m.Kind);
                }
            }
        }

        await catalog.MarkOwnedByHashAsync(matched, ct);
        log.LogInformation("Verify: {Matched} catalog games confirmed owned by hash", matched.Count);
        return matched.Count;
    }

    /// <summary>Match a file's hashes to a game in the console's rom index, SHA1 → MD5 → CRC32.</summary>
    private static (long GameId, string Kind)? MatchByHash(CatalogRepository.VimmHashIndex index, FileHashes.Hashes h)
    {
        if (h.Sha1 is { Length: > 0 } sha1 && index.BySha1.TryGetValue(sha1.ToLowerInvariant(), out var gs)) return (gs, "sha1");
        if (h.Md5 is { Length: > 0 } md5 && index.ByMd5.TryGetValue(md5.ToLowerInvariant(), out var gm)) return (gm, "md5");
        if (h.Crc is { Length: > 0 } crc && index.ByCrc.TryGetValue(crc.ToLowerInvariant(), out var gc)) return (gc, "crc");
        return null;
    }

    private static FileHashes.Hashes? TryComputeHashes(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return null; // can't hash without extracting
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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
