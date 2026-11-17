using System.Diagnostics;
using System.Runtime.InteropServices;
using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

Check("p60f-external-localtest-and-forged-actor-denied", SecurityAndSubject);
Check("ordered-intent-cross-cell-keeps-canonical-spatial-consistent", OrderedSpatial);
Check("p25-speed-modifier-and-p26-control-movement-gates", SpeedAndControl);
Check("old-session-generation-tick-and-teleport-rejected", ReplayAndLifecycle);
Check("p19-same-tick-source-sensitive-movement-digest", DeterministicDigest);
Check("seeded-real-executor-movement-baseline-recorded", MovementBenchmark);
Console.WriteLine("P27_MOVEMENT_INTEGRATION_PASS");

static void Check(string name, Action test)
{
    test();
    Console.WriteLine("PASS " + name);
}

static void Require(bool okay, string reason)
{
    if (!okay) throw new InvalidOperationException(reason);
}

static (WorldInstance World, WorldExecutor Executor) Start()
{
    var world = new WorldInstance(new WorldId(4), new WorldEpoch(2),
        new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 256, 32, 4);
    Run(executor, ctx =>
    {
        Require(world.BeginLoading(ctx) && world.Start(ctx), "WorldExecutor startup rejected");
    });
    return (world, executor);
}

static EntityIdentity Id(ulong id = 1, ulong generation = 1) =>
    new(new WorldId(4), new WorldEpoch(2),
        new EntityId(id), new EntityGeneration(generation));

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "trusted dispatch not accepted");
    var result = executor.RunBatch(1);
    Require(result.Status == WorldRunStatus.Completed && result.Executed == 1,
        "real P18 WorldExecutor did not complete action");
}

static ulong SpawnAndBind(WorldInstance world, WorldExecutor executor,
    EntityIdentity identity, double start, string subject = "alice")
{
    ulong version = 0;
    Run(executor, ctx =>
    {
        Require(world.Spawn(ctx, new WorldEntitySeed(identity,
            new Transform(new Position(start, 0, 0), new Angle(0)))) ==
            WorldEntityResult.Spawned, "real P17 entity spawn failed");
        Require(world.SetBaseAttribute(ctx, identity, WorldSpeedModel.SpeedStat, 10m) ==
            WorldEntityResult.AttributeChanged, "P25 speed stat failed");
        Require(world.BindMovementSubject(ctx, identity, BoundIdentity.CreateLocalTest(subject), new Tick(0),
            new Duration(50), out version) == WorldEntityResult.MovementBound,
            "movement subject session was not bound");
    });
    return version;
}

static WorldMovementIntent Intent(EntityIdentity identity, string actor,
    ulong version, ulong sequence, ulong tick, double x) =>
    new(identity, BoundIdentity.CreateLocalTest(actor), version, sequence, new Tick(tick),
        new Transform(new Position(x, 0, 0), new Angle(0)));

static void SecurityAndSubject()
{
    var (world, executor) = Start();
    var identity = Id();
    var version = SpawnAndBind(world, executor, identity, 31.75);
    // P60.F 本地测试身份不能被冒用为生产认证主体；无真实生产签发者时外部默认拒绝。
    Require(executor.OfferExternal(BoundIdentity.CreateLocalTest("alice"),
        new Work(_ => throw new InvalidOperationException("external untrusted command ran"))) ==
        WorldAdmissionStatus.Denied, "P60.F default deny failed");
    Require(executor.OfferExternal(null,
        new Work(_ => throw new InvalidOperationException("null identity ran"))) ==
        WorldAdmissionStatus.Denied, "anonymous external world action accepted");
    Run(executor, ctx =>
    {
        Require(world.SubmitMovement(ctx, Intent(identity, "mallory", version, 1, 1, 32.1)) ==
            WorldEntityResult.MovementSubjectMismatch, "forged actor was accepted");
        Require(world.SubmitMovement(ctx, Intent(identity, "alice", version + 1, 1, 1, 32.1)) ==
            WorldEntityResult.MovementSubjectMismatch, "forged session version was accepted");
    });
    Require(world.TryGetMovement(identity, out var state) && state.LastSequence == 0,
        "forged input unexpectedly consumed movement baseline");
}

