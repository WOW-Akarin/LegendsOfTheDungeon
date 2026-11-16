using LegendsOfTheDungeon.Platform.Concurrency;
using LegendsOfTheDungeon.Platform.Diagnostics;

await CheckAsync("actual-worker-lifecycle-and-slow-task", WorkerDiagnosticsAsync);
await CheckAsync("sink-failure-does-not-change-business", SinkFailureAsync);
await CheckAsync("cancelled-operation-keeps-original-result", CancelledAsync);
await CheckAsync("fixed-cardinality-and-bounded-correlations", CardinalityAsync);
await CheckAsync("health-unknown-is-not-ready", HealthAsync);
await CheckAsync("bounded-crash-context-excludes-user-payloads", CrashContextAsync);
Console.WriteLine("P14_DIAGNOSTICS_INTEGRATION_PASS");

static async Task CheckAsync(string name, Func<Task> probe)
{
    await probe().WaitAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine("PASS " + name);
}

static void Check(bool condition, string reason)
{
    if (!condition)
    {
        throw new InvalidOperationException(reason);
    }
}

static readonlyProbeState Shared = new();

static async Task WorkerDiagnosticsAsync()
{
    var metrics = Shared.Metrics;
    var trace = Shared.Trace;
    var slow = Shared.Slow;
    var outbox = Shared.Outbox;
    var queue = new BoundedWorkChannel(256);
    var executed = 0;
    var failed = 0;
    for (var i = 1; i <= 200; i++)
    {
        var id = i;
        var correlation = Guid.NewGuid();
        var work = new BackgroundWorkItem(id, token =>
            SafeOperationTelemetry.RunAsync(async inner =>
            {
                if (id == 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(70), inner);
                }

                if (id == 2)
                {
                    Interlocked.Increment(ref failed);
                    throw new InvalidOperationException("private-exception-sentinel");
                }

                Interlocked.Increment(ref executed);
            }, DiagnosticArea.Queue, correlation, metrics, slow, trace, outbox, token));
        Check(queue.Offer(work) == WorkOfferStatus.Accepted, "unexpected bounded enqueue rejection");
    }

    await using var workers = new BackgroundWorkerPool(queue, workerCount: 2);
    var report = await workers.ShutdownAsync(TimeSpan.FromSeconds(5));
    Check(report.Drained && report.Outstanding.Count == 0,
        "instrumented worker pool failed to drain");
    Check(report.Succeeded == 199 && report.Failed == 1,
        "telemetry changed isolated worker failures");
    Check(executed == 199 && failed == 1, "actual work was changed by telemetry");
    var cells = metrics.Snapshot();
    Check(cells.Count == FixedMetricRecorder.MaximumSeries,
        "fixed metrics changed label cardinality");
    Check(cells.Single(x => x.Area == DiagnosticArea.Queue
        && x.Outcome == DiagnosticOutcome.Succeeded).Count == 199,
        "successful tasks were not counted");
    Check(cells.Single(x => x.Area == DiagnosticArea.Queue
        && x.Outcome == DiagnosticOutcome.Failed).Count == 1,
        "failed task was not counted");
    Check(slow.TotalSlowOperations >= 1, "real 70-ms task was not detected");
    Check(slow.Snapshot().Any(x => x.Area == DiagnosticArea.Queue
        && x.ElapsedMilliseconds >= 10),
        "slow queue task was not locatable");
    Check(trace.Snapshot().Count <= 16 && outbox.Pending <= 2 && outbox.Dropped > 0,
        "observability caches exceeded configured limits");
    Shared.ActualBusinessSuccess = executed;
}

static async Task SinkFailureAsync()
{
    var attempted = await Shared.Outbox.FlushAsync(new FailingExporter(), 4);
    Check(attempted == 2, "expected two queued observations");
    Check(Shared.Outbox.Pending == 0 && Shared.Outbox.FailedExports == 2,
        "exporter failures were not bounded/recorded");
    Check(Shared.ActualBusinessSuccess == 199,
        "exporter exception mutated prior completed work");
}

