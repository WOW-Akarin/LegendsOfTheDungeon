using LegendsOfTheDungeon.Platform.Concurrency;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class ConcurrencyTests
{
    [Fact]
    public void MailboxPreservesCriticalCapacityWhenNormalLaneIsFull()
    {
        var mailbox = new BoundedMailbox<string>(normalCapacity: 2, criticalCapacity: 1, maximumCriticalBurst: 2);
        Assert.Equal(MailboxOfferResult.Accepted, mailbox.Offer("n1", MailboxLane.Normal));
        Assert.Equal(MailboxOfferResult.Accepted, mailbox.Offer("n2", MailboxLane.Normal));
        Assert.Equal(MailboxOfferResult.Busy, mailbox.Offer("n3", MailboxLane.Normal));
        Assert.Equal(MailboxOfferResult.Accepted, mailbox.Offer("receipt", MailboxLane.Critical));
        Assert.Equal(MailboxOfferResult.Busy, mailbox.Offer("receipt2", MailboxLane.Critical));
        Assert.Equal(3, mailbox.Count);

        mailbox.Complete();
        Assert.Equal(MailboxOfferResult.Closed, mailbox.Offer("late", MailboxLane.Critical));
        var delivered = mailbox.Drain(10);
        Assert.Equal(["receipt", "n1", "n2"], delivered.Select(static item => item.Value));
        Assert.Equal(MailboxLane.Critical, delivered[0].Lane);
        Assert.Equal(0, mailbox.Count);
    }

    [Fact]
    public void MailboxEnforcesFairnessAcrossSeparateDrainCalls()
    {
        var mailbox = new BoundedMailbox<int>(normalCapacity: 2, criticalCapacity: 8, maximumCriticalBurst: 2);
        mailbox.Offer(1, MailboxLane.Normal);
        mailbox.Offer(2, MailboxLane.Normal);
        for (var i = 10; i < 16; i++)
        {
            mailbox.Offer(i, MailboxLane.Critical);
        }

        Assert.Equal([10, 11], mailbox.Drain(2).Select(static item => item.Value));
        Assert.Equal([1, 12], mailbox.Drain(2).Select(static item => item.Value));
        Assert.Equal([13, 2], mailbox.Drain(2).Select(static item => item.Value));
        Assert.Equal([14, 15], mailbox.Drain(8).Select(static item => item.Value));
    }

    [Fact]
    public void MailboxConcurrentWritersDoNotExceedEitherLane()
    {
        var mailbox = new BoundedMailbox<int>(normalCapacity: 17, criticalCapacity: 3, maximumCriticalBurst: 2);
        var offers = new MailboxOfferResult[300];
        Parallel.For(0, offers.Length, i =>
        {
            offers[i] = mailbox.Offer(i, i % 5 == 0 ? MailboxLane.Critical : MailboxLane.Normal);
        });

        Assert.Equal(20, offers.Count(static value => value == MailboxOfferResult.Accepted));
        Assert.Equal(280, offers.Count(static value => value == MailboxOfferResult.Busy));
        Assert.Equal(20, mailbox.Count);
        Assert.Equal(3, mailbox.Drain(20).Count(static item => item.Lane == MailboxLane.Critical));
    }

    [Fact]
    public void SchedulerIsBoundedStableCancelableAndLogical()
    {
        var scheduler = new LogicalScheduler<string>(3);
        var a = scheduler.Schedule(7, "a");
        var b = scheduler.Schedule(2, "b");
        var c = scheduler.Schedule(7, "c");
        Assert.Equal(ScheduleOfferStatus.Accepted, a.Status);
        Assert.Equal(ScheduleOfferStatus.Accepted, b.Status);
        Assert.Equal(ScheduleOfferStatus.Accepted, c.Status);
        Assert.Equal(ScheduleOfferStatus.Busy, scheduler.Schedule(0, "d").Status);
        Assert.True(scheduler.Cancel(a.Id));
        Assert.False(scheduler.Cancel(a.Id));

        var added = scheduler.Schedule(7, "d");
        Assert.Equal(ScheduleOfferStatus.Accepted, added.Status);
        Assert.Empty(scheduler.DrainDue(1, 3));
        Assert.Equal(["b"], scheduler.DrainDue(2, 1).Select(static item => item.Payload));
        Assert.Equal(["c"], scheduler.DrainDue(7, 1).Select(static item => item.Payload));
        Assert.Equal(["d"], scheduler.DrainDue(7, 3).Select(static item => item.Payload));

        scheduler.Complete();
        Assert.Equal(ScheduleOfferStatus.Closed, scheduler.Schedule(8, "late").Status);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void SchedulerConcurrentWritersNeverExceedCapacity()
    {
        var scheduler = new LogicalScheduler<int>(25);
        var results = new ScheduleOfferStatus[300];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = scheduler.Schedule(i % 5, i).Status;
        });

        Assert.Equal(25, results.Count(static item => item == ScheduleOfferStatus.Accepted));
        Assert.Equal(25, scheduler.Count);
        var drained = scheduler.DrainDue(4, 25);
        Assert.Equal(25, drained.Count);
        Assert.Equal(25, drained.Select(static item => item.Id).Distinct().Count());
        Assert.True(drained.Zip(drained.Skip(1), static (left, right) =>
            left.DueTick < right.DueTick ||
            left.DueTick == right.DueTick && left.Id < right.Id).All(static valid => valid));
    }

    [Fact]
    public void WorkQueueBackpressureDuplicateAndClosingAreExplicit()
    {
        var queue = new BoundedWorkChannel(2);
        var first = new BackgroundWorkItem(1, static _ => ValueTask.CompletedTask);
        Assert.Equal(WorkOfferStatus.Accepted, queue.Offer(first));
        Assert.Equal(WorkOfferStatus.Duplicate, queue.Offer(first));
        Assert.Equal(WorkOfferStatus.Accepted, queue.Offer(
            new BackgroundWorkItem(2, static _ => ValueTask.CompletedTask)));
        Assert.Equal(WorkOfferStatus.Busy, queue.Offer(
            new BackgroundWorkItem(3, static _ => ValueTask.CompletedTask)));
        Assert.Equal([1L, 2L], queue.SnapshotOutstanding().Select(static x => x.Id));

        queue.Complete();
        Assert.Equal(WorkOfferStatus.Closed, queue.Offer(
            new BackgroundWorkItem(4, static _ => ValueTask.CompletedTask)));
    }

    [Fact]
    public async Task WorkerExceptionIsIsolatedAndLaterWorkStillRuns()
    {
        var queue = new BoundedWorkChannel(4);
        var completed = new List<int>();
        await using var pool = new BackgroundWorkerPool(queue, workerCount: 1);
        queue.Offer(new BackgroundWorkItem(1, _ => throw new InvalidOperationException("secret-error-message")));
        queue.Offer(new BackgroundWorkItem(2, _ =>
        {
            completed.Add(2);
            return ValueTask.CompletedTask;
        }));
        queue.Offer(new BackgroundWorkItem(3, _ =>
        {
            completed.Add(3);
            return ValueTask.CompletedTask;
        }));

        var report = await pool.ShutdownAsync(TimeSpan.FromSeconds(5));
        Assert.True(report.Drained);
        Assert.Empty(report.Outstanding);
        Assert.Equal([2, 3], completed);
        Assert.Equal(2L, report.Succeeded);
        Assert.Equal(1L, report.Failed);
        Assert.Equal(0L, report.Cancelled);
        var failure = Assert.Single(report.RecentFailures);
        Assert.Equal(1, failure.Id);
        Assert.DoesNotContain("secret", failure.Code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PerItemCancellationDoesNotExecuteOrLeakTask()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var queue = new BoundedWorkChannel(2);
        var invoked = 0;
        queue.Offer(new BackgroundWorkItem(1, _ =>
        {
            Interlocked.Increment(ref invoked);
            return ValueTask.CompletedTask;
        }, cancelled.Token));
        await using var pool = new BackgroundWorkerPool(queue, 1);
        var report = await pool.ShutdownAsync(TimeSpan.FromSeconds(5));

        Assert.True(report.Drained);
        Assert.Equal(0, invoked);
        Assert.Equal(1L, report.Cancelled);
        Assert.Empty(report.Outstanding);
    }

    [Fact]
    public async Task TimedShutdownReportsRunningAndQueuedTasks()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new BoundedWorkChannel(1);
        await using var pool = new BackgroundWorkerPool(queue, 1);
        Assert.Equal(WorkOfferStatus.Accepted, queue.Offer(new BackgroundWorkItem(21, async _ =>
        {
            started.TrySetResult(true);
            await gate.Task;
        })));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkOfferStatus.Accepted, queue.Offer(new BackgroundWorkItem(22,
            static _ => ValueTask.CompletedTask)));

        var timedOut = await pool.ShutdownAsync(TimeSpan.FromMilliseconds(60));
        Assert.False(timedOut.Drained);
        Assert.Equal([21L, 22L], timedOut.Outstanding.Select(static x => x.Id));
        Assert.Equal(OutstandingWorkState.Running, timedOut.Outstanding[0].State);
        Assert.Equal(OutstandingWorkState.Queued, timedOut.Outstanding[1].State);
        Assert.Equal(WorkOfferStatus.Closed, queue.Offer(
            new BackgroundWorkItem(23, static _ => ValueTask.CompletedTask)));

        gate.SetResult(true);
        var completed = await pool.ShutdownAsync(TimeSpan.FromSeconds(5));
        // 超时取消 Worker 后，不再自行执行尚未领取的工作；必须由调用方恢复。
        Assert.False(completed.Drained);
        Assert.Equal([22L], completed.Outstanding.Select(static x => x.Id));
        await pool.DisposeAsync();
        var abandoned = Assert.Single(queue.SnapshotOutstanding());
        Assert.Equal(22L, abandoned.Id);
        Assert.Equal(OutstandingWorkState.Abandoned, abandoned.State);
    }

    [Fact]
    public async Task DisposeNeverWaitsForUncooperativeHandlerAndReleasesQueuedCallbacks()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new BoundedWorkChannel(1);
        var pool = new BackgroundWorkerPool(queue, 1);
        queue.Offer(new BackgroundWorkItem(31, async _ =>
        {
            started.TrySetResult(true);
            await release.Task; // 明确模拟不配合取消的任务
        }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Offer(new BackgroundWorkItem(32, static _ => ValueTask.CompletedTask));

        var stopped = await pool.ShutdownAsync(TimeSpan.FromMilliseconds(50));
        Assert.False(stopped.Drained);
        Assert.Equal([31L, 32L], stopped.Outstanding.Select(static x => x.Id));

        try
        {
            await pool.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var pending = queue.SnapshotOutstanding();
            Assert.Contains(pending, static x => x.Id == 31 &&
                x.State == OutstandingWorkState.Running);
            Assert.Contains(pending, static x => x.Id == 32 &&
                x.State == OutstandingWorkState.Abandoned);
        }
        finally
        {
            release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task LogicalSchedulerAndReservedMailboxFeedBoundedWorkersEndToEnd()
    {
        var scheduler = new LogicalScheduler<string>(4);
        scheduler.Schedule(5, "normal-a");
        scheduler.Schedule(5, "normal-b");
        scheduler.Schedule(7, "future");
        var mailbox = new BoundedMailbox<string>(
            normalCapacity: 2, criticalCapacity: 1, maximumCriticalBurst: 1);

        foreach (var delivery in scheduler.DrainDue(5, 2))
        {
            Assert.Equal(MailboxOfferResult.Accepted,
                mailbox.Offer(delivery.Payload, MailboxLane.Normal));
        }

        // 普通车道已满，数据库回执仍应占用独立预留容量。
        Assert.Equal(MailboxOfferResult.Busy, mailbox.Offer("overflow", MailboxLane.Normal));
        Assert.Equal(MailboxOfferResult.Accepted, mailbox.Offer("db-receipt", MailboxLane.Critical));
        Assert.Equal(MailboxOfferResult.Busy, mailbox.Offer("critical-overflow", MailboxLane.Critical));
        Assert.Empty(scheduler.DrainDue(6, 3));

        var queue = new BoundedWorkChannel(3);
        var observed = new List<string>();
        await using var workers = new BackgroundWorkerPool(queue, 1);
        long nextId = 1;
        foreach (var item in mailbox.Drain(3))
        {
            var payload = item.Value;
            Assert.Equal(WorkOfferStatus.Accepted, queue.Offer(new BackgroundWorkItem(nextId++, _ =>
            {
                observed.Add(payload);
                return ValueTask.CompletedTask;
            })));
        }

        mailbox.Complete();
        Assert.Equal(MailboxOfferResult.Closed, mailbox.Offer("late", MailboxLane.Normal));

        var report = await workers.ShutdownAsync(TimeSpan.FromSeconds(5));
        Assert.True(report.Drained);
        Assert.Empty(report.Outstanding);
        Assert.Equal(["db-receipt", "normal-a", "normal-b"], observed);

        var future = Assert.Single(scheduler.DrainDue(7, 1));
        Assert.Equal("future", future.Payload);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task FailureSummariesAreBoundedAndNeverContainExceptions()
    {
        var queue = new BoundedWorkChannel(8);
        await using var pool = new BackgroundWorkerPool(queue, 1, maximumFailureRecords: 2);
        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal(WorkOfferStatus.Accepted,
                queue.Offer(new BackgroundWorkItem(i,
                    _ => throw new InvalidOperationException("private-credential-sentinel"))));
        }

        var report = await pool.ShutdownAsync(TimeSpan.FromSeconds(5));
        Assert.True(report.Drained);
        Assert.Equal(5L, report.Failed);
        Assert.Equal([4L, 5L], report.RecentFailures.Select(static x => x.Id));
        Assert.All(report.RecentFailures,
            static item => Assert.DoesNotContain("private", item.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidConcurrencyBudgetsFailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedMailbox<int>(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedMailbox<int>(1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedMailbox<int>(1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalScheduler<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedWorkChannel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackgroundWorkItem(0,
            static _ => ValueTask.CompletedTask));
        var mailbox = new BoundedMailbox<int>(1, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => mailbox.Offer(1, (MailboxLane)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => mailbox.Drain(0));
        var scheduler = new LogicalScheduler<int>(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.DrainDue(0, 0));
    }

    [Fact]
    public async Task ShutdownDrainsAcceptedTasksAndCapsWorkerParallelism()
    {
        var queue = new BoundedWorkChannel(15);
        await using var pool = new BackgroundWorkerPool(queue, 2);
        var active = 0;
        var maximum = 0;
        var completed = 0;
        for (var i = 1; i <= 12; i++)
        {
            var item = new BackgroundWorkItem(i, async token =>
            {
                var current = Interlocked.Increment(ref active);
                int observed;
                do
                {
                    observed = Volatile.Read(ref maximum);
                    if (observed >= current)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref maximum, current, observed) != observed);

                try
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref completed);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
            Assert.Equal(WorkOfferStatus.Accepted, queue.Offer(item));
        }

        var report = await pool.ShutdownAsync(TimeSpan.FromSeconds(5));
        Assert.True(report.Drained);
        Assert.Empty(report.Outstanding);
        Assert.Equal(12, completed);
        Assert.InRange(maximum, 1, 2);
    }
}
