using Module.Catalog;

/// <summary>
/// Populates <c>catalog_game.ra_players</c> (RetroAchievements popularity = NumDistinctPlayers) and
/// folds it into <c>rank_score</c> (epic #123 / R2). For each RA-hash-joinable cartridge/handheld
/// console (<see cref="RaConsoles"/>) it pulls RA's game list with recognised ROM MD5 hashes, joins them
/// to the catalog by <b>exact MD5 equality</b> (so RA games whose hashing differs simply don't match —
/// no false positives), then fetches each matched game's player count (per-game — RA's bulk list omits
/// it) and stores the blended <see cref="RankScore.Blend"/>. No-ops (returns 0) when no RA web API key
/// is configured. Incremental by default (only games without a player count yet); <c>force</c> refetches
/// all matched games. Throttled + cancellable — the first full run is a long background job, like the
/// Vimm scrape; subsequent incremental runs are cheap.
/// </summary>
class RetroAchievementsSyncService(CatalogRepository catalog, QueueRepository settings,
    RetroAchievementsClient ra, ILogger<RetroAchievementsSyncService> log)
{
    private static readonly TimeSpan FetchDelay = TimeSpan.FromMilliseconds(300); // be polite to the RA API

    public async Task<int> SyncAsync(bool force, CancellationToken ct)
    {
        var apiKey = (await settings.GetAllSettingsAsync()).GetValueOrDefault(SettingsKeys.RetroAchievementsApiKey, "").Trim();
        if (apiKey.Length == 0)
        {
            log.LogInformation("RetroAchievements sync skipped — no API key configured");
            return 0;
        }

        var total = 0;
        foreach (var (console, raId) in RaConsoles.ByConsole)
        {
            ct.ThrowIfCancellationRequested();

            // Catalog side: MD5 -> game_id for this console. No catalog games → nothing to match.
            var byMd5 = (await catalog.GetVimmHashIndexAsync(console, ct)).ByMd5;
            if (byMd5.Count == 0) continue;

            // RA side: MD5 -> RA game id for the console's achievement-bearing games.
            var listJson = await ra.GetGameListAsync(raId, apiKey, ct);
            if (listJson is null) continue; // transient failure — skip this console, keep the rest
            var raByMd5 = RaGameList.BuildHashIndex(RaGameList.ParseGameList(listJson));
            if (raByMd5.Count == 0) continue;

            // Hash-join: catalog game_id -> RA game id (first matching MD5 per game wins).
            var matched = new Dictionary<long, int>();
            foreach (var (md5, gameId) in byMd5)
                if (raByMd5.TryGetValue(md5, out var raGameId))
                    matched.TryAdd(gameId, raGameId);
            if (matched.Count == 0) continue;

            var components = await catalog.GetRankComponentsForConsoleAsync(console, ct);

            var rows = new List<(long Id, int Players, double Score)>();
            foreach (var (gameId, raGameId) in matched)
            {
                ct.ThrowIfCancellationRequested();
                var comp = components.GetValueOrDefault(gameId);
                if (!force && comp?.RaPlayers is not null) continue; // incremental: already populated

                var extJson = await ra.GetGameExtendedAsync(raGameId, apiKey, ct);
                await Task.Delay(FetchDelay, ct);
                if (extJson is null || RaGameList.ParsePlayers(extJson) is not { } players) continue;

                rows.Add((gameId, players, RankScore.Blend(comp?.IgdbRating, comp?.IgdbCount, players)));
            }

            if (rows.Count == 0) continue;
            await catalog.SetRaPopularityAsync(rows, ct);
            total += rows.Count;
            log.LogInformation("RetroAchievements: {Console} → {Count} games ranked by popularity", console, rows.Count);
        }
        return total;
    }
}
