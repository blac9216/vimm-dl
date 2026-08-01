using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Module.Core;

namespace Module.Catalog;

/// <summary>
/// A fresher catalog DAT source. hugo19941994/auto-datfile-generator regenerates the No-Intro and
/// Redump clrmamepro profiles <b>daily</b> and publishes each group as a single GitHub release-asset
/// zip (<c>no-intro.zip</c> / <c>redump.zip</c>) of per-system <c>.dat</c> files — the exact format
/// <see cref="ClrMameProParser"/> already handles. The libretro mirror, by contrast, lags upstream by
/// weeks.
///
/// <para>Downloading <b>one zip per group</b> (≈2 requests for a whole multi-console sync) is both
/// fresher and inherently rate-safe — so <see cref="InterSystemDelay"/> is zero. Each group's zip is
/// fetched once and cached for the run; a system's DAT is extracted on demand from the cached
/// <i>compressed</i> bytes, so memory holds the compressed bundle plus only one decompressed DAT at a
/// time. A system absent from the bundle fails soft (skipped), exactly like a 404 on the libretro path.</para>
/// </summary>
public sealed class DailyBundleDatSource(HttpClient http, ILogger<DailyBundleDatSource> log) : IDatSource
{
    /// <summary>Release-asset URL template; <c>{0}</c> is the group (<c>no-intro</c> / <c>redump</c>).</summary>
    internal string BundleUrlTemplate { get; set; } =
        "https://github.com/hugo19941994/auto-datfile-generator/releases/latest/download/{0}.zip";

    /// <inheritdoc/>
    public string Origin => "daily-bundle";
    public TimeSpan InterSystemDelay => TimeSpan.Zero;   // ~2 requests for a full run — no pacing needed

    // group -> compressed zip bytes, downloaded at most once per run. Guarded by _gate.
    private readonly Dictionary<string, byte[]> _zips = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<string>> GetDatAsync(CatalogSystemInfo sys, CancellationToken ct)
    {
        var zip = await GetZipAsync(sys.Group, ct);
        if (!zip.IsOk) return Result<string>.Fail(zip.Error!);
        return ExtractDat(zip.Value!, sys.DatName);
    }

    /// <summary>Download (once) and cache the group's bundle zip bytes.</summary>
    private async Task<Result<byte[]>> GetZipAsync(string group, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_zips.TryGetValue(group, out var cached)) return Result<byte[]>.Ok(cached);

            var url = string.Format(BundleUrlTemplate, group);
            byte[] bytes;
            try
            {
                using var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                    return Result<byte[]>.Fail($"HTTP {(int)resp.StatusCode} for {group} bundle");
                bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<byte[]>.Fail($"{group} bundle download failed: {ex.Message}");
            }

            _zips[group] = bytes;
            log.LogInformation("Catalog: downloaded {Group} bundle ({Size:N0} bytes)", group, bytes.Length);
            return Result<byte[]>.Ok(bytes);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Extract the system's DAT text from a cached bundle, or fail (system skipped).</summary>
    internal static Result<string> ExtractDat(byte[] zipBytes, string datName)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);

        ZipArchiveEntry? best = null;
        int bestQualifiers = int.MaxValue;
        foreach (var e in archive.Entries)
        {
            var fileName = Path.GetFileName(e.FullName);
            if (!IsMatch(fileName, datName)) continue;

            // A system's own DAT is "{DatName} (timestamp).dat" — exactly one trailing group. Sibling
            // DATs that extend the name add more ("(Parent-Clone)", "(Lotcheck)", "(Dev)"), and those
            // also satisfy the prefix test, so the least-qualified match is the system's real DAT.
            // Wii U makes this load-bearing: "Nintendo - Wii U (Digital) (CDN)" shares its prefix with
            // both "(CDN) (Lotcheck)" and "(CDN) (Dev)" in the same bundle.
            var qualifiers = CountQualifiers(fileName, datName);
            if (qualifiers < bestQualifiers) { best = e; bestQualifiers = qualifiers; }
        }
        if (best is null) return Result<string>.Fail($"no DAT for '{datName}' in bundle");

        using var reader = new StreamReader(best.Open());
        return Result<string>.Ok(reader.ReadToEnd());
    }

    /// <summary>
    /// How many parenthesised groups follow <paramref name="datName"/> in the entry filename. The
    /// system's own DAT scores 1 (its timestamp); a variant that extends the name scores more. A bare
    /// "{DatName}.dat" scores 0.
    /// </summary>
    internal static int CountQualifiers(string entryFileName, string datName)
    {
        var tail = entryFileName.Length > datName.Length
            ? entryFileName[datName.Length..]
            : "";
        int count = 0;
        for (int i = tail.IndexOf('('); i >= 0; i = tail.IndexOf('(', i + 1)) count++;
        return count;
    }

    /// <summary>
    /// Match a zip entry filename to a system. No-Intro/Redump originals are named
    /// <c>"{DatName} (timestamp).dat"</c> (or a <c>(Parent-Clone)</c> variant); a bare
    /// <c>"{DatName}.dat"</c> is also accepted. Requiring the <c>" ("</c>/<c>.dat</c> boundary right
    /// after the exact name stops a prefix like "…Game Boy" matching "…Game Boy Advance".
    /// </summary>
    internal static bool IsMatch(string entryFileName, string datName)
        => entryFileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
           && (entryFileName.Equals(datName + ".dat", StringComparison.OrdinalIgnoreCase)
               || entryFileName.StartsWith(datName + " (", StringComparison.OrdinalIgnoreCase));
}
