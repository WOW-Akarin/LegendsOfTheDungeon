using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

await CheckAsync("parallel-real-world-isolation", ParallelWorldsAsync);
await CheckAsync("worker-reply-after-despawn-rejected", StaleWorkerReplyAsync);
await CheckAsync("world-shutdown-cleanup-release-and-epoch-restart", ShutdownAndRestartAsync);
await CheckAsync("faulted-cleanup-retains-owner", FaultedCleanupAsync);
Console.WriteLine("P17_WORLD_LIFECYCLE_INTEGRATION_PASS");

static async Task CheckAsync(string name, Func<Task> action)
{
    await action().WaitAsync(TimeSpan.FromSeconds(20));
    Console.WriteLine("PASS " + name);
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static WorldExecutor Executor(ulong world, ulong epoch = 1) =>
    new(new WorldId(world), new WorldEpoch(epoch),
        new DenyByDefaultCommandAuthorizer(), 64, 32, 4);

static EntityIdentity Id(ulong value, ulong world = 1, ulong epoch = 1, ulong gen = 1) =>
    new(new WorldId(world), new WorldEpoch(epoch),
        new EntityId(value), new EntityGeneration(gen));

static WorldEntitySeed Seed(EntityIdentity id) =>
    new(id, new Transform(new Position(0, 0, 0), new Angle(0)));

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Check(executor.CreateTrustedProducer().Offer(new Act(action)) ==
        WorldAdmissionStatus.Accepted, "world action rejected before enqueue");
    Check(executor.RunBatch(1).Status == WorldRunStatus.Completed, "world action failed");
}

static void Start(WorldExecutor ex, WorldInstance world) =>
    Run(ex, ctx => Check(world.BeginLoading(ctx) && world.Start(ctx),
        "world lifecycle did not start"));

static async Task ParallelWorldsAsync()
{
    var ownership = new WorldOwnershipRegistry();
    var a = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
    var b = new WorldInstance(new WorldId(2), new WorldEpoch(1), ownership);
    var ea = Executor(1);
    var eb = Executor(2);
    Start(ea, a);
    Start(eb, b);

    using var both = new CountdownEvent(2);
    using var release = new ManualResetEventSlim();
    var first = Id(10);
    var competing = Id(10, world: 2);
    var resultA = WorldEntityResult.InvalidState;
    var resultB = WorldEntityResult.InvalidState;
    ea.CreateTrustedProducer().Offer(new Act(ctx =>
    {
        both.Signal();
        Check(release.Wait(TimeSpan.FromSeconds(5)), "parallel release timed out");
        resultA = a.Spawn(ctx, Seed(first));
    }));
    eb.CreateTrustedProducer().Offer(new Act(ctx =>
    {
        both.Signal();
        Check(release.Wait(TimeSpan.FromSeconds(5)), "parallel release timed out");
        resultB = b.Spawn(ctx, Seed(competing));
    }));
    var firstRun = Task.Run(() => ea.RunBatch(1));
    var secondRun = Task.Run(() => eb.RunBatch(1));
    Check(both.Wait(TimeSpan.FromSeconds(5)), "two independent worlds were serialized");
    release.Set();
    Check((await firstRun).Executed == 1 && (await secondRun).Executed == 1,
        "parallel world processing failed");
    Check(ownership.ClaimedEntities == 1, "two active worlds claimed the same EntityId");
    Check((resultA == WorldEntityResult.Spawned && resultB == WorldEntityResult.WorldConflict) ||
        (resultA == WorldEntityResult.WorldConflict && resultB == WorldEntityResult.Spawned),
        "concurrent claim did not produce exactly one winner and one rejected world");
    Check((a.LiveCount == 1 && b.LiveCount == 0) ||
        (a.LiveCount == 0 && b.LiveCount == 1), "entity duplicated in two worlds");

    // 独立 ID 仍能分别存在于两个世界。
    Run(ea, ctx => Check(a.Spawn(ctx, Seed(Id(11))) == WorldEntityResult.Spawned,
        "world A independent entity rejected"));
    Run(eb, ctx => Check(b.Spawn(ctx, Seed(Id(12, world: 2))) == WorldEntityResult.Spawned,
        "world B independent entity rejected"));
    Check(ownership.ClaimedEntities == 3, "global owner count inconsistent");
}

