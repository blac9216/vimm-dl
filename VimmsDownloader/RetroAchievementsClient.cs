using System.Web;

/// <summary>
/// Minimal RetroAchievements Web API client: the system game list (with recognised ROM hashes) and a
/// single game's extended details (for <c>NumDistinctPlayers</c>). Auth is the user's web API key passed
/// as the <c>y</c> query param on every call; callers no-op when none is configured. Plain HTTPS GETs to
/// retroachievements.org. Used by the RA popularity ingest (epic #123 / R2).
/// </summary>
class RetroAchievementsClient(IHttpClientFactory httpFactory, ILogger<RetroAchievementsClient> log)
{
    private const string Base = "https://retroachievements.org/API";

    /// <summary>
    /// The game list for a system, with each game's recognised ROM MD5 hashes (<c>h=1</c>) and only
    /// games that have achievements (<c>f=1</c> — a smaller, popularity-biased set). Raw JSON, or null
    /// on failure.
    /// </summary>
    public Task<string?> GetGameListAsync(int consoleId, string apiKey, CancellationToken ct) =>
        GetAsync($"{Base}/API_GetGameList.php?i={consoleId}&h=1&f=1&y={Key(apiKey)}", ct);

    /// <summary>A single game's extended details (carries <c>NumDistinctPlayers</c>). Raw JSON, or null on failure.</summary>
    public Task<string?> GetGameExtendedAsync(int raGameId, string apiKey, CancellationToken ct) =>
        GetAsync($"{Base}/API_GetGameExtended.php?i={raGameId}&y={Key(apiKey)}", ct);

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await httpFactory.CreateClient("ra").GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("RetroAchievements request failed: {Status}", (int)resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "RetroAchievements request errored");
            return null;
        }
    }

    private static string Key(string apiKey) => HttpUtility.UrlEncode(apiKey);
}
