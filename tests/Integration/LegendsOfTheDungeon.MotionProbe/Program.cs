using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.Environment;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

Check("p28-all-ten-state-machines-advance-real-worldexecutor", AllStrategies);
Check("p28-p23-real-detour-and-no-path-never-cross-wall", DetourAndNoPath);
Check("p28-cancel-and-priority-reject-stale-p23-replies", PriorityAndCancel);
Check("p28-target-retire-replan-expiry-and-timeout", LifecycleAndTimeout);
Check("p28-p25-speed-p26-control-and-p27-session-isolation", SpeedControlAndInput);
Check("p28-p19-fixed-tick-replay-motion-hash", TickAndReplay);
Console.WriteLine("P28_SERVER_MOTION_INTEGRATION_PASS");

static void Check(string label, Action action)
{
    action();
    Console.WriteLine("PASS " + label);
}

static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static EntityIdentity Id(ulong number = 1, ulong generation = 1) =>
    new(new WorldId(31), new WorldEpoch(2),
        new EntityId(number), new EntityGeneration(generation));

static (WorldInstance World, WorldExecutor Executor) Start()
{
    var world = new WorldInstance(new WorldId(31), new WorldEpoch(2),
        new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
    Run(executor, ctx =>
    {
        Require(world.BeginLoading(ctx) && world.Start(ctx), "P17/P18 World failed to start");
        Spawn(world, ctx, Id(), new Position(.5, .5, 0));
    });
    return (world, executor);
}

static void Spawn(WorldInstance world, IWorldWriteContext context,
    EntityIdentity identity, Position start)
{
    Require(world.Spawn(context, new WorldEntitySeed(identity,
        new Transform(start, new Angle(0)))) == WorldEntityResult.Spawned,
        "P17 canonical entity spawn failed");
    Require(world.SetBaseAttribute(context, identity, WorldSpeedModel.SpeedStat, 20m) ==
        WorldEntityResult.AttributeChanged, "P25 speed baseline failed");
}

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "P18 trusted producer rejected");
    var report = executor.RunBatch(1);
    Require(report.Status == WorldRunStatus.Completed && report.Executed == 1,
        "P18 real synchronous world scope did not complete");
}

static INavigationQuery Navigation(IEnumerable<PlaneCell>? blocked = null) =>
    new PlanarGridEnvironment(-9, -9, 9, 9, 1, 0, blocked ?? [], 27);

static WorldMotionPathRequest? Begin(WorldInstance world, WorldExecutor executor,
    WorldMotionCommand command, ulong tick = 0)
{
    WorldMotionPathRequest? result = null;
    Run(executor, ctx => Require(world.StartMotion(ctx, Id(), command,
        new Tick(tick), 27, out result) == WorldMotionResult.Started, "P28 BeginMotion rejected"));
    return result;
}

static void Apply(WorldInstance world, WorldExecutor executor,
    WorldMotionPathRequest request, INavigationQuery navigation,
    WorldMotionResult expected = WorldMotionResult.PathAccepted)
{
    // 查询在世界写入 Scope 外，只有带版本回执进入真实 P18 世界执行器才可提交。
    var result = navigation.FindPath(request.Request);
    Run(executor, ctx => Require(world.ApplyMotionPath(ctx, request, result) == expected,
        "P23 versioned motion path result was not handled correctly"));
}

static WorldMotionSnapshot Finish(WorldInstance world, WorldExecutor executor,
    INavigationQuery navigation, uint maximumTicks = 190)
{
    for (ulong i = 1; i <= maximumTicks; i++)
    {
        if (world.TryGetPendingMotionPath(Id(), out var pending))
            Apply(world, executor, pending, navigation);
        Run(executor, ctx => world.AdvanceMotion(ctx, Id(), new Tick(i), new Duration(50)));
        Require(world.TryGetMotion(Id(), out var state), "P28 live motion disappeared");
        if (state.State is not (WorldMotionState.Active or WorldMotionState.AwaitingPath))
            return state;
    }
    throw new InvalidOperationException("P28 bounded strategy did not terminate");
}

