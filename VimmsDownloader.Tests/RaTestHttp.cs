using System.Net;
using System.Web;

namespace VimmsDownloader.Tests;

/// <summary>
/// Stub for the RetroAchievements endpoints used by the RA sync tests: routes by path (GetGameList vs
/// GetGameExtended), keys the responses by the `i` query param (console id / game id), counts calls, and
/// records the last API key seen. No network.
/// </summary>
sealed class StubRaHandler : HttpMessageHandler
{
    public int GameListCalls { get; private set; }
    public int ExtendedCalls { get; private set; }
    public string? LastApiKey { get; private set; }

    /// <summary>GetGameList response per console id. Default: empty array.</summary>
    public Func<int, (HttpStatusCode Code, string Body)> GameList { get; set; } =
        _ => (HttpStatusCode.OK, "[]");

    /// <summary>GetGameExtended response per RA game id. Default: a fixed player count.</summary>
    public Func<int, (HttpStatusCode Code, string Body)> Extended { get; set; } =
        id => (HttpStatusCode.OK, $"{{\"ID\":{id},\"NumDistinctPlayers\":1000}}");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var q = HttpUtility.ParseQueryString(uri.Query);
        LastApiKey = q["y"];
        var i = int.TryParse(q["i"], out var n) ? n : 0;

        HttpStatusCode code;
        string body;
        if (uri.AbsolutePath.Contains("GetGameList"))
        {
            GameListCalls++;
            (code, body) = GameList(i);
        }
        else
        {
            ExtendedCalls++;
            (code, body) = Extended(i);
        }
        return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }
}

sealed class StubRaFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
