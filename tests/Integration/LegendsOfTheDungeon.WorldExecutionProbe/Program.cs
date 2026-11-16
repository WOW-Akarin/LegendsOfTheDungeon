using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.Concurrency;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Security.Contracts;

await CheckAsync("real-background-worker-critical-reply-and-write-guard", WorkerReturnAsync);
await CheckAsync("parallel-worlds-without-same-world-reentrancy", ParallelWorldsAsync);
await CheckAsync("stale-epoch-generation-and-replay-rejected", LifecycleRecoveryAsync);
await CheckAsync("external-production-identity-default-denied", ExternalSecurityAsync);
await CheckAsync("faulted-world-preserves-remaining-accepted-messages", FaultIsolationAsync);
Console.WriteLine("P18_WORLD_EXECUTION_INTEGRATION_PASS");

static async Task CheckAsync(string name, Func<Task> probe)
{
    await probe().WaitAsync(TimeSpan.FromSeconds(20));
    Console.WriteLine("PASS " + name);
}

static void Check(bool condition, string explanation)
{
    if (!condition)
    {
        throw new InvalidOperationException(explanation);
    }
}

static WorldExecutor Create(ulong id, int normal = 128, int critical = 128) =>
    new(new WorldId(id), new WorldEpoch(1), new DenyByDefaultCommandAuthorizer(),
        normal, critical, maximumCriticalBurst: 4, maximumTickets: 128);

static async Task WorkerReturnAsync()
{
    var world = Create(10);
    var trusted = world.CreateTrustedProducer();
    var tokens = new List<IWorldReplyTicket>();
    IWorldWriteContext? staleContext = null;
    var values = new int[64];
    Check(trusted.Offer(new ProbeAction(ctx =>
    {
        staleContext = ctx;
        for (var i = 0; i < 64; i++)
        {
            var id = new EntityId((ulong)i + 1);
            ctx.BindEntity(id, new EntityGeneration(1));
            tokens.Add(ctx.IssueReplyTicket(id, new EntityGeneration(1)));
        }
    })) == WorldAdmissionStatus.Accepted, "initial entity registration not enqueued");
    Check(world.RunBatch(1).Executed == 1, "initial entity registration not executed");

    var normal = 0;
    for (var i = 0; i < 20; i++)
    {
        Check(trusted.Offer(new ProbeAction(_ =>
            Interlocked.Increment(ref normal))) == WorldAdmissionStatus.Accepted,
            "normal work cannot be accepted");
    }

    var offWorldDenied = 0;
    var queue = new BoundedWorkChannel(96);
    for (var i = 0; i < tokens.Count; i++)
    {
        var index = i;
        var stamp = tokens[i];
        var task = new BackgroundWorkItem(index + 1, cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 即便持有遗留 Context，P12 Worker 仍不具任何有效的写入权。
                staleContext!.Demand(stamp.Entity, stamp.Generation);
                throw new InvalidOperationException("background worker mutated world");
            }
            catch (WorldAccessException ex) when (ex.Fault == WorldAccessFault.LeaseRequired)
            {
                Interlocked.Increment(ref offWorldDenied);
            }

            var result = trusted.OfferReply(stamp, new ProbeAction(ctx =>
            {
                ctx.Demand(stamp.Entity, stamp.Generation);
                values[index]++;
            }));
            Check(result == WorldAdmissionStatus.Accepted,
                "worker result failed to enter reserved critical lane");
            return ValueTask.CompletedTask;
        });
        Check(queue.Offer(task) == WorkOfferStatus.Accepted,
            "bounded P12 WorkerQueue unexpectedly rejected task");
    }

    await using var workers = new BackgroundWorkerPool(queue, workerCount: 4);
    var shutdown = await workers.ShutdownAsync(TimeSpan.FromSeconds(5));
    Check(shutdown.Drained && shutdown.Succeeded == 64 &&
        shutdown.Failed == 0 && shutdown.Outstanding.Count == 0,
        "actual background pool did not successfully return all 64 results");
    Check(offWorldDenied == 64, "background worker unexpectedly gained a world write lease");

    var run = world.RunBatch(100);
    Check(run.Status == WorldRunStatus.Completed &&
        run.Executed == 84 && run.Stale == 0 &&
        normal == 20 && values.Sum() == 64 &&
        values.All(static x => x == 1), "real worker results were lost, duplicated or starved");
    Check(world.RunBatch(10).Remaining == 0, "world mailbox not drained");
}