static void AllStrategies()
{
    foreach (var kind in Enum.GetValues<WorldMotionKind>())
    {
        var (world, executor) = Start();
        if (kind is WorldMotionKind.Chase or WorldMotionKind.Follow or WorldMotionKind.Flee)
            Run(executor, ctx => Spawn(world, ctx, Id(2), new Position(4.5, .5, 0)));
        if (kind == WorldMotionKind.Home)
            Run(executor, ctx => Require(world.Relocate(ctx, Id(),
                new Transform(new Position(2.5, .5, 0), new Angle(0))) ==
                WorldEntityResult.Relocated, "Home setup relocate failed"));
        var command = kind switch
        {
            WorldMotionKind.Random => new WorldMotionCommand(kind, seed: 193),
            WorldMotionKind.Chase or WorldMotionKind.Follow or WorldMotionKind.Flee =>
                new WorldMotionCommand(kind, target: Id(2)),
            WorldMotionKind.Waypoint or WorldMotionKind.Spline =>
                new WorldMotionCommand(kind, points:
                    [new Position(1.5, .5, 0), new Position(2.5, 1.5, 0)]),
            WorldMotionKind.Jump or WorldMotionKind.Knockback =>
                new WorldMotionCommand(kind, priority: 80,
                    goal: new Position(2.5, .5, 0), durationTicks: 4),
            WorldMotionKind.Home => new WorldMotionCommand(kind),
            _ => new WorldMotionCommand(kind, goal: new Position(3.5, .5, 0))
        };
        var requested = Begin(world, executor, command);
        if (requested is { } pending) Apply(world, executor, pending, Navigation());
        var status = Finish(world, executor, Navigation());
        Require(status.Kind == kind && status.State == WorldMotionState.Completed,
            "strategy failed to reach Completed: " + kind);
        Require(world.TryGetSnapshot(Id().Id, out var snapshot) &&
            snapshot.Motion == status, "canonical server motion snapshot not synchronized");
        Require(world.QueryNearby(snapshot.Transform.Position, default).Count == 1,
            "moving entity missing from P20 spatial index: " + kind);
        Console.WriteLine("MODE PASS " + kind);
    }
}

static void DetourAndNoPath()
{
    var obstacles = new[] { new PlaneCell(1, 0) };
    var nav = Navigation(obstacles);
    var (world, executor) = Start();
    var ticket = Begin(world, executor,
        new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(3.5, .5, 0)));
    Require(ticket.HasValue, "Point did not request P23 path");
    var path = nav.FindPath(ticket.GetValueOrDefault().Request);
    Require(path.Status == EnvironmentQueryStatus.Success && path.Waypoints.Count > 4,
        "P23 path unexpectedly skipped detour");
    var collision = new PlanarGridEnvironment(-9, -9, 9, 9, 1, 0, obstacles, 27);
    for (var i = 1; i < path.Waypoints.Count; i++)
    {
        var segment = collision.TraceSegment(path.Waypoints[i - 1], path.Waypoints[i]);
        Require(segment.Status == EnvironmentQueryStatus.Success && !segment.IsBlocked,
            "P28 approved a P23 path crossing a solid cell");
    }
    Apply(world, executor, ticket.GetValueOrDefault(), nav);
    Require(Finish(world, executor, nav).State == WorldMotionState.Completed,
        "P28 detour did not finish");
    Require(world.TryGetSnapshot(Id().Id, out var done) &&
        done.Transform.Position == new Position(3.5, .5, 0), "detour destination was incorrect");

    var closedWall = Enumerable.Range(-9, 19).Select(y => new PlaneCell(1, y));
    var (sealedWorld, sealedExecutor) = Start();
    var noPath = Begin(sealedWorld, sealedExecutor,
        new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(3.5, .5, 0)));
    Require(noPath.HasValue, "NoPath test had no request");
    Apply(sealedWorld, sealedExecutor, noPath.GetValueOrDefault(),
        Navigation(closedWall), WorldMotionResult.NoPath);
    Require(sealedWorld.TryGetSnapshot(Id().Id, out var blocked) &&
        blocked.Motion.State == WorldMotionState.NoPath &&
        blocked.Transform.Position == new Position(.5, .5, 0),
        "NoPath silently allowed a straight-line teleport");
}

