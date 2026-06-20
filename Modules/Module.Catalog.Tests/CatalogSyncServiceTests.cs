using System.Net;
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
