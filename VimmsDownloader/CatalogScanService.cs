using Module.Catalog;

/// <summary>
/// Scans <c>completed/{console}/</c> and records which catalog games are present on disk
/// (via <see cref="CatalogMatcher"/>). The file's console is its parent folder, so matches are
/// console-scoped; files in the <c>completed/</c> root (no console folder) are left unmatched.
/// </summary>
class CatalogScanService(CatalogRepository catalog, QueueRepository queue, ILogger<CatalogScanService> log)
{
    /// <summary>
    /// <paramref name="report"/>, when given, is called once per console folder (message = console,
    /// current = 1-based folder index, total = folder count) — the L1 Jobs API progress checkpoint.
    /// </summary>
    public async Task<int> ScanAsync(Action<string?, int?, int?>? report, CancellationToken ct)
    {
        var completed = Path.Combine(queue.GetDownloadPath(), "completed");
        var games = await catalog.GetGameKeysAsync();
        var owned = CatalogMatcher.Match(games, EnumerateCompleted(completed, report, ct));
        await catalog.ReplaceOwnedAsync(owned, ct);
        log.LogInformation("Catalog scan: {Owned} of {Total} games found on disk", owned.Count, games.Count);
        return owned.Count;
    }

    public Task<int> ScanAsync(CancellationToken ct) => ScanAsync(null, ct);

    /// <summary>Yield (console, path) for files under each <c>completed/{console}/</c> subfolder.</summary>
    private static IEnumerable<(string Console, string Path)> EnumerateCompleted(string completedDir,
        Action<string?, int?, int?>? report, CancellationToken ct)
    {
        if (!Directory.Exists(completedDir)) yield break;
        var consoleDirs = Directory.GetDirectories(completedDir);
        for (var i = 0; i < consoleDirs.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var console = Path.GetFileName(consoleDirs[i]);
            report?.Invoke(console, i + 1, consoleDirs.Length);
            foreach (var file in Directory.EnumerateFiles(consoleDirs[i], "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                yield return (console, file);
            }
        }
    }
}
