namespace VimmsDownloader.Tests;

/// <summary>
/// Covers the Internet Archive S3 "LOW" auth header rule: present only when BOTH keys are set,
/// trimmed, and cleared when either is removed (host-internal via InternalsVisibleTo).
/// </summary>
[TestClass]
public class ArchiveAuthTests
{
    [TestMethod]
    public void BothKeysSet_ProducesLowHeader()
    {
        var auth = new ArchiveAuth();
        auth.Set("myaccess", "mysecret");
        Assert.AreEqual("LOW myaccess:mysecret", auth.Header?.ToString());
    }

    [TestMethod]
    public void EitherKeyBlankOrNull_NoHeader()
    {
        var auth = new ArchiveAuth();
        auth.Set("myaccess", "");
        Assert.IsNull(auth.Header);
        auth.Set("", "mysecret");
        Assert.IsNull(auth.Header);
        auth.Set("   ", "mysecret");
        Assert.IsNull(auth.Header);
        auth.Set(null, null);
        Assert.IsNull(auth.Header);
    }

    [TestMethod]
    public void Set_TrimsWhitespace()
    {
        var auth = new ArchiveAuth();
        auth.Set("  acc  ", "  sec  ");
        Assert.AreEqual("LOW acc:sec", auth.Header?.ToString());
    }

    [TestMethod]
    public void Set_ClearingASecretRemovesHeader()
    {
        var auth = new ArchiveAuth();
        auth.Set("acc", "sec");
        Assert.IsNotNull(auth.Header);
        auth.Set("acc", "");   // user cleared the secret
        Assert.IsNull(auth.Header);
    }

    [TestMethod]
    public async Task Handler_AddsAuthHeader_WhenBothKeysSet()
    {
        var auth = new ArchiveAuth();
        auth.Set("acc", "sec");
        HttpRequestMessage? seen = null;
        using var client = new HttpClient(new ArchiveAuthHandler(auth)
        {
            InnerHandler = new StubHandler(req => seen = req)
        });
        await client.GetAsync("https://archive.org/download/x");
        Assert.AreEqual("LOW acc:sec", seen?.Headers.Authorization?.ToString());
    }

    [TestMethod]
    public async Task Handler_AddsNoHeader_WhenKeysUnset()
    {
        var auth = new ArchiveAuth(); // never configured
        HttpRequestMessage? seen = null;
        using var client = new HttpClient(new ArchiveAuthHandler(auth)
        {
            InnerHandler = new StubHandler(req => seen = req)
        });
        await client.GetAsync("https://archive.org/download/x");
        Assert.IsNull(seen?.Headers.Authorization);
    }

    /// <summary>Captures the outgoing request and returns 200 without touching the network.</summary>
    private sealed class StubHandler(Action<HttpRequestMessage> onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            onSend(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
