using Microsoft.Extensions.Logging.Abstractions;
using Module.Core;

[TestClass]
public class CatalogSyncServiceTests
{
    private const string GbaDat = """
        clrmamepro ( name "Nintendo - Game Boy Advance" version "2026.05.02" )
        game ( name "Advance Wars (USA)" region "USA" serial "AWRE" rom ( name "Advance Wars (USA).gba" size 4194304 crc DBEF116C ) )
        game ( name "Mother 3 (Japan)" region "Japan" rom ( name "Mother 3 (Japan).gba" size 33554432 crc ABCDEF12 ) )
        """;

    private static CatalogSyncService NewService(FakeStore store)
        => new(store, NullLogger<CatalogSyncService>.Instance);

    [TestMethod]
    public async Task SyncSystem_FetchesParsesPersists()
    {
        var store = new FakeStore();
        var svc = NewService(store);

        var r = await svc.SyncSystemAsync(
            new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"),
            new FakeSource(_ => Result<string>.Ok(GbaDat)));

        Assert.IsTrue(r.IsOk, r.Error);
        Assert.AreEqual(2, r.Value);
        Assert.HasCount(1, store.Systems);
        Assert.AreEqual(("Nintendo - Game Boy Advance", "gba", "no-intro"), store.Systems[0]);
        Assert.HasCount(2, store.Games);
        Assert.AreEqual("2026.05.02", store.LastVersion);
    }

    [TestMethod]
    public async Task SyncSystem_SourceFailure_Fails_NoPersist()
    {
        var store = new FakeStore();
        var svc = NewService(store);

        var r = await svc.SyncSystemAsync(
            new CatalogSystemInfo("X", "no-intro", "x"),
            new FakeSource(_ => Result<string>.Fail("HTTP 404 for X")));

        Assert.IsFalse(r.IsOk);
        Assert.IsEmpty(store.Systems);
    }

    [TestMethod]
    public async Task Sync_SkipsFailedSystem_ContinuesOthers()
    {
        var store = new FakeStore();
        var svc = NewService(store);
        var source = new FakeSource(sys =>
            sys.DatName.Contains("Game Boy Advance")
                ? Result<string>.Ok(GbaDat)
                : Result<string>.Fail("no DAT"));

        var summary = await svc.SyncAsync(
        [
            new CatalogSystemInfo("Sega - Missing", "no-intro", "x"),
            new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"),
        ], source);

        Assert.AreEqual(1, summary.SystemsSynced);
        Assert.AreEqual(1, summary.SystemsFailed);
        Assert.AreEqual(2, summary.TotalGames);
        Assert.HasCount(1, store.Systems); // only the successful system persisted
    }

    [TestMethod]
    public async Task Sync_ThrottlesBetweenSystems_BySourceDelay()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = NewService(store);
        svc.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        var source = new FakeSource(_ => Result<string>.Ok(GbaDat)) { InterSystemDelay = TimeSpan.FromMilliseconds(100) };

        await svc.SyncAsync(
        [
            new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"),
            new CatalogSystemInfo("Sony - PlayStation 3", "redump", "ps3"),
        ], source);

        // The only recorded wait is the single inter-system pause (none before the first).
        Assert.HasCount(1, waits);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), waits[0]);
    }

    [TestMethod]
    public async Task Sync_ZeroSourceDelay_NoThrottle()
    {
        var store = new FakeStore();
        var waits = new List<TimeSpan>();
        var svc = NewService(store);
        svc.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        var source = new FakeSource(_ => Result<string>.Ok(GbaDat)); // InterSystemDelay = Zero (bundle-like)

        await svc.SyncAsync(
        [
            new CatalogSystemInfo("Nintendo - Game Boy Advance", "no-intro", "gba"),
            new CatalogSystemInfo("Sony - PlayStation 3", "redump", "ps3"),
        ], source);

        Assert.IsEmpty(waits);
    }

    private sealed class FakeSource(Func<CatalogSystemInfo, Result<string>> responder) : IDatSource
    {
        public string Origin { get; init; } = "libretro";
        public TimeSpan InterSystemDelay { get; init; } = TimeSpan.Zero;
        public Task<Result<string>> GetDatAsync(CatalogSystemInfo sys, CancellationToken ct)
            => Task.FromResult(responder(sys));
    }

    private sealed class FakeStore : ICatalogStore
    {
        public readonly List<(string Dat, string Console, string Source)> Systems = [];
        public readonly List<DatGame> Games = [];
        public string? LastVersion;
        public string? LastOrigin;

        public Task<long> UpsertSystemAsync(string datName, string console, string source, CancellationToken ct)
        {
            Systems.Add((datName, console, source));
            return Task.FromResult((long)Systems.Count);
        }

        public Task MergeSystemGamesAsync(long systemId, string origin, IReadOnlyList<DatGame> games, string? datVersion, CancellationToken ct)
        {
            Games.AddRange(games);
            LastVersion = datVersion;
            LastOrigin = origin;
            return Task.CompletedTask;
        }
    }
}