static async Task ParallelWorldsAsync()
{
    var first = Create(1);
    var second = Create(2);
    using var ready = new CountdownEvent(2);
    using var release = new ManualResetEventSlim();
    var sawBusy = 0;
    first.CreateTrustedProducer().Offer(new ProbeAction(_ =>
    {
        if (first.RunBatch(1).Status == WorldRunStatus.Busy)
        {
            Interlocked.Increment(ref sawBusy);
        }

        ready.Signal();
        release.Wait(TimeSpan.FromSeconds(5));
    }));
    second.CreateTrustedProducer().Offer(new ProbeAction(_ =>
    {
        ready.Signal();
        release.Wait(TimeSpan.FromSeconds(5));
    }));
    var a = Task.Run(() => first.RunBatch(1));
    var b = Task.Run(() => second.RunBatch(1));
    Check(ready.Wait(TimeSpan.FromSeconds(5)),
        "independent worlds failed to run concurrently on distinct workers");
    release.Set();
    Check((await a).Executed == 1 && (await b).Executed == 1 && sawBusy == 1,
        "same world reentered or independent worlds were serialized");
}

static Task LifecycleRecoveryAsync()
{
    var world = Create(3);
    var producer = world.CreateTrustedProducer();
    IWorldReplyTicket? retired = null;
    IWorldReplyTicket? expired = null;
    producer.Offer(new ProbeAction(ctx =>
    {
        var entity = new EntityId(12);
        ctx.BindEntity(entity, new EntityGeneration(1));
        retired = ctx.IssueReplyTicket(entity, new EntityGeneration(1));
        ctx.RetireEntity(entity, new EntityGeneration(1));
        ctx.BindEntity(entity, new EntityGeneration(2));
        expired = ctx.IssueReplyTicket(entity, new EntityGeneration(2));
    }));
    Check(world.RunBatch(1).Executed == 1, "generation lifecycle not executed");
    Check(producer.OfferReply(retired!, new ProbeAction(_ => { })) ==
        WorldAdmissionStatus.Stale, "retired generation accepted late reply");

    Check(producer.OfferReply(expired!, new ProbeAction(_ => { })) ==
        WorldAdmissionStatus.Accepted, "new generation reply should be accepted");
    world.AdvanceEpoch(new WorldEpoch(2));
    var batch = world.RunBatch(2);
    Check(batch.Stale == 1 && batch.Executed == 0,
        "old epoch accepted a previously queued reply");
    Check(producer.OfferReply(expired!, new ProbeAction(_ => { })) ==
        WorldAdmissionStatus.Stale, "old epoch reply ticket remained valid");
    return Task.CompletedTask;
}

static Task ExternalSecurityAsync()
{
    var world = Create(4);
    var action = new ProbeAction(_ =>
        throw new InvalidOperationException("untrusted direct write"));
    Check(world.OfferExternal(null, action) == WorldAdmissionStatus.Denied,
        "anonymous untrusted command reached world");
    Check(world.OfferExternal(BoundIdentity.CreateLocalTest("test"), action) ==
        WorldAdmissionStatus.Denied,
        "local test identity was upgraded to production trust");
    Check(world.RunBatch(1).Executed == 0 && !world.IsFaulted,
        "denied external command executed");
    return Task.CompletedTask;
}

static Task FaultIsolationAsync()
{
    var world = Create(5);
    var producer = world.CreateTrustedProducer();
    var touched = 0;
    producer.Offer(new ProbeAction(_ =>
    {
        touched++;
        throw new InvalidOperationException("secret error text");
    }));
    producer.Offer(new ProbeAction(_ => touched++));
    var run = world.RunBatch(10);
    Check(run.Status == WorldRunStatus.Faulted && world.IsFaulted &&
        touched == 1 && run.Remaining == 1,
        "unexpected exception did not fail close without draining unrelated work");
    Check(world.RunBatch(1).Status == WorldRunStatus.Faulted,
        "faulted world continued processing");
    Check(producer.Offer(new ProbeAction(_ => { })) == WorldAdmissionStatus.Closed,
        "faulted world still accepts commands");
    return Task.CompletedTask;
}

file sealed class ProbeAction(Action<IWorldWriteContext> apply) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => apply(context);
}
