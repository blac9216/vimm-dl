using System.Net;

namespace VimmsDownloader.Tests;

/// <summary>
/// Stub for the IGDB + Twitch endpoints used by the IGDB client/sync tests: routes by host (twitch =
/// token, else = games), counts calls, and records the last games request's auth headers. No network.
/// </summary>
sealed class StubIgdbHandler : HttpMessageHandler
{
    public int TokenCalls { get; private set; }
    public int GamesCalls { get; private set; }
    public string? LastClientId { get; private set; }
    public string? LastAuthorization { get; private set; }

    /// <summary>Token response per token-call index (1-based). Default: a fresh token + long expiry.</summary>
    public Func<int, (HttpStatusCode Code, string Body)> Token { get; set; } =
        i => (HttpStatusCode.OK, $"{{\"access_token\":\"tok-{i}\",\"expires_in\":5184000}}");

    /// <summary>Games response per request body (the Apicalypse query). Default: empty array.</summary>
    public Func<string, (HttpStatusCode Code, string Body)> Games { get; set; } =
        _ => (HttpStatusCode.OK, "[]");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpStatusCode code;
        string body;
        if (request.RequestUri!.Host.Contains("twitch"))
        {
            TokenCalls++;
            (code, body) = Token(TokenCalls);
        }
        else
        {
            GamesCalls++;
            LastClientId = request.Headers.TryGetValues("Client-ID", out var ci) ? ci.FirstOrDefault() : null;
            LastAuthorization = request.Headers.Authorization?.ToString();
            var query = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            (code, body) = Games(query);
        }
        return new HttpResponseMessage(code) { Content = new StringContent(body) };
    }
}

sealed class StubIgdbFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