static void OrderedSpatial()
{
    var (world, executor) = Start();
    var entity = Id();
    var version = SpawnAndBind(world, executor, entity, 31.75);
    Run(executor, ctx =>
    {
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 1, 1, 32.25)) ==
            WorldEntityResult.MovementAccepted, "valid movement rejected");
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 1, 2, 32.4)) ==
            WorldEntityResult.MovementOutOfOrder, "replayed sequence accepted");
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 2, 1, 32.4)) ==
            WorldEntityResult.MovementOutOfOrder, "old tick accepted");
    });
    Require(world.QueryNearby(new Position(31.75, 0, 0), default).Count == 0,
        "old Spatial cell still had moved entity");
    var current = world.QueryNearby(new Position(32.25, 0, 0), default);
    Require(current.Count == 1 && current[0].Identity == entity &&
        current[0].Transform.Position == new Position(32.25, 0, 0) &&
        current[0].Movement.LastSequence == 1,
        "canonical position and spatial index diverged");
    Require(world.TryGetMovement(entity, out var state) && state.Mode == WorldMovementMode.Moving &&
        state.LastTick == new Tick(1), "wrong accepted movement baseline");
}

static void SpeedAndControl()
{
    var (world, executor) = Start();
    var entity = Id();
    var version = SpawnAndBind(world, executor, entity, 0);
    AttributeModifierHandle bonus = default;
    EntityEffectHandle root = default;
    Run(executor, ctx =>
    {
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 1, 1, 0.75)) ==
            WorldEntityResult.MovementSpeedExceeded, "unmodified speed budget bypassed");
        Require(world.GrantAttributeModifier(ctx, entity, new AttributeModifierSource(1),
            WorldSpeedModel.SpeedStat, AttributeModifierOperation.Flat, 10, 0, out bonus) ==
            WorldEntityResult.ModifierAdded, "P25 speed modifier unavailable");
        Require(world.GrantControl(ctx, entity, new EntityEffectSource(1),
            EntityControlEffects.Rooted, out root) == WorldEntityResult.ControlGranted,
            "P26 root control unavailable");
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 1, 1, 0.75)) ==
            WorldEntityResult.MovementControlled, "rooted actor moved");
        Require(world.RevokeControl(ctx, entity, root) == WorldEntityResult.ControlRevoked,
            "root revoke failed");
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 1, 1, 0.75)) ==
            WorldEntityResult.MovementAccepted, "speed bonus didn't alter movement budget");
        Require(world.RevokeAttributeModifier(ctx, entity, bonus) ==
            WorldEntityResult.ModifierRemoved, "P25 modifier revoke failed");
        Require(world.SubmitMovement(ctx, Intent(entity, "alice", version, 2, 2, 1.5)) ==
            WorldEntityResult.MovementSpeedExceeded, "reverted speed was not enforced");
    });
    Require(world.TryGetMovement(entity, out var snap) &&
        snap.EffectiveSpeed == 10m && snap.LastSequence == 1,
        "speed state or failed input sequence changed");
}

static void ReplayAndLifecycle()
{
    var (world, executor) = Start();
    var old = Id();
    var oldSession = SpawnAndBind(world, executor, old, 5);
    IWorldWriteContext? expired = null;
    Run(executor, ctx =>
    {
        Require(world.Relocate(ctx, old, new Transform(new Position(8, 0, 0), new Angle(0))) ==
            WorldEntityResult.Relocated, "trusted relocation failed");
        Require(world.SubmitMovement(ctx, Intent(old, "alice", oldSession, 1, 1, 8.5)) ==
            WorldEntityResult.MovementUnbound, "teleport failed to invalidate old input");
        Require(world.Remove(ctx, old) == WorldEntityResult.Removed, "old generation not retired");
        Require(world.Spawn(ctx, new WorldEntitySeed(Id(1, 2),
            new Transform(new Position(2, 0, 0), new Angle(0)))) ==
            WorldEntityResult.Spawned, "replacement spawn failed");
        world.SetBaseAttribute(ctx, Id(1, 2), WorldSpeedModel.SpeedStat, 10m);
        Require(world.SubmitMovement(ctx, Intent(old, "alice", oldSession, 1, 1, 8.5)) ==
            WorldEntityResult.Stale, "retired generation input accepted");
        expired = ctx;
    });
    try
    {
        world.SubmitMovement(expired!, Intent(Id(1, 2), "alice", oldSession, 1, 1, 2.5));
        throw new InvalidOperationException("expired world lease unexpectedly submitted a command");
    }
    catch (WorldAccessException ex) when (ex.Fault == WorldAccessFault.LeaseRequired)
    {
        // 当前真实 P18 租约已过期，不能由攻击者留存重用。
    }
}

