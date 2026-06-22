using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class LibretroDatSourceTests
{
    private const string GbaDat = """
        clrmamepro ( name "Nintendo - Game Boy Advance" version "2026.05.02" )
        game ( name "Advance Wars (USA)" region "USA" rom ( name "Advance Wars (USA).gba" size 4194304 crc DBEF116C ) )
        """;

    private static readonly CatalogSystemInfo Gba = new("Nintendo - Game Boy Advance", "no-intro", "gba");

    [TestMethod]
    public async Task GetDat_BuildsLibretroUrl_WithGroupAndEncodedName()
    {
        string? requested = null;
        var src = new LibretroDatSource(
            new HttpClient(new StubHandler(req => { requested = req.RequestUri!.AbsoluteUri; return (HttpStatusCode.OK, GbaDat); })),
            NullLogger<LibretroDatSource>.Instance);

        await src.GetDatAsync(new CatalogSystemInfo("Sony - PlayStation 3", "redump", "ps3"), default);

        StringAssert.Contains(requested!, "/metadat/redump/");
        StringAssert.Contains(requested!, "Sony%20-%20PlayStation%203.dat");
    }

    [TestMethod]
    public async Task GetDat_Success_ReturnsBody()
    {
        var src = new LibretroDatSource(
            new HttpClient(new StubHandler(_ => (HttpStatusCode.OK, GbaDat))), NullLogger<LibretroDatSource>.Instance);

        var r = await src.GetDatAsync(Gba, default);

        Assert.IsTrue(r.IsOk, r.Error);
        StringAssert.Contains(r.Value!, "Advance Wars");
    }

    [TestMethod]
    public async Task GetDat_HttpError_Fails()
    {
        var src = new LibretroDatSource(
            new HttpClient(new StubHandler(_ => (HttpStatusCode.NotFound, "nope"))), NullLogger<LibretroDatSource>.Instance);

        var r = await src.GetDatAsync(new CatalogSystemInfo("X", "no-intro", "x"), default);

        Assert.IsFalse(r.IsOk);
    }

    [TestMethod]
    public void DefaultInterSystemDelay_IsNonZero()
        => Assert.IsTrue(new LibretroDatSource(new HttpClient(new StubHandler(_ => (HttpStatusCode.OK, ""))),
            NullLogger<LibretroDatSource>.Instance).InterSystemDelay > TimeSpan.Zero);

    // ---- rate-limit-safe fetch (retry/backoff), no real sleeping (Delay recorder) ----

    private static LibretroDatSource Scripted(List<TimeSpan> waits, Func<int, HttpResponseMessage> script)
    {
        var src = new LibretroDatSource(new HttpClient(new ScriptedHandler(script)), NullLogger<LibretroDatSource>.Instance);
        src.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        return src;
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body = "", TimeSpan? retryAfter = null, string? remaining = null)
    {
        var m = new HttpResponseMessage(code) { Content = new StringContent(body) };
        if (retryAfter is { } ra) m.Headers.RetryAfter = new RetryConditionHeaderValue(ra);
        if (remaining is not null) m.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", remaining);
        return m;
    }

    [TestMethod]
    public async Task GetDat_RetriesOn429_ThenSucceeds()
    {
        var waits = new List<TimeSpan>();
        var src = Scripted(waits, call => call == 0 ? Resp(HttpStatusCode.TooManyRequests) : Resp(HttpStatusCode.OK, GbaDat));

        var r = await src.GetDatAsync(Gba, default);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(1, waits);                              // one backoff between the two attempts
        Assert.AreEqual(TimeSpan.FromSeconds(2), waits[0]);     // default first-retry backoff
    }

    [TestMethod]
    public async Task GetDat_HonorsRetryAfterHeader_OverBackoff()
    {
        var waits = new List<TimeSpan>();
        var src = Scripted(waits,
            call => call == 0 ? Resp(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(5)) : Resp(HttpStatusCode.OK, GbaDat));

        var r = await src.GetDatAsync(Gba, default);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(TimeSpan.FromSeconds(5), waits[0]);     // server's Retry-After wins over the 2s default
    }

    [TestMethod]
    public async Task GetDat_403WithNoRemainingQuota_IsRetried()
    {
        var waits = new List<TimeSpan>();
        var src = Scripted(waits,
            call => call == 0 ? Resp(HttpStatusCode.Forbidden, remaining: "0") : Resp(HttpStatusCode.OK, GbaDat));

        var r = await src.GetDatAsync(Gba, default);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(1, waits);
    }

    [TestMethod]
    public async Task GetDat_PlainForbidden_FailsImmediately_NoRetry()
    {
        var waits = new List<TimeSpan>();
        var src = Scripted(waits, _ => Resp(HttpStatusCode.Forbidden));   // no rate-limit header

        var r = await src.GetDatAsync(new CatalogSystemInfo("X", "no-intro", "x"), default);

        Assert.IsFalse(r.IsOk);
        Assert.IsEmpty(waits);     // a non-rate-limit 403 is not retried
    }

    [TestMethod]
    public async Task GetDat_Persistent429_ExhaustsAttempts_Fails()
    {
        var waits = new List<TimeSpan>();
        var src = Scripted(waits, _ => Resp(HttpStatusCode.TooManyRequests));

        var r = await src.GetDatAsync(new CatalogSystemInfo("X", "no-intro", "x"), default);

        Assert.IsFalse(r.IsOk);
        Assert.HasCount(3, waits);                 // 4 attempts → 3 backoffs
        Assert.AreEqual(TimeSpan.FromSeconds(2), waits[0]);   // 2s, 4s, 8s (capped at 30s)
        Assert.AreEqual(TimeSpan.FromSeconds(4), waits[1]);
        Assert.AreEqual(TimeSpan.FromSeconds(8), waits[2]);
    }

    [TestMethod]
    public async Task GetDat_TransientException_IsRetried_ThenSucceeds()
    {
        var waits = new List<TimeSpan>();
        var src = Scripted(waits,
            call => call == 0 ? throw new HttpRequestException("connection reset") : Resp(HttpStatusCode.OK, GbaDat));

        var r = await src.GetDatAsync(Gba, default);

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.HasCount(1, waits);
    }

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> script) : HttpMessageHandler
    {
        private int _call;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(script(_call++));
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