static void PriorityAndCancel()
{
    var (world, executor) = Start();
    var earlier = Begin(world, executor,
        new WorldMotionCommand(WorldMotionKind.Point,
            priority: 20, goal: new Position(3.5, .5, 0)));
    Require(earlier.HasValue, "first request missing");
    Run(executor, ctx =>
    {
        Require(world.StartMotion(ctx, Id(), new WorldMotionCommand(
            WorldMotionKind.Home, priority: 1), new Tick(0), 27, out _) ==
            WorldMotionResult.PriorityRejected, "low priority unexpectedly preempted");
        Require(world.StartMotion(ctx, Id(), new WorldMotionCommand(
            WorldMotionKind.Point, priority: 60,
            goal: new Position(1.5, .5, 0)), new Tick(0), 27, out _) ==
            WorldMotionResult.Started, "high priority could not preempt");
    });
    Require(world.TryGetPendingMotionPath(Id(), out var latest) &&
        latest.Request.RequestVersion > earlier.GetValueOrDefault().Request.RequestVersion,
        "motion preemption did not advance request generation");
    Apply(world, executor, earlier.GetValueOrDefault(), Navigation(), WorldMotionResult.Stale);
    Run(executor, ctx =>
    {
        Require(world.CancelMotion(ctx, Id(), earlier.GetValueOrDefault().MotionVersion) ==
            WorldMotionResult.Stale, "stale cancel was accepted");
        Require(world.CancelMotion(ctx, Id(), latest.MotionVersion) ==
            WorldMotionResult.Cancelled, "current motion cancel failed");
    });
    Apply(world, executor, latest, Navigation(), WorldMotionResult.Stale);
    Require(world.TryGetSnapshot(Id().Id, out var idle) &&
        idle.Transform.Position == new Position(.5, .5, 0) &&
        idle.Motion.State == WorldMotionState.Cancelled, "cancelled motion moved anyway");
}

static void LifecycleAndTimeout()
{
    var (world, executor) = Start();
    Run(executor, ctx => Spawn(world, ctx, Id(2), new Position(3.5, .5, 0)));
    var original = Begin(world, executor,
        new WorldMotionCommand(WorldMotionKind.Chase, target: Id(2)));
    Require(original.HasValue, "Chase request missing");
    Apply(world, executor, original.GetValueOrDefault(), Navigation());
    Run(executor, ctx =>
    {
        Require(world.Relocate(ctx, Id(2), new Transform(
            new Position(4.5, .5, 0), new Angle(0))) ==
            WorldEntityResult.Relocated, "target relocation setup failed");
        Require(world.AdvanceMotion(ctx, Id(), new Tick(1), new Duration(50)) ==
            WorldMotionResult.WaitingForPath, "target motion did not create a new path request");
    });
    Require(world.TryGetPendingMotionPath(Id(), out var replacement) &&
        replacement.Request.RequestVersion > original.GetValueOrDefault().Request.RequestVersion,
        "target movement did not increment request version");
    Apply(world, executor, original.GetValueOrDefault(), Navigation(), WorldMotionResult.Stale);
    Run(executor, ctx => Require(world.Remove(ctx, Id(2)) ==
        WorldEntityResult.Removed, "target retirement failed"));
    Apply(world, executor, replacement, Navigation(), WorldMotionResult.Stale);
    Require(world.TryGetMotion(Id(), out var stopped) &&
        stopped.State == WorldMotionState.TargetLost, "despawned target retained live chase");

    var (timed, timedExecutor) = Start();
    var ticket = Begin(timed, timedExecutor,
        new WorldMotionCommand(WorldMotionKind.Point,
            goal: new Position(4.5, .5, 0), deadline: new Tick(2)));
    Require(ticket.HasValue, "deadline Point did not plan");
    Run(timedExecutor, ctx =>
    {
        Require(timed.AdvanceMotion(ctx, Id(), new Tick(1), new Duration(50)) ==
            WorldMotionResult.WaitingForPath, "pending path not kept");
        Require(timed.AdvanceMotion(ctx, Id(), new Tick(2), new Duration(50)) ==
            WorldMotionResult.TimedOut, "logical deadline did not end pending motion");
    });
    Apply(timed, timedExecutor, ticket.GetValueOrDefault(), Navigation(), WorldMotionResult.Stale);
}