static Task StaleWorkerReplyAsync()
{
    var owner = new WorldOwnershipRegistry();
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), owner);
    var executor = Executor(1);
    Start(executor, world);
    var id = Id(7);
    var controller = Id(8);
    IWorldReplyTicket? staleTicket = null;
    IWorldReplyTicket? retirementTicket = null;
    Run(executor, ctx =>
    {
        Check(world.Spawn(ctx, Seed(id)) == WorldEntityResult.Spawned, "spawn failed");
        Check(world.Spawn(ctx, Seed(controller)) == WorldEntityResult.Spawned,
            "controller spawn failed");
        staleTicket = ctx.IssueReplyTicket(id.Id, id.Generation);
        retirementTicket = ctx.IssueReplyTicket(controller.Id, controller.Generation);
    });

    Check(executor.CreateTrustedProducer().OfferReply(retirementTicket!,
        new Act(ctx => Check(world.Despawn(ctx, id) ==
            WorldEntityResult.Despawned, "retirement before queued worker reply failed"))) ==
        WorldAdmissionStatus.Accepted, "retirement reply failed admission");
    var acceptedFromWorker = Task.Run(() =>
        executor.CreateTrustedProducer().OfferReply(staleTicket!, new Act(ctx =>
        {
            _ = world.Relocate(ctx, id,
                new Transform(new Position(99, 0, 0), new Angle(0)));
            throw new InvalidOperationException("stale worker reply ran");
        }))).GetAwaiter().GetResult();
    Check(acceptedFromWorker == WorldAdmissionStatus.Accepted,
        "worker stale reply should be initially accepted");
    var report = executor.RunBatch(2);
    Check(report.Executed == 1 && report.Stale == 1 &&
        report.Status == WorldRunStatus.Completed && !executor.IsFaulted,
        "queued stale worker result was executed or world faulted");
    Check(world.TryGetSnapshot(id.Id, out var old) &&
        old.Lifecycle == WorldEntityLifecycle.Disposed &&
        old.Transform.Position == new Position(0, 0, 0),
        "stale worker result mutated disposed entity");
    Check(executor.CreateTrustedProducer().OfferReply(staleTicket!,
        new Act(_ => throw new InvalidOperationException("replayed stale reply"))) ==
        WorldAdmissionStatus.Stale, "stale ticket was replayable");
    Run(executor, ctx => Check(world.Transfer(ctx, controller, new WorldId(2)) ==
        WorldEntityResult.TransferNotSupported, "unimplemented transfer was permitted"));
    return Task.CompletedTask;
}

static Task ShutdownAndRestartAsync()
{
    var owner = new WorldOwnershipRegistry();
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), owner);
    var ex = Executor(1);
    Start(ex, world);
    var source = Id(21);
    var target = Id(22);
    var cleanupOrder = new List<ulong>();
    Run(ex, ctx =>
    {
        Check(world.Spawn(ctx, Seed(source)) == WorldEntityResult.Spawned, "source spawn");
        Check(world.Spawn(ctx, Seed(target)) == WorldEntityResult.Spawned, "target spawn");
        Check(world.SelectTarget(ctx, source, target) ==
            WorldEntityResult.TargetChanged, "target validation failed");
        Check(world.RegisterCleanup(ctx, target, new Hook(id =>
        {
            cleanupOrder.Add(id.Id.Value);
        })) == WorldEntityResult.CleanupRegistered, "cleanup hook rejected");
        Check(world.BeginDrain(ctx), "drain failed");
        Check(world.Stop(ctx), "stop failed");
    });
    Check(owner.ClaimedEntities == 0 && owner.RegisteredWorlds == 0 &&
        world.LiveCount == 0 && cleanupOrder.SequenceEqual([22UL]),
        "shutdown leaked claims or cleanup");
    Check(world.TryGetSnapshot(source.Id, out var snapshot) &&
        snapshot.Lifecycle == WorldEntityLifecycle.Disposed &&
        snapshot.Target is null, "shutdown left a stale target");
    var restarted = new WorldInstance(new WorldId(1), new WorldEpoch(2), owner);
    var restartedExecutor = Executor(1, epoch: 2);
    Start(restartedExecutor, restarted);
    Run(restartedExecutor, ctx => Check(
        restarted.Spawn(ctx, Seed(Id(21, epoch: 2, gen: 2))) ==
        WorldEntityResult.Spawned, "new epoch failed to reclaim the ID"));
    Check(owner.ClaimedEntities == 1, "restarted world lost ownership");
    return Task.CompletedTask;
}

static Task FaultedCleanupAsync()
{
    var owner = new WorldOwnershipRegistry();
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), owner);
    var executor = Executor(1);
    Start(executor, world);
    var id = Id(50);
    Run(executor, ctx =>
    {
        Check(world.Spawn(ctx, Seed(id)) == WorldEntityResult.Spawned, "spawn failed");
        Check(world.RegisterCleanup(ctx, id, new Hook(_ =>
            throw new InvalidOperationException("simulated cleanup fault"))) ==
            WorldEntityResult.CleanupRegistered, "cleanup hook not registered");
    });
    executor.CreateTrustedProducer().Offer(new Act(ctx => world.Despawn(ctx, id)));
    Check(executor.RunBatch(1).Status == WorldRunStatus.Faulted &&
        owner.ClaimedEntities == 1 && world.State == WorldLifecycle.Faulted,
        "fault prematurely released ownership or failed to mark world fault");
    return Task.CompletedTask;
}

internal sealed class Act(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}

internal sealed class Hook(Action<EntityIdentity> action) : IWorldEntityCleanup
{
    public void OnRemoving(EntityIdentity identity) => action(identity);
}