static void DeterministicDigest()
{
    static string Hash(string actor, ulong sequence)
    {
        var (world, executor) = Start();
        var entity = Id();
        var version = SpawnAndBind(world, executor, entity, 2, actor);
        Run(executor, ctx => Require(world.SubmitMovement(ctx, Intent(entity,
            actor, version, sequence, 1, 2)) == WorldEntityResult.MovementAccepted,
            "valid idle input rejected"));
        var clock = new FakeClock(new Duration(50),
            new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new WorldTickPipeline(world, clock, []);
        clock.AdvanceTicks(1);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
        return pipeline.LastStateHash;
    }
    Require(Hash("alice", 1) == Hash("alice", 1), "same capture not reproducible");
    Require(Hash("alice", 1) != Hash("bob", 1), "bound subject absent from digest");
    Require(Hash("alice", 1) != Hash("alice", 2), "accepted sequence absent from digest");
}

static void MovementBenchmark()
{
    const int entities = 100;
    const int ticks = 5;
    var (world, executor) = Start();
    var tokens = new ulong[entities];
    var ids = new EntityIdentity[entities];
    Run(executor, ctx =>
    {
        for (var i = 0; i < entities; i++)
        {
            var id = Id((ulong)i + 1);
            ids[i] = id;
            Require(world.Spawn(ctx, new WorldEntitySeed(id,
                new Transform(new Position(i * 2, 0, 0), new Angle(0)))) ==
                WorldEntityResult.Spawned, "benchmark entity spawn failed");
            world.SetBaseAttribute(ctx, id, WorldSpeedModel.SpeedStat, 10m);
            Require(world.BindMovementSubject(ctx, id, BoundIdentity.CreateLocalTest("bench"), new Tick(0),
                new Duration(50), out tokens[i]) == WorldEntityResult.MovementBound,
                "benchmark subject binding rejected");
        }
    });
    var measurements = new double[entities * ticks];
    var cursor = 0;
    Run(executor, ctx =>
    {
        for (ulong tick = 1; tick <= ticks; tick++)
        {
            for (var i = 0; i < entities; i++)
            {
                var intent = Intent(ids[i], "bench", tokens[i], tick, tick, i * 2 + tick * 0.25);
                var started = Stopwatch.GetTimestamp();
                var result = world.SubmitMovement(ctx, intent);
                measurements[cursor++] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Require(result == WorldEntityResult.MovementAccepted, "benchmark movement rejected");
            }
        }
    });
    Array.Sort(measurements);
    static double Percentile(double[] samples, double percent)
    {
        var index = Math.Min(samples.Length - 1, (int)Math.Ceiling((samples.Length - 1) * percent));
        return samples[index];
    }
    Console.WriteLine($"BASELINE P27 world_move entities={entities} moves={measurements.Length} " +
        $"p50={Percentile(measurements, .5):F6}ms p95={Percentile(measurements, .95):F6}ms " +
        $"p99={Percentile(measurements, .99):F6}ms os={RuntimeInformation.OSDescription} " +
        $"cpu={Environment.ProcessorCount} runtime={Environment.Version} " +
        $"mode=Release ticks={ticks} tickDurationMs=50 threshold=none");
    Require(world.SpatialIndexedCount == entities, "benchmark moved entity out of spatial registry");
}

sealed class Work(Action<IWorldWriteContext> callback) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => callback(context);
}
