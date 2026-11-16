using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Platform.WorldScheduling;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

await CheckAsync("same-inputs-ordered-phases-and-stable-state-hash", ReplayAsync);
await CheckAsync("two-worlds-real-executor-parallel-with-bounded-workers", ParallelWorldsAsync);
await CheckAsync("fake-clock-bounded-catchup-and-slow-tick-observation", CatchupAsync);
await CheckAsync("backpressure-never-promotes-admission-to-commit", BackpressureAsync);
Console.WriteLine("P19_WORLD_TICK_INTEGRATION_PASS");

static async Task CheckAsync(string name, Func<Task> test)
{
    await test().WaitAsync(TimeSpan.FromSeconds(25));
    Console.WriteLine("PASS " + name);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static (WorldInstance World, WorldExecutor Executor, FakeClock Clock) Create(ulong id = 1, int capacity = 64)
{
    var world = new WorldInstance(new WorldId(id), new WorldEpoch(1), new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), capacity, 16, 2);
    var clock = new FakeClock(new Duration(50),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    Offer(executor, ctx =>
    {
        Check(world.BeginLoading(ctx), "world did not enter Loading");
        Check(world.Start(ctx), "world did not enter Running");
    });
    return (world, executor, clock);
}

static void Offer(WorldExecutor executor, Action<IWorldWriteContext> work)
{
    Check(executor.CreateTrustedProducer().Offer(new ActionWork(work)) ==
        WorldAdmissionStatus.Accepted, "trusted offer failed");
    Check(executor.RunBatch(1).Status == WorldRunStatus.Completed,
        "trusted action failed on P18 executor");
}

static async Task ReplayAsync()
{
    async Task<List<string>> Replay(bool reverse, bool modifyPosition)
    {
        var (world, executor, clock) = Create();
        var entity = new EntityIdentity(world.World, world.Epoch,
            new EntityId(101), new EntityGeneration(1));
        Offer(executor, ctx =>
        {
            Check(world.Spawn(ctx, new WorldEntitySeed(entity,
                new Transform(new Position(0, 0, 0), new Angle(0)))) ==
                WorldEntityResult.Spawned, "spawn rejected");
        });
        var trace = new List<string>();
        var steps = new IWorldTickStep[]
        {
            new Step(WorldTickPhase.Commit, "finish", (_, _, tick) =>
                trace.Add("commit-" + tick.Value)),
            new Step(WorldTickPhase.Input, "ordered", (_, _, tick) =>
                trace.Add("input-" + tick.Value)),
            new Step(WorldTickPhase.Simulation, "position", (ctx, w, tick) =>
            {
                var x = modifyPosition && tick.Value == 3 ? 999d : tick.Value;
                Check(w.Relocate(ctx, entity,
                    new Transform(new Position(x, 2, 3), new Angle(0))) ==
                    WorldEntityResult.Relocated, "simulation did not mutate world under P18 lease");
                trace.Add("simulation-" + tick.Value);
            })
        };
        var pipeline = new WorldTickPipeline(world, clock,
            reverse ? steps.Reverse() : steps);
        var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)], 1, 2);
        var hashes = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            clock.AdvanceTicks(1);
            var cycle = await scheduler.RunDueAsync();
            var report = cycle.Worlds.Single();
            Check(report.Status == WorldTickLaneStatus.Completed &&
                report.CompletedTicks == 1 && report.LastTick.Value == (ulong)i &&
                report.StateHash.Length == 64, "tick did not commit precisely once");
            hashes.Add(report.StateHash);
        }

        Check(trace.Count == 15, "one or more scheduled phases were skipped");
        for (var i = 0; i < 5; i++)
        {
            Check(trace[i * 3] == "input-" + (i + 1) &&
                  trace[i * 3 + 1] == "simulation-" + (i + 1) &&
                  trace[i * 3 + 2] == "commit-" + (i + 1),
                  "phase ordering was not stable");
        }

        return hashes;
    }

    var baseline = await Replay(false, false);
    var shuffled = await Replay(true, false);
    var changed = await Replay(false, true);
    Check(baseline.SequenceEqual(shuffled), "same accepted sequence produced divergent SHA256 hashes");
    Check(baseline[2] != changed[2], "state hash missed a different position");
}

