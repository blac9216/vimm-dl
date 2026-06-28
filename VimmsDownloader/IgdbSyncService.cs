using System.Runtime.CompilerServices;
using Module.Catalog;

/// <summary>
/// Populates <c>catalog_game.description</c> from IGDB. For each catalog console that has an IGDB
/// platform mapping (<see cref="IgdbPlatforms"/>), it pages through that platform's games (Apicalypse,
/// throttled to stay under IGDB's rate limit), builds a normalized-name → description index, joins the
/// console's catalog games by name (<see cref="CatalogMatcher"/>), and stores the matches. No-ops
/// (returns 0) when the user hasn't configured Twitch creds, or when a token can't be obtained.
///
/// By default the run is incremental: only games that still lack a description are pulled and matched,
/// so a console whose games are all already described is skipped without any IGDB fetch. Pass
/// <c>force</c> to re-pull and re-store every game (e.g. after a description-source change).
/// </summary>
class IgdbSyncService(CatalogRepository catalog, QueueRepository settings, IgdbClient igdb,
    ILogger<IgdbSyncService> log)
{
    private const int PageSize = 500;                              // IGDB's max page size
    private static readonly TimeSpan PageDelay = TimeSpan.FromMilliseconds(300); // ~3.3 req/s, under IGDB's 4 req/s

    public async Task<int> SyncAsync(bool force, CancellationToken ct)
    {
        var s = await settings.GetAllSettingsAsync();
        var clientId = s.GetValueOrDefault(SettingsKeys.IgdbClientId, "").Trim();
        var clientSecret = s.GetValueOrDefault(SettingsKeys.IgdbClientSecret, "").Trim();
        if (clientId.Length == 0 || clientSecret.Length == 0)
        {
            log.LogInformation("IGDB sync skipped — no Twitch client id/secret configured");
            return 0;
        }

        var token = await igdb.GetTokenAsync(clientId, clientSecret, ct);
        if (token is null)
        {
            log.LogWarning("IGDB sync aborted — could not obtain a Twitch token");
            return 0;
        }

        var totalMatched = 0;
        foreach (var console in (await catalog.GetConsolesAsync()).Select(c => c.Console))
        {
            ct.ThrowIfCancellationRequested();
            if (IgdbPlatforms.Ids(console) is not { } platformIds) continue; // no IGDB mapping

            // Incremental by default: only undescribed games. A fully-described console yields an empty
            // list and is skipped here, before the (expensive) IGDB pagination below.
            var games = await catalog.GetGamesForConsoleAsync(console, onlyUndescribed: !force);
            if (games.Count == 0) continue;

            // Pull every IGDB game for this console's platform(s) into one normalized-name index.
            var index = new Dictionary<string, string>();
            foreach (var pid in platformIds)
                await foreach (var page in PagePlatformAsync(clientId, token, pid, ct))
                    foreach (var kv in IgdbDescriptions.BuildIndex(page))
                        index.TryAdd(kv.Key, kv.Value);
            if (index.Count == 0) continue;

            var matched = IgdbDescriptions.Match(index, games);
            if (matched.Count == 0) continue;
            await catalog.SetDescriptionsAsync(matched, ct);
            totalMatched += matched.Count;
            log.LogInformation("IGDB: {Console} → {Matched}/{Games} described", console, matched.Count, games.Count);
        }
        return totalMatched;
    }

    /// <summary>
    /// Page one IGDB platform's described games (offset pagination, id-sorted), yielding each page's
    /// parsed games. Stops on an empty/short page or a transient query failure (keeping what was pulled).
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<IgdbGame>> PagePlatformAsync(
        string clientId, string token, int platformId, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; ; offset += PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var query =
                $"fields name,summary,storyline; where platforms = ({platformId}) & " +
                $"(summary != null | storyline != null); sort id asc; limit {PageSize}; offset {offset};";
            var json = await igdb.QueryGamesAsync(clientId, token, query, ct);
            if (json is null) yield break; // transient failure — stop this platform, keep the index so far
            var games = IgdbDescriptions.ParseGames(json);
            if (games.Count == 0) yield break;
            yield return games;
            if (games.Count < PageSize) yield break; // last (partial) page
            await Task.Delay(PageDelay, ct);
        }
    }
}
