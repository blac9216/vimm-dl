using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class CatalogSyncServiceTests
{
    private const string GbaDat = """
        clrmamepro ( name "Nintendo - Game Boy Advance" version "2026.05.02" )
        game ( name "Advance Wars (USA)" region "USA" serial "AWRE" rom ( name "Advance Wars (USA).gba" size 4194304 crc DBEF116C ) )
        game ( name "Mother 3 (Japan)" region "Japan" rom ( name "Mother 3 (Japan).gba" size 33554432 crc ABCDEF12 ) )
        """;

    private static CatalogSyncService NewService(FakeStore store, Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
        => new(new HttpClient(new StubHandler(responder)), store, NullLogger<CatalogSyncService>.Instance);

    [TestMethod]
    public async Task SyncSystem_FetchesParsesPersists()
    {
        var store = new FakeStore();
        var svc = NewService(store, _ => (HttpStatusCode.OK, GbaDat));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(2, r.Value);
        Assert.HasCount(1, store.Systems);
        Assert.AreEqual(("Nintendo - Game Boy Advance", "gba", "no-intro"), store.Systems[0]);
        Assert.HasCount(2, store.Games);
        Assert.AreEqual("2026.05.02", store.LastVersion);
    }

    [TestMethod]
    public async Task SyncSystem_BuildsLibretroUrl_WithGroupAndEncodedName()
    {
        var store = new FakeStore();
        string? requested = null;
        var svc = NewService(store, req => { requested = req.RequestUri!.AbsoluteUri; return (HttpStatusCode.OK, GbaDat); });

        await svc.SyncSystemAsync(new CatalogSystemInfo("Sony - PlayStation 3", "redump", "ps3"));

        StringAssert.Contains(requested!, "/metadat/redump/");
        StringAssert.Contains(requested!, "Sony%20-%20PlayStation%203.dat");
    }

    [TestMethod]
    public async Task SyncSystem_HttpError_Fails_NoPersist()
    {
        var store = new FakeStore();
        var svc = NewService(store, _ => (HttpStatusCode.NotFound, "nope"));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("X", "no-intro", "x"));

        Assert.IsFalse(r.IsOk);
        Assert.IsEmpty(store.Systems);
    }

    [TestMethod]
    public async Task Sync_SkipsFailedSystem_ContinuesOthers()
    {
        var store = new FakeStore();
        var svc = NewService(store, req =>
            req.RequestUri!.AbsoluteUri.Contains("Game%20Boy%20Advance")
                ? (HttpStatusCode.OK, GbaDat)
                : (HttpStatusCode.NotFound, "nope"));

        var summary = await svc.SyncAsync(
        [
            new CatalogSystemInfo("Sega - Missing", "no-intro", "x"),
            new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"),
        ]);

        Assert.AreEqual(1, summary.SystemsSynced);
        Assert.AreEqual(1, summary.SystemsFailed);
        Assert.AreEqual(2, summary.TotalGames);
        Assert.HasCount(1, store.Systems); // only the successful system persisted
    }

    // ---- D3a: rate-limit-safe fetch (retry/backoff + throttle), no real sleeping (Delay recorder) ----

    /// <summary>Build a scripted service whose Delay is recorded into <paramref name="waits"/> instead of slept.</summary>
    private static CatalogSyncService ScriptedService(FakeStore store, List<TimeSpan> waits, Func<int, HttpResponseMessage> script)
    {
        var svc = new CatalogSyncService(new HttpClient(new ScriptedHandler(script)), store, NullLogger<CatalogSyncService>.Instance);
        svc.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        return svc;
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body = "", TimeSpan? retryAfter = null, string? remaining = null)
    {
        var m = new HttpResponseMessage(code) { Content = new StringContent(body) };
        if (retryAfter is { } ra) m.Headers.RetryAfter = new RetryConditionHeaderValue(ra);
        if (remaining is not null) m.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", remaining);
        return m;
    }

    [TestMethod]
    public async Task SyncSystem_RetriesOn429_ThenSucceeds()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits, call => call == 0 ? Resp(HttpStatusCode.TooManyRequests) : Resp(HttpStatusCode.OK, GbaDat));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(2, r.Value);
        Assert.HasCount(1, waits);                              // one backoff between the two attempts
        Assert.AreEqual(TimeSpan.FromSeconds(2), waits[0]);     // default first-retry backoff
    }

    [TestMethod]
    public async Task SyncSystem_HonorsRetryAfterHeader_OverBackoff()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits,
            call => call == 0 ? Resp(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(5)) : Resp(HttpStatusCode.OK, GbaDat));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(TimeSpan.FromSeconds(5), waits[0]);     // server's Retry-After wins over the 2s default
    }

    [TestMethod]
    public async Task SyncSystem_403WithNoRemainingQuota_IsRetried()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits,
            call => call == 0 ? Resp(HttpStatusCode.Forbidden, remaining: "0") : Resp(HttpStatusCode.OK, GbaDat));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(1, waits);
    }

    [TestMethod]
    public async Task SyncSystem_PlainForbidden_FailsImmediately_NoRetry()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits, _ => Resp(HttpStatusCode.Forbidden));   // no rate-limit header

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("X", "no-intro", "x"));

        Assert.IsFalse(r.IsOk);
        Assert.IsEmpty(waits);     // a non-rate-limit 403 is not retried
    }

    [TestMethod]
    public async Task SyncSystem_Persistent429_ExhaustsAttempts_Fails()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits, _ => Resp(HttpStatusCode.TooManyRequests));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("X", "no-intro", "x"));

        Assert.IsFalse(r.IsOk);
        Assert.HasCount(3, waits);                 // 4 attempts → 3 backoffs
        Assert.AreEqual(TimeSpan.FromSeconds(2), waits[0]);   // 2s, 4s, 8s (capped at 30s)
        Assert.AreEqual(TimeSpan.FromSeconds(4), waits[1]);
        Assert.AreEqual(TimeSpan.FromSeconds(8), waits[2]);
    }

    [TestMethod]
    public async Task SyncSystem_TransientException_IsRetried_ThenSucceeds()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits,
            call => call == 0 ? throw new HttpRequestException("connection reset") : Resp(HttpStatusCode.OK, GbaDat));

        var r = await svc.SyncSystemAsync(new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(1, waits);
    }

    [TestMethod]
    public async Task Sync_ThrottlesBetweenSystems()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = ScriptedService(store, waits, _ => Resp(HttpStatusCode.OK, GbaDat));
        svc.InterSystemDelay = TimeSpan.FromMilliseconds(100);

        await svc.SyncAsync(
        [
            new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"),
            new CatalogSystemInfo("Sony - PlayStation 3", "redump", "ps3"),
        ]);

        // No retries (all 200) → the only recorded wait is the single inter-system pause (none before the first).
        Assert.HasCount(1, waits);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), waits[0]);
    }

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> script) : HttpMessageHandler
    {
        private int _call;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(script(_call++));
    }

    private sealed class FakeStore : ICatalogStore
    {
        public readonly List<(string Dat, string Console, string Source)> Systems = [];
        public readonly List<DatGame> Games = [];
        public string? LastVersion;

        public Task<long> UpsertSystemAsync(string datName, string console, string source, CancellationToken ct)
        {
            Systems.Add((datName, console, source));
            return Task.FromResult((long)Systems.Count);
        }

        public Task ReplaceSystemGamesAsync(long systemId, IReadOnlyList<DatGame> games, string? datVersion, CancellationToken ct)
        {
            Games.AddRange(games);
            LastVersion = datVersion;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (status, body) = responder(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
