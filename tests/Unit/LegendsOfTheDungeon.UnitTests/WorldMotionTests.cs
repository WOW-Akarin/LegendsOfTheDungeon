using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.Environment;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldMotionTests
{
    private static readonly Duration Step = new(50);

    private static EntityIdentity Id(ulong number = 1, ulong generation = 1) =>
        new(new WorldId(14), new WorldEpoch(2),
            new EntityId(number), new EntityGeneration(generation));

    private static (WorldInstance World, WorldExecutor Executor) Start()
    {
        var world = new WorldInstance(new WorldId(14), new WorldEpoch(2),
            new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
            Spawn(world, ctx, Id(), new Position(.5, .5, 0));
        });
        return (world, executor);
    }

    private static void Spawn(WorldInstance world, IWorldWriteContext context,
        EntityIdentity identity, Position start)
    {
        Assert.Equal(WorldEntityResult.Spawned, world.Spawn(context,
            new WorldEntitySeed(identity, new Transform(start, new Angle(0)))));
        Assert.Equal(WorldEntityResult.AttributeChanged,
            world.SetBaseAttribute(context, identity, WorldSpeedModel.SpeedStat, 20m));
    }

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(action)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    private sealed class Work(Action<IWorldWriteContext> body) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => body(context);
    }

    private static PlanarGridEnvironment Port(IEnumerable<PlaneCell>? obstacles = null) =>
        new PlanarGridEnvironment(-9, -9, 9, 9, 1, 0, obstacles ?? [], 12);

    private static WorldMotionPathRequest Begin(WorldInstance world, WorldExecutor executor,
        WorldMotionCommand command, EntityIdentity? identity = null, long environmentVersion = 12,
        ulong tick = 0)
    {
        WorldMotionPathRequest? pending = null;
        Run(executor, ctx => Assert.Equal(WorldMotionResult.Started,
            world.StartMotion(ctx, identity ?? Id(), command, new Tick(tick),
                environmentVersion, out pending)));
        Assert.NotNull(pending);
        return pending.Value;
    }

    private static void Apply(WorldInstance world, WorldExecutor executor, INavigationQuery port,
        WorldMotionPathRequest request, WorldMotionResult expected = WorldMotionResult.PathAccepted)
    {
        var result = port.FindPath(request.Request);
        Run(executor, ctx => Assert.Equal(expected, world.ApplyMotionPath(ctx, request, result)));
    }

    private static WorldMotionSnapshot Finish(WorldInstance world, WorldExecutor executor,
        INavigationQuery port, EntityIdentity? entity = null, ulong start = 1, ulong limit = 160)
    {
        var identity = entity ?? Id();
        for (var tick = start; tick <= limit; tick++)
        {
            if (world.TryGetPendingMotionPath(identity, out var next)) Apply(world, executor, port, next);
            Run(executor, ctx => world.AdvanceMotion(ctx, identity, new Tick(tick), Step));
            Assert.True(world.TryGetMotion(identity, out var snapshot));
            if (snapshot.State != WorldMotionState.AwaitingPath &&
                snapshot.State != WorldMotionState.Active) return snapshot;
        }

        throw new Xunit.Sdk.XunitException("Motion did not reach terminal state in bounded Tick budget.");
    }

    [Theory]
    [InlineData(WorldMotionKind.Point)]
    [InlineData(WorldMotionKind.Random)]
    [InlineData(WorldMotionKind.Home)]
    [InlineData(WorldMotionKind.Waypoint)]
    [InlineData(WorldMotionKind.Spline)]
    public void StaticMotionModesReachCanonicalDestination(WorldMotionKind kind)
    {
        var (world, executor) = Start();
        var port = Port();
        if (kind == WorldMotionKind.Home)
            Run(executor, ctx => Assert.Equal(WorldEntityResult.Relocated,
                world.Relocate(ctx, Id(), new Transform(new Position(2.5, .5, 0), new Angle(0)))));
        WorldMotionCommand command = kind switch
        {
            WorldMotionKind.Random => new(kind, seed: 123),
            WorldMotionKind.Home => new(kind),
            WorldMotionKind.Waypoint or WorldMotionKind.Spline => new(kind,
                points: [new Position(1.5, .5, 0), new Position(2.5, 1.5, 0)]),
            _ => new(kind, goal: new Position(3.5, .5, 0))
        };
        var first = Begin(world, executor, command);
        Apply(world, executor, port, first);
        var done = Finish(world, executor, port);
        Assert.Equal(WorldMotionState.Completed, done.State);
        Assert.Equal(kind, done.Kind);
        Assert.True(world.TryGetSnapshot(Id().Id, out var entity));
        Assert.Equal(kind switch
        {
            WorldMotionKind.Home => new Position(.5, .5, 0),
            WorldMotionKind.Waypoint or WorldMotionKind.Spline => new Position(2.5, 1.5, 0),
            WorldMotionKind.Random => done.Goal,
            _ => new Position(3.5, .5, 0)
        }, entity.Transform.Position);
        Assert.Equal(done, entity.Motion);
        Assert.Single(world.QueryNearby(entity.Transform.Position, default));
    }

    [Theory]
    [InlineData(WorldMotionKind.Chase)]
    [InlineData(WorldMotionKind.Follow)]
    [InlineData(WorldMotionKind.Flee)]
    public void DynamicMotionTracksGenuineGenerationAndCompletes(WorldMotionKind kind)
    {
        var (world, executor) = Start();
        Run(executor, ctx => Spawn(world, ctx, Id(2), new Position(4.5, .5, 0)));
        var port = Port();
        var first = Begin(world, executor, new WorldMotionCommand(kind, target: Id(2)));
        Assert.Equal(Id(2), first.Target);
        Apply(world, executor, port, first);
        var done = Finish(world, executor, port);
        Assert.Equal(WorldMotionState.Completed, done.State);
        Assert.Equal(kind, done.Kind);
        Assert.True(world.TryGetSnapshot(Id().Id, out var snapshot));
        if (kind == WorldMotionKind.Flee)
            Assert.True(snapshot.Transform.Position.X < .5);
        else if (kind == WorldMotionKind.Chase)
            Assert.Equal(new Position(4.5, .5, 0), snapshot.Transform.Position);
        else
            Assert.True(snapshot.Transform.Position.IsWithin(
                new Position(4.5, .5, 0), new Distance(1.5)));
    }

    [Theory]
    [InlineData(WorldMotionKind.Jump)]
    [InlineData(WorldMotionKind.Knockback)]
    public void TrustedBallisticModesAdvanceWithoutFakingNavigation(WorldMotionKind kind)
    {
        var (world, executor) = Start();
        WorldMotionPathRequest? request = null;
        Run(executor, ctx => Assert.Equal(WorldMotionResult.Started, world.StartMotion(ctx,
            Id(), new WorldMotionCommand(kind, priority: 80,
                goal: new Position(2.5, .5, 0), durationTicks: 4),
            new Tick(0), 12, out request)));
        Assert.Null(request);
        Run(executor, ctx => Assert.Equal(WorldMotionResult.Advanced,
            world.AdvanceMotion(ctx, Id(), new Tick(1), Step)));
        Assert.True(world.TryGetSnapshot(Id().Id, out var midway));
        Assert.True(kind != WorldMotionKind.Jump || midway.Transform.Position.Z > 0);
        for (ulong tick = 2; tick <= 4; tick++)
        {
            Run(executor, ctx =>
            {
                var result = world.AdvanceMotion(ctx, Id(), new Tick(tick), Step);
                Assert.Equal(tick == 4 ? WorldMotionResult.Completed : WorldMotionResult.Advanced, result);
            });
        }
        Assert.True(world.TryGetSnapshot(Id().Id, out var finished));
        Assert.Equal(new Position(2.5, .5, 0), finished.Transform.Position);
        Assert.Equal(WorldMotionState.Completed, finished.Motion.State);
        Assert.Equal(4U, finished.Motion.BallisticProgress);
    }

    [Fact]
    public void EnvironmentNoPathCannotTurnIntoStraightLineTeleport()
    {
        var (world, executor) = Start();
        var wall = Enumerable.Range(-9, 19).Select(y => new PlaneCell(1, y));
        var port = Port(wall);
        var first = Begin(world, executor,
            new WorldMotionCommand(WorldMotionKind.Point,
                goal: new Position(3.5, .5, 0)));
        Assert.Equal(EnvironmentQueryStatus.NoPath, port.FindPath(first.Request).Status);
        Apply(world, executor, port, first, WorldMotionResult.NoPath);
        Assert.True(world.TryGetMotion(Id(), out var state));
        Assert.Equal(WorldMotionState.NoPath, state.State);
        Assert.Equal(WorldMotionResult.NotActive, RecordAdvance(world, executor, 1));
        Assert.True(world.TryGetSnapshot(Id().Id, out var entity));
        Assert.Equal(new Position(.5, .5, 0), entity.Transform.Position);
    }

    private static WorldMotionResult RecordAdvance(WorldInstance world, WorldExecutor executor, ulong tick)
    {
        var result = WorldMotionResult.NotActive;
        Run(executor, ctx => result = world.AdvanceMotion(ctx, Id(), new Tick(tick), Step));
        return result;
    }

    [Fact]
    public void PriorityCancelAndStalePathCannotOverwriteCurrentMotion()
    {
        var (world, executor) = Start();
        var port = Port();
        var first = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            priority: 10, goal: new Position(3.5, .5, 0)));
        Run(executor, ctx =>
        {
            Assert.Equal(WorldMotionResult.PriorityRejected,
                world.StartMotion(ctx, Id(), new WorldMotionCommand(WorldMotionKind.Home, priority: 1),
                    new Tick(0), 12, out _));
            Assert.Equal(WorldMotionResult.Started,
                world.StartMotion(ctx, Id(), new WorldMotionCommand(WorldMotionKind.Point,
                    priority: 80, goal: new Position(1.5, .5, 0)),
                    new Tick(0), 12, out _));
        });
        Assert.True(world.TryGetPendingMotionPath(Id(), out var latest));
        Assert.True(latest.MotionVersion > first.MotionVersion);
        Apply(world, executor, port, first, WorldMotionResult.Stale);
        Apply(world, executor, port, latest);
        Assert.Equal(WorldMotionState.Completed, Finish(world, executor, port).State);
        var wait = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(3.5, .5, 0)), tick: 10);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldMotionResult.Stale, world.CancelMotion(ctx, Id(), first.MotionVersion));
            Assert.Equal(WorldMotionResult.Cancelled,
                world.CancelMotion(ctx, Id(), wait.MotionVersion));
            Assert.Equal(WorldMotionResult.Stale,
                world.ApplyMotionPath(ctx, wait, port.FindPath(wait.Request)));
        });
        Assert.True(world.TryGetMotion(Id(), out var state));
        Assert.Equal(WorldMotionState.Cancelled, state.State);
    }

    [Fact]
    public void MovingTargetReplansButOldPathAndDestroyedTargetAreRejected()
    {
        var (world, executor) = Start();
        Run(executor, ctx => Spawn(world, ctx, Id(2), new Position(3.5, .5, 0)));
        var port = Port();
        var first = Begin(world, executor,
            new WorldMotionCommand(WorldMotionKind.Chase, target: Id(2)));
        Apply(world, executor, port, first);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Relocated,
                world.Relocate(ctx, Id(2), new Transform(new Position(4.5, .5, 0), new Angle(0))));
            Assert.Equal(WorldMotionResult.WaitingForPath,
                world.AdvanceMotion(ctx, Id(), new Tick(1), Step));
        });
        Assert.True(world.TryGetPendingMotionPath(Id(), out var newest));
        Assert.True(newest.Request.RequestVersion > first.Request.RequestVersion);
        Apply(world, executor, port, first, WorldMotionResult.Stale);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.LifeChanged, world.MarkDead(ctx, Id(2))));
        Assert.True(world.TryGetMotion(Id(), out var state));
        Assert.Equal(WorldMotionState.TargetLost, state.State);
        Apply(world, executor, port, newest, WorldMotionResult.Stale);
        Assert.True(world.TryGetSnapshot(Id().Id, out var unchanged));
        Assert.Equal(new Position(.5, .5, 0), unchanged.Transform.Position);
    }

    [Fact]
    public void DeadlineAndEnvironmentVersionAreCheckedBeforeMutatingPath()
    {
        var (world, executor) = Start();
        var port = Port();
        var request = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(3.5, .5, 0), deadline: new Tick(2)));
        var real = port.FindPath(request.Request);
        var wrongVersion = new NavigationPathResult(real.Status, real.RequestVersion, 99, real.Waypoints);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldMotionResult.Stale,
                world.ApplyMotionPath(ctx, request, wrongVersion));
            Assert.Equal(WorldMotionResult.PathAccepted,
                world.ApplyMotionPath(ctx, request, real));
            Assert.Equal(WorldMotionResult.Advanced,
                world.AdvanceMotion(ctx, Id(), new Tick(1), Step));
            Assert.Equal(WorldMotionResult.TimedOut,
                world.AdvanceMotion(ctx, Id(), new Tick(2), Step));
        });
        Assert.True(world.TryGetMotion(Id(), out var state));
        Assert.Equal(WorldMotionState.TimedOut, state.State);
    }

    [Fact]
    public void ControlDeathManualRelocateAndP27InputNeverSplitPositionOwnership()
    {
        var (world, executor) = Start();
        var port = Port();
        var first = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(3.5, .5, 0)));
        Apply(world, executor, port, first);
        EntityEffectHandle root = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ControlGranted, world.GrantControl(ctx, Id(),
                new EntityEffectSource(3), EntityControlEffects.Rooted, out root));
            Assert.Equal(WorldMotionResult.Paused, world.AdvanceMotion(ctx, Id(), new Tick(1), Step));
            Assert.Equal(WorldEntityResult.ControlRevoked, world.RevokeControl(ctx, Id(), root));
            Assert.Equal(WorldMotionResult.Advanced, world.AdvanceMotion(ctx, Id(), new Tick(2), Step));
            Assert.Equal(WorldEntityResult.Relocated, world.Relocate(ctx, Id(),
                new Transform(new Position(4.5, .5, 0), new Angle(0))));
        });
        Assert.True(world.TryGetMotion(Id(), out var state));
        Assert.Equal(WorldMotionState.Cancelled, state.State);
        Assert.Equal(WorldMotionResult.NotActive, RecordAdvance(world, executor, 3));
        ulong bound = 0;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.MovementBound,
                world.BindMovementSubject(ctx, Id(), BoundIdentity.CreateLocalTest("alice"),
                    new Tick(3), Step, out bound));
            Assert.Equal(WorldEntityResult.MovementAccepted,
                world.SubmitMovement(ctx, new WorldMovementIntent(Id(), BoundIdentity.CreateLocalTest("alice"),
                    bound, 1, new Tick(4), new Transform(new Position(4.75, .5, 0), new Angle(0)))));
        });
        Assert.True(world.TryGetSnapshot(Id().Id, out var snapshot));
        Assert.Equal(new Position(4.75, .5, 0), snapshot.Transform.Position);
        Assert.Single(world.QueryNearby(snapshot.Transform.Position, default));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.LifeChanged, world.MarkDead(ctx, Id())));
        Assert.Equal(WorldMotionState.Cancelled, world.QueryNearby(snapshot.Transform.Position, default)[0].Motion.State);
    }

    [Fact]
    public void RetiredGenerationAndExpiredWorldLeaseRejectOldAsyncPath()
    {
        var (world, executor) = Start();
        var port = Port();
        var old = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(3.5, .5, 0)));
        IWorldWriteContext? captured = null;
        Run(executor, ctx => captured = ctx);
        Assert.Equal(WorldAccessFault.LeaseRequired, Assert.Throws<WorldAccessException>(
            () => world.ApplyMotionPath(captured!, old, port.FindPath(old.Request))).Fault);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, Id()));
            Spawn(world, ctx, Id(generation: 2), new Position(1.5, .5, 0));
            Assert.Equal(WorldMotionResult.Stale,
                world.ApplyMotionPath(ctx, old, port.FindPath(old.Request)));
        });
        Assert.True(world.TryGetMotion(Id(generation: 2), out var state));
        Assert.Equal(WorldMotionState.Idle, state.State);
    }

    [Fact]
    public void RealWorldTickStepAdvancesMotionInsideFixedSimulationPhase()
    {
        var (world, executor) = Start();
        var port = Port();
        var pending = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(2.5, .5, 0)));
        Apply(world, executor, port, pending);
        var clock = new FakeClock(Step,
            new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new WorldTickPipeline(world, clock, [new WorldMotionTickStep(Step)]);
        clock.AdvanceTicks(1);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
        Assert.True(world.TryGetSnapshot(Id().Id, out var initialCell));
        Assert.Equal(new Position(.5, .5, 0), initialCell.Transform.Position);
        Assert.Equal(2, initialCell.Motion.PathIndex);
        clock.AdvanceTicks(1);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(2)));
        Assert.True(world.TryGetSnapshot(Id().Id, out var entity));
        Assert.NotEqual(new Position(.5, .5, 0), entity.Transform.Position);
        Assert.Equal(new Tick(2), entity.Motion.LastTick);
        Assert.Equal(64, pipeline.LastStateHash.Length);
    }

    [Fact]
    public void MotionStateHashTracksFuturePathAndPriorityAtSameTick()
    {
        static string Hash(int priority)
        {
            var (world, executor) = Start();
            var pending = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
                priority: priority, goal: new Position(2.5, .5, 0)));
            Apply(world, executor, Port(), pending);
            var clock = new FakeClock(Step,
                new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
            var pipeline = new WorldTickPipeline(world, clock, []);
            clock.AdvanceTicks(1);
            Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
            return pipeline.LastStateHash;
        }
        Assert.Equal(Hash(10), Hash(10));
        Assert.NotEqual(Hash(10), Hash(20));
    }

    [Fact]
    public void InvalidStrategyInputsFailBeforeWorldWrites()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldMotionCommand(WorldMotionKind.Random));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldMotionCommand(WorldMotionKind.Jump, durationTicks: 21));
        Assert.Throws<ArgumentException>(() =>
            new WorldMotionCommand(WorldMotionKind.Chase));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldMotionCommand(WorldMotionKind.Waypoint, points: []));
        var (world, executor) = Start();
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, Id(), WorldSpeedModel.SpeedStat, 101m);
            Assert.Equal(WorldMotionResult.InvalidSpeed,
                world.StartMotion(ctx, Id(), new WorldMotionCommand(WorldMotionKind.Point,
                    goal: new Position(2.5, .5, 0)), new Tick(0), 12, out _));
        });
    }
}
