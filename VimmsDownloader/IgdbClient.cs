using System.Text;
using System.Text.Json;

/// <summary>
/// Minimal IGDB API v4 client: a Twitch OAuth2 client-credentials token (cached in-memory until it nears
/// expiry) plus an Apicalypse POST to api.igdb.com. The Twitch client id/secret are supplied per call
/// from settings; with none configured callers no-op. Shared by the description sync (#138) and, later,
/// the IGDB ranking ingest (#140).
/// </summary>
class IgdbClient(IHttpClientFactory httpFactory, ILogger<IgdbClient> log)
{
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    private const string GamesUrl = "https://api.igdb.com/v4/games";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private string? _tokenClientId;
    private string? _tokenClientSecret;
    private DateTimeOffset _tokenExpiry;

    /// <summary>
    /// A valid bearer token for these creds, fetching + caching one when needed. Returns null if the
    /// token request fails (bad creds / network) — callers treat that as "IGDB unavailable".
    /// </summary>
    public async Task<string?> GetTokenAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            // Reuse a cached token issued for the same credentials while it has comfortable life left.
            // Both the id AND the secret must match, so rotating only the secret forces a fresh token.
            if (_token is not null && _tokenClientId == clientId && _tokenClientSecret == clientSecret &&
                _tokenExpiry - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
                return _token;

            var url = $"{TokenUrl}?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";
            using var resp = await httpFactory.CreateClient("igdb").PostAsync(url, null, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("IGDB token request failed: {Status}", (int)resp.StatusCode);
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("access_token", out var tok) || tok.ValueKind != JsonValueKind.String)
                return null;
            var expires = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var s) ? s : 3600;
            _token = tok.GetString();
            _tokenClientId = clientId;
            _tokenClientSecret = clientSecret;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expires);
            return _token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "IGDB token request errored");
            return null;
        }
        finally { _tokenLock.Release(); }
    }

    /// <summary>POST an Apicalypse query to <c>/v4/games</c>; returns the raw JSON array text, or null on failure.</summary>
    public async Task<string?> QueryGamesAsync(string clientId, string token, string apicalypse, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, GamesUrl);
            req.Headers.Add("Client-ID", clientId);
            req.Headers.Add("Authorization", $"Bearer {token}");
            req.Content = new StringContent(apicalypse, Encoding.UTF8, "text/plain");
            using var resp = await httpFactory.CreateClient("igdb").SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("IGDB games query failed: {Status}", (int)resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "IGDB games query errored");
            return null;
        }
    }
}
