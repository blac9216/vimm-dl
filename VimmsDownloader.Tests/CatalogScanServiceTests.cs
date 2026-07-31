using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// L1 #255 — <see cref="CatalogScanService"/> gained a Report checkpoint (once per
/// <c>completed/{console}/</c> folder) plus cancellation checks it previously lacked entirely. Exercises
/// both against real files/temp DB, mirroring <see cref="CatalogImportServiceTests"/>.
/// </summary>
[TestClass]
public class CatalogScanServiceTests
{
    private string _dir = null!;
    private string _connStr = null!;
    private CatalogRepository _catalog = null!;
    private QueueRepository _queue = null!;
    private CatalogScanService _svc = null!;
    private string _completedDir = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"catscan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "data"));
        _connStr = $"Data Source={Path.Combine(_dir, "data", "queue.db")}";

        _queue = new QueueRepository();
        await _queue.InitAsync(_connStr, NullLogger.Instance);
        _catalog = new CatalogRepository();
        _catalog.Configure(_connStr);
        _svc = new CatalogScanService(_catalog, _queue, NullLogger<CatalogScanService>.Instance);

        _completedDir = Path.Combine(_queue.GetDownloadPath(), "completed");
        Directory.CreateDirectory(_completedDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [TestMethod]
    public async Task ScanAsync_ReportsProgressPerConsoleFolder()
    {
        Drop("snes", "Cat.sfc");
        Drop("gba", "Cat.gba");

        var seen = new List<(string? Message, int? Current, int? Total)>();
        await _svc.ScanAsync((msg, cur, tot) => seen.Add((msg, cur, tot)), default);

        Assert.HasCount(2, seen);
        Assert.IsTrue(seen.All(s => s.Total == 2));
        CollectionAssert.AreEquivalent(new[] { "snes", "gba" }, seen.Select(s => s.Message).ToList());
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, seen.Select(s => s.Current).ToList());
    }

    [TestMethod]
    public async Task ScanAsync_NoReportCallback_StillWorks()
    {
        Drop("snes", "Cat.sfc");

        var owned = await _svc.ScanAsync(default);

        Assert.AreEqual(0, owned); // no catalog game seeded to match — just proving the null-report path doesn't throw
    }

    [TestMethod]
    public async Task ScanAsync_Cancelled_ThrowsAndStopsBeforeSecondFolder()
    {
        Drop("snes", "Cat.sfc");
        Drop("gba", "Cat.gba");
        Drop("nes", "Cat.nes");
        using var cts = new CancellationTokenSource();

        var reported = 0;
        var thrown = false;
        try
        {
            await _svc.ScanAsync((_, _, _) => { reported++; if (reported == 1) cts.Cancel(); }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            thrown = true;
        }

        Assert.IsTrue(thrown, "ScanAsync should exit via OperationCanceledException once cancelled");
        Assert.AreEqual(1, reported, "cancellation should stop before a second console folder is reported");
    }

    private void Drop(string console, string name)
    {
        var dir = Path.Combine(_completedDir, console);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), "x");
    }
}
