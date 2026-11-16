using LegendsOfTheDungeon.Platform.Diagnostics;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void MetricDimensionsStayAtTwentyDespiteManyUniqueCorrelations()
    {
        var recorder = new FixedMetricRecorder(10);
        Assert.Equal(20, FixedMetricRecorder.MaximumSeries);
        for (var i = 0; i < 50_000; i++)
        {
            recorder.Record((DiagnosticArea)(i % 5), DiagnosticOutcome.Succeeded, i % 20);
        }

        var snapshot = recorder.Snapshot();
        Assert.Equal(20, snapshot.Count);
        Assert.Equal(50_000, snapshot.Sum(static x => x.Count));
        Assert.Equal(10_000L, snapshot.Single(static x =>
            x.Area == DiagnosticArea.Tick && x.Outcome == DiagnosticOutcome.Succeeded).Count);
        Assert.All(snapshot, static x => Assert.True(x.Count is >= 0));
    }

    [Fact]
    public void SaturatingMetricCountsCannotOverflow()
    {
        var recorder = new FixedMetricRecorder(1);
        recorder.Record(DiagnosticArea.Database, DiagnosticOutcome.Failed, long.MaxValue);
        recorder.Record(DiagnosticArea.Database, DiagnosticOutcome.Failed, long.MaxValue);
        var cell = recorder.Snapshot().Single(static x =>
            x.Area == DiagnosticArea.Database && x.Outcome == DiagnosticOutcome.Failed);
        Assert.Equal(2L, cell.Count);
        Assert.Equal(long.MaxValue, cell.TotalMilliseconds);
        Assert.Equal(long.MaxValue, cell.MaximumMilliseconds);
        Assert.Equal(2L, cell.ThresholdExceeded);
    }

    [Fact]
    public void SlowHistoryIsBoundedAndPreservesCorrelationWithoutPayloadText()
    {
        var slow = new SlowOperationDetector(2, 50);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        Assert.False(slow.Observe(DiagnosticArea.Tick, first, 49));
        Assert.True(slow.Observe(DiagnosticArea.Tick, first, 50));
        Assert.True(slow.Observe(DiagnosticArea.Database, second, 100));
        Assert.True(slow.Observe(DiagnosticArea.Script, third, 500));
        Assert.Equal(3L, slow.TotalSlowOperations);
        Assert.Equal([second, third], slow.Snapshot().Select(static x => x.Correlation));
        Assert.Equal([100L, 500L], slow.Snapshot().Select(static x => x.ElapsedMilliseconds));
    }

    [Fact]
    public void TraceDropsOldEntriesWithoutLeakingIntoMetrics()
    {
        var trace = new DiagnosticTraceBuffer(3);
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            trace.Record(DiagnosticArea.Queue, DiagnosticTraceStage.Started, id);
        }

        Assert.Equal(ids[^3..], trace.Snapshot().Select(static x => x.Correlation));
        Assert.Equal([18L, 19L, 20L], trace.Snapshot().Select(static x => x.Sequence));
        Assert.Equal(3, trace.Snapshot().Count);
    }

    [Fact]
    public void HealthIsFailClosedUntilEachActualProbeReportsHealthy()
    {
        var health = new FixedHealthRegistry();
        Assert.False(health.AllHealthy);
        Assert.Equal(5, health.Snapshot().Count);
        Assert.All(health.Snapshot(), static item =>
            Assert.Equal(HealthCondition.Unknown, item.Condition));
        foreach (var component in Enum.GetValues<HealthComponent>())
        {
            health.Report(component, HealthCondition.Healthy);
        }

        Assert.True(health.AllHealthy);
        health.Report(HealthComponent.Persistence, HealthCondition.Degraded);
        Assert.False(health.AllHealthy);
        Assert.Equal(HealthCondition.Degraded,
            health.Snapshot().Single(static x =>
                x.Component == HealthComponent.Persistence).Condition);
    }

    [Fact]
    public void CrashContextRetainsFixedMinimalSnapshotsAndNoException()
    {
        var metrics = new FixedMetricRecorder(10);
        var health = new FixedHealthRegistry();
        var trace = new DiagnosticTraceBuffer(8);
        var slow = new SlowOperationDetector(8, 10);
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            trace.Record(DiagnosticArea.Queue, DiagnosticTraceStage.Faulted, id);
            slow.Observe(DiagnosticArea.Queue, id, 11);
            metrics.Record(DiagnosticArea.Queue, DiagnosticOutcome.Failed, 11);
        }

        var crash = new CrashContextCapture(metrics, health, trace, slow, 3)
            .Capture(CrashCause.UnexpectedException, ids[^1]);
        Assert.Equal(ids[^3..], crash.Traces.Select(static x => x.Correlation));
        Assert.Equal(3, crash.SlowOperations.Count);
        Assert.Equal(20, crash.Metrics.Count);
        Assert.Equal(5, crash.Health.Count);
        Assert.Equal(CrashCause.UnexpectedException, crash.Cause);
        Assert.Equal(ids[^1], crash.Correlation);
    }

    [Fact]
    public void BudgetValidatesMemoryLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticBudget(0, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticBudget(4097, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticBudget(1, 1025, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticBudget(1, 1, 129, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticBudget(1, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticTraceBuffer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlowOperationDetector(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedMetricRecorder(0));
    }

    [Fact]
    public void InvalidMetricEnumsOrNegativeDurationsCannotPolluteTheRegistry()
    {
        var meter = new FixedMetricRecorder(1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            meter.Record((DiagnosticArea)100, DiagnosticOutcome.Failed, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            meter.Record(DiagnosticArea.Queue, (DiagnosticOutcome)100, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            meter.Record(DiagnosticArea.Queue, DiagnosticOutcome.Failed, -1));
        Assert.Equal(0, meter.Snapshot().Sum(static x => x.Count));
        var trace = new DiagnosticTraceBuffer(2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            trace.Record((DiagnosticArea)99, DiagnosticTraceStage.Started, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() =>
            trace.Record(DiagnosticArea.Queue, DiagnosticTraceStage.Started, Guid.Empty));
        Assert.Empty(trace.Snapshot());
        var health = new FixedHealthRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            health.Report((HealthComponent)100, HealthCondition.Healthy));
    }

    [Fact]
    public async Task ExporterIsolationDropsTelemetryWithoutAffectingBusiness()
    {
        var outbox = new BoundedObservationOutbox(2);
        Assert.True(outbox.TryOffer(new DiagnosticObservation(
            DiagnosticArea.Queue, DiagnosticOutcome.Succeeded, 3)));
        Assert.True(outbox.TryOffer(new DiagnosticObservation(
            DiagnosticArea.Queue, DiagnosticOutcome.Succeeded, 4)));
        Assert.False(outbox.TryOffer(new DiagnosticObservation(
            DiagnosticArea.Queue, DiagnosticOutcome.Failed, 5)));
        Assert.Equal(1L, outbox.Dropped);
        Assert.Equal(2, outbox.Pending);
        Assert.Equal(2, await outbox.FlushAsync(new ThrowingSink(), 4));
        Assert.Equal(2L, outbox.FailedExports);
        Assert.Equal(0, outbox.Pending);
    }

    [Fact]
    public async Task AsyncObserverNeverChangesSuccessFailureOrCancellation()
    {
        var metrics = new FixedMetricRecorder(1);
        var trace = new DiagnosticTraceBuffer(10);
        var slow = new SlowOperationDetector(10, 1);
        var outbox = new BoundedObservationOutbox(1);
        var successful = false;
        await SafeOperationTelemetry.RunAsync(_ =>
        {
            successful = true;
            return ValueTask.CompletedTask;
        }, DiagnosticArea.Queue, Guid.NewGuid(), metrics, slow, trace, outbox);
        Assert.True(successful);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SafeOperationTelemetry.RunAsync(
                _ => throw new InvalidOperationException("sensitive-error-sentinel"),
                DiagnosticArea.Database, Guid.NewGuid(), metrics, slow, trace, outbox));
        Assert.Equal("sensitive-error-sentinel", error.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await SafeOperationTelemetry.RunAsync(
                token => throw new OperationCanceledException(token),
                DiagnosticArea.Script, Guid.NewGuid(), metrics, slow, trace,
                outbox, cancellation.Token));
        Assert.Equal(1L, metrics.Snapshot().Single(static x =>
            x.Area == DiagnosticArea.Queue &&
            x.Outcome == DiagnosticOutcome.Succeeded).Count);
        Assert.Equal(1L, metrics.Snapshot().Single(static x =>
            x.Area == DiagnosticArea.Database &&
            x.Outcome == DiagnosticOutcome.Failed).Count);
        Assert.Equal(1L, metrics.Snapshot().Single(static x =>
            x.Area == DiagnosticArea.Script &&
            x.Outcome == DiagnosticOutcome.Cancelled).Count);
        Assert.Equal(2L, outbox.Dropped);
        Assert.Equal(6, trace.Snapshot().Count);
    }

    [Fact]
    public void ConcurrentTraceAndSlowRecordingNeverExceedBudget()
    {
        var trace = new DiagnosticTraceBuffer(12);
        var slow = new SlowOperationDetector(7, 1);
        Parallel.For(0, 5000, _ =>
        {
            var id = Guid.NewGuid();
            trace.Record(DiagnosticArea.Queue, DiagnosticTraceStage.Faulted, id);
            slow.Observe(DiagnosticArea.Queue, id, 2);
        });

        Assert.Equal(12, trace.Snapshot().Count);
        Assert.Equal(7, slow.Snapshot().Count);
        Assert.Equal(5000, slow.TotalSlowOperations);
    }

    private sealed class ThrowingSink : IDiagnosticObservationSink
    {
        public ValueTask WriteAsync(
            DiagnosticObservation observation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret-exporter-error");
    }
}