static async Task ParallelWorldsAsync()
{
    var a = Create(1);
    var b = Create(2);
    using var both = new CountdownEvent(2);
    using var release = new ManualResetEventSlim();
    IWorldTickStep Step() => new Step(WorldTickPhase.Simulation, "parallel", (_, _, _) =>
    {
        both.Signal();
        Check(release.Wait(TimeSpan.FromSeconds(12)), "worlds were serialized across bounded workers");
    });
    var scheduler = new WorldTickScheduler(
        [new WorldTickBinding(b.Executor, new WorldTickPipeline(b.World, b.Clock, [Step()])),
         new WorldTickBinding(a.Executor, new WorldTickPipeline(a.World, a.Clock, [Step()]))], 2);
    a.Clock.AdvanceTicks(1);
    b.Clock.AdvanceTicks(1);
    var running = scheduler.RunDueAsync();
    try
    {
        Check(both.Wait(TimeSpan.FromSeconds(12)), "two worlds never ran concurrently");
    }
    finally
    {
        release.Set();
    }
    var report = await running;
    Check(report.Worlds.Count == 2 && report.Worlds[0].World.Value == 1 &&
        report.Worlds[1].World.Value == 2 &&
        report.Worlds.All(r => r.CompletedTicks == 1), "parallel worlds did not commit separately");
}

static async Task CatchupAsync()
{
    var (world, executor, clock) = Create();
    var pipeline = new WorldTickPipeline(world, clock, []);
    var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)],
        1, 3, 64, new Meter(TimeSpan.FromMilliseconds(60)));
    clock.AdvanceTicks(32);
    var first = (await scheduler.RunDueAsync()).Worlds.Single();
    Check(first.CompletedTicks == 3 && first.RemainingDueTicks == 29 &&
        first.SlowTicks == 3 && first.MaximumDurationMilliseconds == 60,
        "catchup failed to respect bounded budget and slow-tick measurement");
    var next = (await scheduler.RunDueAsync()).Worlds.Single();
    Check(next.CompletedTicks == 3 && next.RemainingDueTicks == 26 &&
        next.LastTick.Value == 6, "lag was skipped or duplicated");
}

static async Task BackpressureAsync()
{
    var (world, executor, clock) = Create(capacity: 4);
    var pipeline = new WorldTickPipeline(world, clock, []);
    var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)], 1);
    clock.AdvanceTicks(1);
    for (var i = 0; i < 4; i++)
    {
        Check(executor.CreateTrustedProducer().Offer(new ActionWork(_ => { })) ==
            WorldAdmissionStatus.Accepted, "failed to fill P18 normal lane");
    }

    var full = (await scheduler.RunDueAsync()).Worlds.Single();
    Check(full.Status == WorldTickLaneStatus.Backpressured &&
        full.CompletedTicks == 0 && pipeline.LastCommittedTick.Value == 0,
        "full mailbox reported a ghost committed Tick");
    Check(executor.RunBatch(4).Executed == 4, "could not drain full normal lane");
    var committed = (await scheduler.RunDueAsync()).Worlds.Single();
    Check(committed.CompletedTicks == 1 && committed.LastTick.Value == 1,
        "previously backpressured Tick not executed after budget freed");
}

internal sealed class ActionWork(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}

internal sealed class Step(WorldTickPhase phase, string key,
    Action<IWorldWriteContext, WorldInstance, Tick> action) : IWorldTickStep
{
    public WorldTickPhase Phase => phase;
    public string Key => key;
    public void Execute(IWorldWriteContext context, WorldInstance world, Tick tick) =>
        action(context, world, tick);
}

internal sealed class Meter(TimeSpan elapsed) : IWorldTickExecutionMeter
{
    public long Timestamp() => 1;
    public TimeSpan Elapsed(long _) => elapsed;
}
