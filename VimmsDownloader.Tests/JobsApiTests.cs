using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace VimmsDownloader.Tests;

/// <summary>
/// L1 #255 — the unified Jobs API. Covers <see cref="BackgroundJobGate"/>'s new progress surface
/// (<c>Kind</c>, <c>Report</c>, <c>Snapshot</c>, <c>Cancel</c>) and <see cref="JobsOps"/>, the extracted
/// <c>GET /api/jobs</c> / <c>POST /api/jobs/{kind}/cancel</c> logic — mirroring how
/// <c>CatalogQueueOps</c> is tested without an HTTP host.
/// </summary>
[TestClass]
public class JobsApiTests
{
    private sealed class TestGate() : BackgroundJobGate("test-kind");
    private sealed class OtherTestGate() : BackgroundJobGate("other-kind");

    // --- BackgroundJobGate: Report / Snapshot ---

    [TestMethod]
    public void Snapshot_BeforeAnyRun_AllProgressFieldsNull()
    {
        var gate = new TestGate();

        var snap = gate.Snapshot();

        Assert.AreEqual("test-kind", snap.Kind);
        Assert.IsFalse(snap.Running);
        Assert.IsNull(snap.Message);
        Assert.IsNull(snap.Current);
        Assert.IsNull(snap.Total);
        Assert.IsNull(snap.Percent);
        Assert.IsNull(snap.StartedAt);
        Assert.IsNull(snap.ElapsedMs);
    }

    [TestMethod]
    public void Report_UpdatesSnapshot_AndComputesPercent()
    {
        var gate = new TestGate();
        Assert.IsTrue(gate.TryBegin());

        gate.Report("Syncing X", 3, 4);
        var snap = gate.Snapshot();

        Assert.IsTrue(snap.Running);
        Assert.AreEqual("Syncing X", snap.Message);
        Assert.AreEqual(3, snap.Current);
        Assert.AreEqual(4, snap.Total);
        Assert.AreEqual(75.0, snap.Percent);
        Assert.IsNotNull(snap.StartedAt);
        Assert.IsGreaterThanOrEqualTo(0, snap.ElapsedMs!.Value);
    }

    [TestMethod]
    public void Report_TotalMissing_PercentIsNull()
    {
        var gate = new TestGate();
        gate.TryBegin();

        gate.Report("in progress", 5, null);

        Assert.IsNull(gate.Snapshot().Percent);
    }

    [TestMethod]
    public void Report_ZeroTotal_PercentIsNull()
    {
        var gate = new TestGate();
        gate.TryBegin();

        gate.Report("nothing to do", 0, 0);

        Assert.IsNull(gate.Snapshot().Percent);
    }

    [TestMethod]
    public void TryBegin_ResetsProgressFromAPriorRun()
    {
        var gate = new TestGate();
        gate.TryBegin();
        gate.Report("first run", 1, 2);
        gate.End();

        gate.TryBegin();
        var snap = gate.Snapshot();

        Assert.IsNull(snap.Message);
        Assert.IsNull(snap.Current);
        Assert.IsNull(snap.Total);
    }

    [TestMethod]
    public void ElapsedMs_SurvivesAfterEnd_UntilNextRun()
    {
        var gate = new TestGate();
        gate.TryBegin();
        gate.End();

        // StartedAt/elapsed reflect the last run even once it's finished (Running flips back to false).
        var snap = gate.Snapshot();
        Assert.IsFalse(snap.Running);
        Assert.IsNotNull(snap.StartedAt);
        Assert.IsGreaterThanOrEqualTo(0, snap.ElapsedMs!.Value);
    }

    // --- BackgroundJobGate: Run — last-run fields (#292) ---

    [TestMethod]
    public async Task Run_Completes_RecordsCompletedOutcomeAndDuration()
    {
        var gate = new TestGate();

        gate.Run(NullLogger.Instance, "Test", _ => Task.CompletedTask);
        await WaitUntilStopped(gate);

        var snap = gate.Snapshot();
        Assert.AreEqual("completed", snap.LastOutcome);
        Assert.IsNotNull(snap.LastCompletedAt);
        Assert.IsGreaterThanOrEqualTo(0, snap.LastDurationMs!.Value);
    }

    [TestMethod]
    public async Task Run_Cancelled_RecordsCancelledOutcome()
    {
        var gate = new TestGate();

        gate.Run(NullLogger.Instance, "Test", _ => throw new OperationCanceledException());
        await WaitUntilStopped(gate);

        Assert.AreEqual("cancelled", gate.Snapshot().LastOutcome);
    }