static async Task CancelledAsync()
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    var observed = false;
    try
    {
        await SafeOperationTelemetry.RunAsync(
            token =>
            {
                observed = true;
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }, DiagnosticArea.Script, Guid.NewGuid(),
            Shared.Metrics, Shared.Slow, Shared.Trace, Shared.Outbox,
            cancelled.Token);
        throw new InvalidOperationException("cancelled task incorrectly succeeded");
    }
    catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
    {
        Check(observed, "actual cancellation pathway did not run");
    }

    Check(Shared.Metrics.Snapshot().Single(x =>
        x.Area == DiagnosticArea.Script && x.Outcome == DiagnosticOutcome.Cancelled).Count == 1,
        "cancelled status must be observed without replacing original exception");
}

static Task CardinalityAsync()
{
    for (var i = 0; i < 50_000; i++)
    {
        Shared.Trace.Record(DiagnosticArea.Tick, DiagnosticTraceStage.Accepted, Guid.NewGuid());
        Shared.Metrics.Record(DiagnosticArea.Tick, DiagnosticOutcome.Succeeded, i % 10);
        Shared.Slow.Observe(DiagnosticArea.Tick, Guid.NewGuid(), 20);
    }

    Check(Shared.Metrics.Snapshot().Count == 20, "metric labels grew with input count");
    Check(Shared.Trace.Snapshot().Count == 16, "trace ring grew unbounded");
    Check(Shared.Slow.Snapshot().Count == 4, "slow event ring grew unbounded");
    Check(Shared.Slow.TotalSlowOperations >= 50_000,
        "cumulative overflow-safe slow counter did not advance");
    return Task.CompletedTask;
}

static Task HealthAsync()
{
    var health = Shared.Health;
    Check(!health.AllHealthy, "Unknown health must not mean Ready");
    foreach (var component in Enum.GetValues<HealthComponent>())
    {
        health.Report(component, HealthCondition.Healthy);
    }

    Check(health.AllHealthy, "complete synthetic healthy report not reflected");
    health.Report(HealthComponent.Persistence, HealthCondition.Unavailable);
    Check(!health.AllHealthy, "Unavailable dependency still appeared Ready");
    return Task.CompletedTask;
}

static Task CrashContextAsync()
{
    var correlation = Guid.NewGuid();
    var crash = new CrashContextCapture(
        Shared.Metrics, Shared.Health, Shared.Trace, Shared.Slow, maximumEntries: 3)
        .Capture(CrashCause.DependencyUnavailable, correlation);
    Check(crash.Cause == CrashCause.DependencyUnavailable
        && crash.Correlation == correlation, "crash context cause/correlation missing");
    Check(crash.Traces.Count == 3 && crash.SlowOperations.Count == 3
        && crash.Metrics.Count == 20 && crash.Health.Count == 5,
        "crash context is not bounded");
    Check(!crash.Health.All(x => x.Condition == HealthCondition.Healthy),
        "snapshot masked unhealthy dependency");
    // 不允许 CrashContext 包含原始异常信息：仅受限 enums、guid 和数值类型。
    Check(typeof(CrashContextSnapshot).GetProperties()
        .All(p => p.PropertyType != typeof(Exception)
            && p.PropertyType != typeof(string)),
        "raw secret-bearing string/exception property exposed");
    return Task.CompletedTask;
}

file sealed class readonlyProbeState
{
    internal readonly FixedMetricRecorder Metrics = new(10);
    internal readonly DiagnosticTraceBuffer Trace = new(16);
    internal readonly SlowOperationDetector Slow = new(4, 10);
    internal readonly BoundedObservationOutbox Outbox = new(2);
    internal readonly FixedHealthRegistry Health = new();
    internal int ActualBusinessSuccess;
}

file sealed class FailingExporter : IDiagnosticObservationSink
{
    public ValueTask WriteAsync(
        DiagnosticObservation observation, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("private-exporter-exception-sentinel");
}
