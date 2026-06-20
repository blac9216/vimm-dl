using System.IO.Compression;
using Module.Catalog;

/// <summary>
/// Verifies owned files against the catalog's CRC32s. For <c>.zip</c> the entry CRC is read from
/// the central directory (no decompress); raw roms are streamed; <c>.7z</c> is skipped for now.
/// Unreadable files are left unchecked (never marked mismatch).
/// </summary>
class CatalogVerifyService(CatalogRepository catalog, ILogger<CatalogVerifyService> log)
{
    public async Task<int> VerifyAsync(CancellationToken ct)
    {
        var owned = await catalog.GetOwnedForVerifyAsync();
        var results = new Dictionary<long, bool>();
        foreach (var (gameId, path, crcs) in owned)
        {
            ct.ThrowIfCancellationRequested();
            var crc = TryComputeCrc(path);
            if (crc is null) continue; // unreadable / unsupported → leave unchecked
            results[gameId] = crcs.Contains(crc);
        }
        await catalog.SetVerifiedAsync(results, ct);
        var matched = results.Count(r => r.Value);
        log.LogInformation("Verify: {Matched}/{Checked} owned files matched a catalog CRC", matched, results.Count);
        return matched;
    }

    private static string? TryComputeCrc(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.Entries.FirstOrDefault(e => e.Length > 0);
                return entry is null ? null : Crc32.ToHex(entry.Crc32);
            }
            if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return null; // not supported yet
            using var fs = File.OpenRead(path);
            return Crc32.ComputeHex(fs);
        }
        catch
        {
            return null;
        }
    }
}
