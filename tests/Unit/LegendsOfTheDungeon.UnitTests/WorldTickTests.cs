using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Platform.WorldScheduling;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldTickTests
{
    private static readonly DateTimeOffset EpochTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (WorldInstance World, WorldExecutor Executor, FakeClock Clock) Create(ulong id = 1)
    {
        var world = new WorldInstance(new WorldId(id), new WorldEpoch(1), new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 64, 16, 2);
        var clock = new FakeClock(new Duration(50), EpochTime);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });
        return (world, executor, clock);
    }

    private static EntityIdentity Identity(ulong entity, ulong world = 1) =>
        new(new WorldId(world), new WorldEpoch(1), new EntityId(entity), new EntityGeneration(1));

    private static WorldEntitySeed Seed(EntityIdentity identity) =>
        new(identity, new Transform(new Position(0, 0, 0), new Angle(0)));

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> work)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(work)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    [Fact]
    public void PhaseAndKeyOrderAreStableRegardlessOfRegistrationOrder()
    {
        var (world, executor, clock) = Create();
        var calls = new List<string>();
        var steps = new IWorldTickStep[]
        {
            new Step(WorldTickPhase.Commit, "c", (_, _, _) => calls.Add("commit")),
            new Step(WorldTickPhase.Input, "z", (_, _, _) => calls.Add("input-z")),
            new Step(WorldTickPhase.Simulation, "simulate", (_, _, _) => calls.Add("simulation")),
            new Step(WorldTickPhase.Input, "a", (_, _, _) => calls.Add("input-a")),
            new Step(WorldTickPhase.Timers, "timer", (_, _, _) => calls.Add("timer"))
        };
        var pipeline = new WorldTickPipeline(world, clock, steps);
        Assert.Equal(["a", "z", "timer", "simulate", "c"],
            pipeline.OrderedSteps.Select(x => x.Key));
        clock.AdvanceTicks(1);
        Run(executor, ctx => Assert.Equal(1UL, pipeline.Execute(ctx, new Tick(1)).Tick.Value));
        Assert.Equal(["input-a", "input-z", "timer", "simulation", "commit"], calls);
        Assert.Equal(64, pipeline.LastStateHash.Length);
    }

    [Fact]
    public void DuplicateOrInvalidPhasesAndOversizedStepSetsAreRejected()
    {
        var (world, _, clock) = Create();
        Assert.Throws<ArgumentException>(() => new WorldTickPipeline(world, clock,
            [new Step(WorldTickPhase.Input, "a", (_, _, _) => { }),
             new Step(WorldTickPhase.Input, "a", (_, _, _) => { })]));
        Assert.Throws<ArgumentException>(() => new WorldTickPipeline(world, clock,
            [new Step((WorldTickPhase)99, "a", (_, _, _) => { })]));
        Assert.Throws<ArgumentException>(() => new WorldTickPipeline(world, clock,
            [new Step(WorldTickPhase.Input, " ", (_, _, _) => { })]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTickPipeline(world, clock,
            Enumerable.Range(0, 129).Select(i =>
                (IWorldTickStep)new Step(WorldTickPhase.Input, i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), (_, _, _) => { }))));
    }

    [Fact]
    public async Task FakeClockCatchupIsBoundedAndSlowTicksAreMeasuredSeparately()
    {
        var (world, executor, clock) = Create();
        var calls = new List<ulong>();
        var pipeline = new WorldTickPipeline(world, clock,
            [new Step(WorldTickPhase.Simulation, "count", (_, _, tick) => calls.Add(tick.Value))]);
        var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)],
            maximumWorkers: 1, maximumCatchupTicks: 3,
            meter: new Meter(TimeSpan.FromMilliseconds(80)));
        clock.AdvanceTicks(20);
        var first = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(WorldTickLaneStatus.Deferred, first.Status);
        Assert.Equal(3, first.CompletedTicks);
        Assert.Equal(3, first.SlowTicks);
        Assert.Equal(17UL, first.RemainingDueTicks);
        Assert.Equal(80.0, first.MaximumDurationMilliseconds);
        Assert.Equal([1UL, 2, 3], calls);
        var second = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(3, second.CompletedTicks);
        Assert.Equal(14UL, second.RemainingDueTicks);
        Assert.Equal([1UL, 2, 3, 4, 5, 6], calls);
    }

    [Fact]
    public async Task SameAcceptedSequenceAndDifferentStepRegistrationOrderProduceSameHashes()
    {
        async Task<IReadOnlyList<string>> Replay(bool reverse, bool differentUtc)
        {
            var (world, executor, clock) = Create();
            var identity = Identity(5);
            Run(executor, ctx => Assert.Equal(WorldEntityResult.Spawned,
                world.Spawn(ctx, Seed(identity))));
            var steps = new IWorldTickStep[]
            {
                new Step(WorldTickPhase.Simulation, "update", (ctx, w, t) =>
                    Assert.Equal(WorldEntityResult.Relocated, w.Relocate(ctx, identity,
                        new Transform(new Position(t.Value, 2, 3), new Angle(0))))),
                new Step(WorldTickPhase.Input, "noop", (_, _, _) => { })
            };
            var pipeline = new WorldTickPipeline(world, clock, reverse ? steps.Reverse() : steps);
            var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)], 1, 2);
            var hashes = new List<string>();
            if (differentUtc) clock.SetUtc(EpochTime.AddYears(50));
            for (var i = 0; i < 5; i++)
            {
                clock.AdvanceTicks(1);
                var item = Assert.Single((await scheduler.RunDueAsync()).Worlds);
                Assert.Equal(WorldTickLaneStatus.Completed, item.Status);
                hashes.Add(item.StateHash);
            }
            return hashes;
        }

        var original = await Replay(false, false);
        var same = await Replay(true, true);
        Assert.Equal(original, same);
        Assert.Equal(5, original.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task StateHashSortsEntityIdsAndIncludesGenerationTransformLifeAndTarget()
    {
        async Task<string> Hash(bool reverse)
        {
            var (world, executor, clock) = Create();
            var ids = new[] { Identity(8), Identity(2) };
            Run(executor, ctx =>
            {
                foreach (var id in reverse ? ids.Reverse() : ids)
                {
                    Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
                }
            });
            var pipeline = new WorldTickPipeline(world, clock, []);
            clock.AdvanceTicks(1);
            Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
            return pipeline.LastStateHash;
        }

        Assert.Equal(await Hash(false), await Hash(true));

        var (owner, executor, clock) = Create();
        var source = Identity(1);
        var target = Identity(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, owner.Spawn(ctx, Seed(source)));
            Assert.Equal(WorldEntityResult.Spawned, owner.Spawn(ctx, Seed(target)));
        });
        var pipeline2 = new WorldTickPipeline(owner, clock, []);
        clock.AdvanceTicks(3);
        Run(executor, ctx => pipeline2.Execute(ctx, new Tick(1)));
        var before = pipeline2.LastStateHash;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TargetChanged,
            owner.SelectTarget(ctx, source, target)));
        Run(executor, ctx => pipeline2.Execute(ctx, new Tick(2)));
        Assert.NotEqual(before, pipeline2.LastStateHash);
        var targeted = pipeline2.LastStateHash;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.LifeChanged, owner.MarkDead(ctx, target)));
        Run(executor, ctx => pipeline2.Execute(ctx, new Tick(3)));
        Assert.NotEqual(targeted, pipeline2.LastStateHash);
    }

    [Fact]
    public void TickMustBeContiguousAndDueAndWriteLeaseMustBeGenuine()
    {
        var (world, executor, clock) = Create();
        var pipeline = new WorldTickPipeline(world, clock, []);
        IWorldWriteContext? captured = null;
        Run(executor, ctx => captured = ctx);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() => pipeline.Execute(captured!, new Tick(1))).Fault);
        clock.AdvanceTicks(2);
        Run(executor, ctx => Assert.Equal(1UL, pipeline.Execute(ctx, new Tick(1)).Tick.Value));
        Assert.Equal(WorldAdmissionStatus.Accepted, executor.CreateTrustedProducer().Offer(new Work(ctx =>
            pipeline.Execute(ctx, new Tick(1)))));
        Assert.Equal(WorldRunStatus.Faulted, executor.RunBatch(1).Status);
        Assert.Equal(WorldLifecycle.Faulted, world.State);
        Assert.Equal(1UL, pipeline.LastCommittedTick.Value);
    }

    [Fact]
    public async Task FullMailboxNeverCreatesPhantomTickOrSkipsSequence()
    {
        var (world, executor, clock) = Create();
        var pipeline = new WorldTickPipeline(world, clock, []);
        var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)], 1, 2);
        clock.AdvanceTicks(1);
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(WorldAdmissionStatus.Accepted,
                executor.CreateTrustedProducer().Offer(new Work(_ => { })));
        }
        var busy = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(WorldTickLaneStatus.Backpressured, busy.Status);
        Assert.Equal(0, busy.CompletedTicks);
        Assert.Equal(1UL, busy.RemainingDueTicks);
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(64).Status);
        var committed = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(1, committed.CompletedTicks);
        Assert.Equal(1UL, committed.LastTick.Value);
    }

    [Fact]
    public async Task TickQueuedBehindEarlierCommandIsResumedWithoutDuplicateOffer()
    {
        var (world, executor, clock) = Create();
        var counter = 0;
        var pipeline = new WorldTickPipeline(world, clock,
            [new Step(WorldTickPhase.Input, "count", (_, _, _) => counter++)]);
        var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)], 1, 2, 1);
        clock.AdvanceTicks(1);
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(_ => { })));
        var deferred = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(WorldTickLaneStatus.Deferred, deferred.Status);
        Assert.Equal(0, deferred.CompletedTicks);
        var completed = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(1, completed.CompletedTicks);
        Assert.Equal(1, counter);
        Assert.Equal(0UL, completed.RemainingDueTicks);
    }

    [Fact]
    public async Task CoordinatorRejectsConcurrentRunWhileTickIsBlocked()
    {
        var (world, executor, clock) = Create();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pipeline = new WorldTickPipeline(world, clock,
            [new Step(WorldTickPhase.Simulation, "blocking", (_, _, _) =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            })]);
        var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)], 1);
        clock.AdvanceTicks(1);
        var active = scheduler.RunDueAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(WorldTickScheduleStatus.Busy, (await scheduler.RunDueAsync()).Status);
        }
        finally
        {
            release.Set();
        }
        var complete = Assert.Single((await active).Worlds);
        Assert.Equal(1, complete.CompletedTicks);
    }

    [Fact]
    public async Task TwoWorldsCanRunInParallelWithTwoWorkersAndSingleWorkerIsBounded()
    {
        var a = Create(1);
        var b = Create(2);
        using var both = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        IWorldTickStep StepFor() => new Step(WorldTickPhase.Simulation, "sync", (_, _, _) =>
        {
            both.Signal();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        });
        var pa = new WorldTickPipeline(a.World, a.Clock, [StepFor()]);
        var pb = new WorldTickPipeline(b.World, b.Clock, [StepFor()]);
        var scheduler = new WorldTickScheduler(
            [new WorldTickBinding(b.Executor, pb), new WorldTickBinding(a.Executor, pa)], 2);
        a.Clock.AdvanceTicks(1);
        b.Clock.AdvanceTicks(1);
        var running = scheduler.RunDueAsync();
        try
        {
            Assert.True(both.Wait(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release.Set();
        }
        var reports = (await running).Worlds;
        Assert.Equal([1UL, 2UL], reports.Select(item => item.World.Value));
        Assert.All(reports, r => Assert.Equal(1, r.CompletedTicks));
    }

    [Fact]
    public void DuplicateWorldBindingsAndInvalidWorkerBudgetsAreRejected()
    {
        var (world, executor, clock) = Create();
        var pipeline = new WorldTickPipeline(world, clock, []);
        var binding = new WorldTickBinding(executor, pipeline);
        Assert.Throws<ArgumentException>(() => new WorldTickScheduler([binding, binding]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTickScheduler([binding], 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTickScheduler([binding], 1, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTickScheduler([binding], 1, 1, 1025));
        Assert.Throws<ArgumentException>(() => new WorldTickScheduler([]));
        var other = new WorldExecutor(new WorldId(2), new WorldEpoch(1),
            new DenyByDefaultCommandAuthorizer(), 4, 4, 2);
        Assert.Throws<ArgumentException>(() => new WorldTickBinding(other, pipeline));
    }

    [Fact]
    public async Task FailingPhaseFaultsBothWorldAndExecutorWithoutPublishingTickHash()
    {
        var (world, executor, clock) = Create();
        var pipeline = new WorldTickPipeline(world, clock,
            [new Step(WorldTickPhase.Simulation, "fail", (_, _, _) =>
                throw new InvalidOperationException("partial failure"))]);
        var scheduler = new WorldTickScheduler([new WorldTickBinding(executor, pipeline)]);
        clock.AdvanceTicks(1);
        var report = Assert.Single((await scheduler.RunDueAsync()).Worlds);
        Assert.Equal(WorldTickLaneStatus.Faulted, report.Status);
        Assert.Equal(WorldLifecycle.Faulted, world.State);
        Assert.True(executor.IsFaulted);
        Assert.Equal(0UL, pipeline.LastCommittedTick.Value);
        Assert.Equal(string.Empty, report.StateHash);
        Assert.Equal(1UL, report.RemainingDueTicks);
    }

    private sealed class Work(Action<IWorldWriteContext> body) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => body(context);
    }

    private sealed class Step(WorldTickPhase phase, string key,
        Action<IWorldWriteContext, WorldInstance, Tick> action) : IWorldTickStep
    {
        public WorldTickPhase Phase => phase;
        public string Key => key;
        public void Execute(IWorldWriteContext context, WorldInstance world, Tick tick) =>
            action(context, world, tick);
    }

    private sealed class Meter(TimeSpan elapsed) : IWorldTickExecutionMeter
    {
        public long Timestamp() => 1;
        public TimeSpan Elapsed(long _) => elapsed;
    }
}
