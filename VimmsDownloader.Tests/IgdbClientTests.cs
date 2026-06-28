using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// Exercises the real <see cref="IgdbClient"/> against the stub Twitch/IGDB endpoints: token fetch +
/// in-memory caching (and re-fetch on a credential change), graceful null on a failed token, and that
/// game queries carry the Client-ID + Bearer headers IGDB requires.
/// </summary>
[TestClass]
public class IgdbClientTests
{
    private static IgdbClient New(StubIgdbHandler h) =>
        new(new StubIgdbFactory(h), NullLogger<IgdbClient>.Instance);

    [TestMethod]
    public async Task GetToken_FetchesThenCachesForSameClientId()
    {
        var handler = new StubIgdbHandler();
        var client = New(handler);

        var t1 = await client.GetTokenAsync("cid", "secret", default);
        var t2 = await client.GetTokenAsync("cid", "secret", default);

        Assert.AreEqual("tok-1", t1);
        Assert.AreEqual("tok-1", t2);           // served from the in-memory cache
        Assert.AreEqual(1, handler.TokenCalls);  // only one network fetch
    }

    [TestMethod]
    public async Task GetToken_RefetchesWhenClientIdChanges()
    {
        var handler = new StubIgdbHandler();
        var client = New(handler);

        var a = await client.GetTokenAsync("cid-a", "s", default);
        var b = await client.GetTokenAsync("cid-b", "s", default);

        Assert.AreEqual("tok-1", a);
        Assert.AreEqual("tok-2", b);             // different creds → fresh token
        Assert.AreEqual(2, handler.TokenCalls);
    }

    [TestMethod]
    public async Task GetToken_RefetchesWhenSecretChanges()
    {
        // #236: the cache must key on the secret too — rotating only the secret (same client id)
        // forces a fresh token rather than serving the one minted from the old secret.
        var handler = new StubIgdbHandler();
        var client = New(handler);

        var a = await client.GetTokenAsync("cid", "secret-1", default);
        var b = await client.GetTokenAsync("cid", "secret-2", default);

        Assert.AreEqual("tok-1", a);
        Assert.AreEqual("tok-2", b);             // same id, new secret → fresh token
        Assert.AreEqual(2, handler.TokenCalls);
    }

    [TestMethod]
    public async Task GetToken_NullWhenTokenRequestFails()
    {
        var handler = new StubIgdbHandler { Token = _ => (HttpStatusCode.Forbidden, "nope") };
        Assert.IsNull(await New(handler).GetTokenAsync("cid", "secret", default));
    }

    [TestMethod]
    public async Task QueryGames_SendsClientIdAndBearer_ReturnsBody()
    {
        var handler = new StubIgdbHandler { Games = _ => (HttpStatusCode.OK, "[{\"id\":1,\"name\":\"G\"}]") };
        var client = New(handler);

        var body = await client.QueryGamesAsync("my-cid", "my-token", "fields name;", default);

        Assert.AreEqual("[{\"id\":1,\"name\":\"G\"}]", body);
        Assert.AreEqual("my-cid", handler.LastClientId);
        Assert.AreEqual("Bearer my-token", handler.LastAuthorization);
    }

    [TestMethod]
    public async Task QueryGames_NullWhenRequestFails()
    {
        var handler = new StubIgdbHandler { Games = _ => (HttpStatusCode.TooManyRequests, "") };
        Assert.IsNull(await New(handler).QueryGamesAsync("cid", "tok", "fields name;", default));
    }
}
