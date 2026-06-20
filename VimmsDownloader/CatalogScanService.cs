using Module.Catalog;

/// <summary>
/// Scans <c>completed/{console}/</c> and records which catalog games are present on disk
/// (via <see cref="CatalogMatcher"/>). The file's console is its parent folder, so matches are
/// console-scoped; files in the <c>completed/</c> root (no console folder) are left unmatched.
/// </summary>
class CatalogScanService(CatalogRepository catalog, QueueRepository queue, ILogger<CatalogScanService> log)
{
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        var completed = Path.Combine(queue.GetDownloadPath(), "completed");
        var games = await catalog.GetGameKeysAsync();
        var owned = CatalogMatcher.Match(games, EnumerateCompleted(completed));
        await catalog.ReplaceOwnedAsync(owned, ct);
        log.LogInformation("Catalog scan: {Owned} of {Total} games found on disk", owned.Count, games.Count);
        return owned.Count;
    }

    /// <summary>Yield (console, path) for files under each <c>completed/{console}/</c> subfolder.</summary>
    private static IEnumerable<(string Console, string Path)> EnumerateCompleted(string completedDir)
    {
        if (!Directory.Exists(completedDir)) yield break;
        foreach (var consoleDir in Directory.EnumerateDirectories(completedDir))
        {
            var console = Path.GetFileName(consoleDir);
            foreach (var file in Directory.EnumerateFiles(consoleDir, "*", SearchOption.AllDirectories))
                yield return (console, file);
        }
    }
}