static void SpeedControlAndInput()
{
    var (world, executor) = Start();
    var ticket = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
        goal: new Position(3.5, .5, 0)));
    Require(ticket.HasValue, "speed test missing path");
    Apply(world, executor, ticket.GetValueOrDefault(), Navigation());
    EntityEffectHandle rooted = default;
    Run(executor, ctx =>
    {
        Require(world.GrantControl(ctx, Id(), new EntityEffectSource(1),
            EntityControlEffects.Rooted, out rooted) == WorldEntityResult.ControlGranted,
            "P26 Rooted grant failed");
        Require(world.AdvanceMotion(ctx, Id(), new Tick(1), new Duration(50)) ==
            WorldMotionResult.Paused, "P26 root did not pause path");
        Require(world.RevokeControl(ctx, Id(), rooted) == WorldEntityResult.ControlRevoked,
            "root revoke failed");
        Require(world.SetBaseAttribute(ctx, Id(), WorldSpeedModel.SpeedStat, 0m) ==
            WorldEntityResult.AttributeChanged, "P25 zero speed failed");
        Require(world.AdvanceMotion(ctx, Id(), new Tick(2), new Duration(50)) ==
            WorldMotionResult.Paused, "zero speed moved path");
        world.SetBaseAttribute(ctx, Id(), WorldSpeedModel.SpeedStat, 20m);
        Require(world.AdvanceMotion(ctx, Id(), new Tick(3), new Duration(50)) ==
            WorldMotionResult.Advanced, "restored speed could not advance path");
    });
    ulong session = 0;
    Run(executor, ctx =>
    {
        Require(world.BindMovementSubject(ctx, Id(), BoundIdentity.CreateLocalTest("alice"),
            new Tick(3), new Duration(50), out session) == WorldEntityResult.MovementBound,
            "P27 player session bind failed");
    });
    Require(world.TryGetMotion(Id(), out var snap) &&
        snap.State == WorldMotionState.Cancelled, "P27 input claim failed to cancel server motion");
    Require(world.TryGetMovement(Id(), out var movement) &&
        movement.Bound && movement.SessionVersion == session,
        "P27 authenticated movement session no longer bound");
}

static void TickAndReplay()
{
    static string Hash(int priority)
    {
        var (world, executor) = Start();
        var ticket = Begin(world, executor, new WorldMotionCommand(WorldMotionKind.Point,
            priority: priority, goal: new Position(2.5, .5, 0)));
        Require(ticket.HasValue, "replay did not produce P23 path request");
        Apply(world, executor, ticket.GetValueOrDefault(), Navigation());
        var clock = new FakeClock(new Duration(50),
            new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new WorldTickPipeline(world, clock, [new WorldMotionTickStep(new Duration(50))]);
        clock.AdvanceTicks(1);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
        Require(world.TryGetSnapshot(Id().Id, out var zeroSegment) &&
            zeroSegment.Motion.PathIndex == 2 &&
            zeroSegment.Transform.Position == new Position(.5, .5, 0),
            "P23 initial zero-length segment should advance path index without moving");
        clock.AdvanceTicks(1);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(2)));
        Require(world.TryGetSnapshot(Id().Id, out var snapshot) &&
            snapshot.Motion.LastTick == new Tick(2) &&
            snapshot.Transform.Position != new Position(.5, .5, 0),
            "P19 Simulation phase did not actually advance Motion");
        return pipeline.LastStateHash;
    }
    Require(Hash(10) == Hash(10), "same capture replay drift");
    Require(Hash(10) != Hash(20), "motion priority missing from schema v6 digest");
}

sealed class Work(Action<IWorldWriteContext> operation) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => operation(context);
}
