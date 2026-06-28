using System.Runtime.CompilerServices;
using Module.Catalog;

/// <summary>
/// Populates the IGDB ranking signals on <c>catalog_game</c> (<c>igdb_rating</c>,
/// <c>igdb_rating_count</c>, and a derived <c>rank_score</c>) so the Library can sort by "best games"
/// (epic #123 / R1). Parallels <see cref="IgdbSyncService"/> (descriptions) — same console loop, same
/// platform pagination, same name join — but pulls <c>total_rating</c>/<c>total_rating_count</c> and
/// stores a Bayesian <see cref="RankScore"/>. Reuses the shared <see cref="IgdbClient"/> + Twitch creds;
/// the host gates it behind the same <c>CatalogIgdbState</c> as the description sync, so the two IGDB
/// jobs serialize under one token + rate limit. No-ops (returns 0) when creds are absent or a token
/// can't be obtained. Incremental by default (only as-yet-unranked games); <c>force</c> re-ranks all.
/// </summary>
class IgdbRankSyncService(CatalogRepository catalog, QueueRepository settings, IgdbClient igdb,
    ILogger<IgdbRankSyncService> log)
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
            log.LogInformation("IGDB rank sync skipped — no Twitch client id/secret configured");
            return 0;
        }

        var token = await igdb.GetTokenAsync(clientId, clientSecret, ct);
        if (token is null)
        {
            log.LogWarning("IGDB rank sync aborted — could not obtain a Twitch token");
            return 0;
        }

        var totalRanked = 0;
        foreach (var console in (await catalog.GetConsolesAsync()).Select(c => c.Console))
        {
            ct.ThrowIfCancellationRequested();
            if (IgdbPlatforms.Ids(console) is not { } platformIds) continue; // no IGDB mapping

            // Incremental by default: only as-yet-unranked games. A fully-ranked console yields an empty
            // list and is skipped here, before the (expensive) IGDB pagination below.
            var games = await catalog.GetGamesForRankAsync(console, onlyUnranked: !force);
            if (games.Count == 0) continue;

            // Pull every rated IGDB game for this console's platform(s) into one normalized-name index.
            var index = new Dictionary<string, IgdbRating>();
            foreach (var pid in platformIds)
                await foreach (var page in PagePlatformAsync(clientId, token, pid, ct))
                    foreach (var kv in IgdbRatings.BuildIndex(page))
                        index.TryAdd(kv.Key, kv.Value);
            if (index.Count == 0) continue;

            var matched = IgdbRatings.Match(index, games);
            if (matched.Count == 0) continue;
            var rows = matched
                .Select(m => (m.Id, m.Rating, m.Count, Score: RankScore.Bayesian(m.Rating, m.Count)))
                .ToList();
            await catalog.SetRanksAsync(rows, ct);
            totalRanked += rows.Count;
            log.LogInformation("IGDB rank: {Console} → {Ranked}/{Games} ranked", console, rows.Count, games.Count);
        }
        return totalRanked;
    }

    /// <summary>
    /// Page one IGDB platform's rated games (offset pagination, id-sorted), yielding each page's parsed
    /// games. Filters to games that actually carry a <c>total_rating</c>. Stops on an empty/short page or
    /// a transient query failure (keeping what was pulled).
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<IgdbRating>> PagePlatformAsync(
        string clientId, string token, int platformId, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; ; offset += PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var query =
                $"fields name,total_rating,total_rating_count; where platforms = ({platformId}) & " +
                $"total_rating != null; sort id asc; limit {PageSize}; offset {offset};";
            var json = await igdb.QueryGamesAsync(clientId, token, query, ct);
            if (json is null) yield break; // transient failure — stop this platform, keep the index so far
            var games = IgdbRatings.ParseGames(json);
            if (games.Count == 0) yield break;
            yield return games;
            if (games.Count < PageSize) yield break; // last (partial) page
            await Task.Delay(PageDelay, ct);
        }
    }
}
