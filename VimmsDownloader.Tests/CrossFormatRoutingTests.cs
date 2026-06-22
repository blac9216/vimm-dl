using DbMatch = QueueRepository.DuplicateDbMatch;

namespace VimmsDownloader.Tests;

/// <summary>
/// #149: endpoint-level cross-format / cross-source duplicate routing in <c>POST /api/queue</c>, extracted
/// into the testable <see cref="DownloadEndpoints"/>.BuildDuplicates. Covers the FormatLabel text, the
/// "different format → soft cross-format warning (skip the disk check)" branch, the queued branch, and the
/// crossSeen dedup that merges the URL pass and the game-id pass without double-reporting. The disk check
/// (pipeline + filesystem) is injected as a stub so the routing is exercised in isolation.
/// </summary>
[TestClass]
public class CrossFormatRoutingTests
{
    private static DbMatch Match(string url, string source = "completed", int? format = null,
        string? title = "Game", string? filename = "Game.7z") =>
        new(url, source, null, title, filename, null, "ps3", Format: format);

    /// <summary>Stand-in for the endpoint's pipeline/filesystem disk check: records calls, returns a fixed verdict.</summary>
    private sealed class StubDiskCheck
    {
        public List<string> Calls { get; } = [];
        public DuplicateInfo? Result { get; init; }
        public DuplicateInfo? Check(DbMatch m) { Calls.Add(m.Url); return Result; }
    }

    [TestMethod]
    public void DifferentFormat_RoutesToCrossFormatWarning_SkippingDiskCheck()
    {
        var disk = new StubDiskCheck();
        // Own format 0; queueing format 1 → soft cross-format warning, disk check NOT consulted.
        var dups = DownloadEndpoints.BuildDuplicates(
            [Match("https://vimm.net/vault/1", format: 0)], [], incomingFormat: 1, disk.Check);

        Assert.HasCount(1, dups);
        Assert.IsTrue(dups[0].CrossFormat);
        Assert.AreEqual(0, dups[0].ExistingFormat);
        Assert.AreEqual("Already have this game as JB Folder (.7z)", dups[0].Reason);
        Assert.IsEmpty(disk.Calls); // routed to cross-format → no disk check
    }

    [TestMethod]
    public void SameFormat_RoutesToDiskCheck_NotCrossFormat()
    {
        var disk = new StubDiskCheck
        {
            Result = new DuplicateInfo("https://vimm.net/vault/1", "completed", "Already downloaded",
                "Game", "Game.7z", null, true, false),
        };
        var dups = DownloadEndpoints.BuildDuplicates(
            [Match("https://vimm.net/vault/1", format: 1)], [], incomingFormat: 1, disk.Check);

        Assert.HasCount(1, dups);
        Assert.IsFalse(dups[0].CrossFormat);
        Assert.AreEqual("Already downloaded", dups[0].Reason);
        CollectionAssert.AreEqual(new[] { "https://vimm.net/vault/1" }, disk.Calls); // disk check consulted
    }

    [TestMethod]
    public void QueuedMatch_SameFormat_RoutesToQueuedReason()
    {
        var disk = new StubDiskCheck();
        var dups = DownloadEndpoints.BuildDuplicates(
            [Match("https://vimm.net/vault/1", source: "queued", format: 0)], [], incomingFormat: 0, disk.Check);

        Assert.HasCount(1, dups);
        Assert.AreEqual("queued", dups[0].Source);
        Assert.AreEqual("Already in download queue", dups[0].Reason);
        Assert.IsEmpty(disk.Calls);
    }

    [TestMethod]
    public void CrossFormat_DedupsAcrossUrlAndGamePasses()
    {
        var disk = new StubDiskCheck();
        // The same URL+format surfaces in BOTH the URL pass (dbMatches) and the game-id pass
        // (gameMatches) → exactly one cross-format entry (the crossSeen dedup).
        var m = Match("https://vimm.net/vault/1", format: 0);
        var dups = DownloadEndpoints.BuildDuplicates([m], [m], incomingFormat: 1, disk.Check);

        Assert.HasCount(1, dups);
        Assert.IsTrue(dups[0].CrossFormat);
    }

    [TestMethod]
    public void CrossSourceMatch_FromGamePass_IsReported()
    {
        var disk = new StubDiskCheck();
        // An archive copy of the same catalog game (different URL) the URL pass can't see, surfaced by
        // the game-id pass → a cross-format/source warning.
        var dups = DownloadEndpoints.BuildDuplicates(
            [], [Match("https://archive.org/download/x", source: "archive", format: 0)], incomingFormat: 1, disk.Check);

        Assert.HasCount(1, dups);
        Assert.AreEqual("https://archive.org/download/x", dups[0].Url);
        Assert.IsTrue(dups[0].CrossFormat);
    }

    [TestMethod]
    public void FormatLabel_MapsKnownAndUnknownFormats()
    {
        var disk = new StubDiskCheck();
        // Incoming format 99 differs from every existing format below, so each routes to cross-format.
        string Reason(int? fmt) => DownloadEndpoints.BuildDuplicates(
            [Match("https://vimm.net/vault/1", format: fmt)], [], incomingFormat: 99, disk.Check)[0].Reason;

        Assert.AreEqual("Already have this game as .dec.iso", Reason(1));
        Assert.AreEqual("Already have this game as format 5", Reason(5));
        // A null format can't be compared as "different", so it only reaches a cross-format warning via
        // the game-id pass (which always builds one) — that's where FormatLabel(null) is exercised.
        var nullFmt = DownloadEndpoints.BuildDuplicates(
            [], [Match("https://vimm.net/vault/1", format: null)], incomingFormat: 99, disk.Check)[0];
        Assert.AreEqual("Already have this game as another format", nullFmt.Reason);
    }
}