    [TestMethod]
    public async Task Run_Throws_RecordsFailedOutcome()
    {
        var gate = new TestGate();

        gate.Run(NullLogger.Instance, "Test", _ => throw new InvalidOperationException("boom"));
        await WaitUntilStopped(gate);

        Assert.AreEqual("failed", gate.Snapshot().LastOutcome);
    }

    [TestMethod]
    public async Task Run_SecondRun_OverwritesLastRunFields()
    {
        var gate = new TestGate();
        gate.Run(NullLogger.Instance, "Test", _ => Task.CompletedTask);
        await WaitUntilStopped(gate);
        var firstCompletedAt = gate.Snapshot().LastCompletedAt;

        gate.Run(NullLogger.Instance, "Test", _ => throw new InvalidOperationException("boom"));
        await WaitUntilStopped(gate);

        var snap = gate.Snapshot();
        Assert.AreEqual("failed", snap.LastOutcome);
        Assert.IsGreaterThanOrEqualTo(firstCompletedAt!.Value, snap.LastCompletedAt!.Value);
    }

    [TestMethod]
    public void Snapshot_BeforeAnyRun_LastRunFieldsNull()
    {
        var gate = new TestGate();

        var snap = gate.Snapshot();

        Assert.IsNull(snap.LastCompletedAt);
        Assert.IsNull(snap.LastDurationMs);
        Assert.IsNull(snap.LastOutcome);
    }

    private static async Task WaitUntilStopped(BackgroundJobGate gate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (gate.IsRunning && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsFalse(gate.IsRunning, "gate should have finished running within the timeout");
    }

    // --- BackgroundJobGate: Cancel ---

    [TestMethod]
    public void Cancel_WhileRunning_SignalsToken()
    {
        var gate = new TestGate();
        gate.TryBegin();
        var token = gate.Token;
        Assert.IsFalse(token.IsCancellationRequested);

        gate.Cancel();

        Assert.IsTrue(token.IsCancellationRequested);
    }

    [TestMethod]
    public void Cancel_WhenNotRunning_IsSafeNoOp()
    {
        var gate = new TestGate();

        gate.Cancel(); // must not throw

        Assert.IsFalse(gate.IsRunning);
    }

    // --- JobsOps.Cancel: 404 / 409 / 204 status-code matrix ---

    [TestMethod]
    public void Cancel_UnknownKind_ReturnsNotFound()
    {
        var gate = new TestGate();

        var result = JobsOps.Cancel("nope", [gate]);

        Assert.AreEqual(404, (result as IStatusCodeHttpResult)?.StatusCode);
    }

    [TestMethod]
    public void Cancel_KnownKind_NotRunning_ReturnsConflict()
    {
        var gate = new TestGate();

        var result = JobsOps.Cancel("test-kind", [gate]);

        Assert.AreEqual(409, (result as IStatusCodeHttpResult)?.StatusCode);
    }

    [TestMethod]
    public void Cancel_KnownKind_Running_ReturnsNoContent_AndSignalsToken()
    {
        var gate = new TestGate();
        gate.TryBegin();
        var token = gate.Token;

        var result = JobsOps.Cancel("test-kind", [gate]);

        Assert.AreEqual(204, (result as IStatusCodeHttpResult)?.StatusCode);
        Assert.IsTrue(token.IsCancellationRequested);
    }

    [TestMethod]
    public void Cancel_KindLookup_IsCaseInsensitive()
    {
        var gate = new TestGate();
        gate.TryBegin();

        var result = JobsOps.Cancel("TEST-KIND", [gate]);

        Assert.AreEqual(204, (result as IStatusCodeHttpResult)?.StatusCode);
    }

    [TestMethod]
    public void Cancel_AfterGateEnds_RunAgainThenCancelSucceeds()
    {
        var gate = new TestGate();
        gate.TryBegin();
        gate.End(); // job finished on its own — gate resets so it can be re-run

        Assert.AreEqual(409, (JobsOps.Cancel("test-kind", [gate]) as IStatusCodeHttpResult)?.StatusCode);

        gate.TryBegin();
        Assert.AreEqual(204, (JobsOps.Cancel("test-kind", [gate]) as IStatusCodeHttpResult)?.StatusCode);
    }

    // --- JobsOps.Snapshot ---

    [TestMethod]
    public void Snapshot_ReturnsOneRowPerGate()
    {
        var a = new TestGate();
        var b = new OtherTestGate();
        a.TryBegin();
        a.Report("running", 1, 10);

        var rows = JobsOps.Snapshot([a, b]);

        Assert.HasCount(2, rows);
        Assert.IsTrue(rows.Any(r => r.Kind == "test-kind" && r.Running));
        Assert.IsTrue(rows.Any(r => r.Kind == "other-kind" && !r.Running));
    }
}
