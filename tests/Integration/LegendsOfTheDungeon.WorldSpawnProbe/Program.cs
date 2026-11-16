using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

Check("duplicate-grid-load-and-FakeClock-respawn-generation", LoadingAndTimers);
Check("linked-respawn-cycle-and-topological-parent-first", LinkedRespawn);
Check("unloaded-cell-and-dynamic-one-shot", GridAndDynamic);
Check("crash-checkpoint-confirmed-identity-never-spawns-duplicate", CrashRecovery);
Check("world-tick-phase-source-is-deterministic", Replay);
Console.WriteLine("P22_WORLD_SPAWN_INTEGRATION_PASS");

static void Check(string name, Action check)
{
    check();
    Console.WriteLine("PASS " + name);
}

static void Require(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException(detail);
}

static WorldSpawnKey Key(ulong value) => new(value);
static WorldSpawnDefinition Def(ulong key, ulong entity, double x = 1, long delay = 100,
    WorldSpawnKind kind = WorldSpawnKind.Static) =>
    new(Key(key), new EntityId(entity),
        new Transform(new Position(x, 0, 0), new Angle(0)),
        new Duration(delay), kind);

static (WorldInstance World, WorldExecutor Executor, FakeClock Clock,
    WorldSpawnRegistry Registry) Create(ulong tick = 0)
{
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
        new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 256, 32, 2);
    var clock = new FakeClock(new Duration(50),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new Tick(tick), new Duration((long)tick * 50));
    Run(executor, ctx => Require(world.BeginLoading(ctx) && world.Start(ctx),
        "world start failed"));
    return (world, executor, clock, new WorldSpawnRegistry(world, clock));
}

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "WorldExecutor rejected trusted Spawn action");
    Require(executor.RunBatch(1).Status == WorldRunStatus.Completed,
        "WorldExecutor action did not complete");
}

static IReadOnlyList<WorldSpawnActivation> Step(WorldExecutor executor,
    FakeClock clock, WorldSpawnRegistry registry)
{
    clock.AdvanceTicks(1);
    IReadOnlyList<WorldSpawnActivation>? result = null;
    Run(executor, ctx => result = registry.Advance(ctx, clock.CurrentTick));
    return result ?? throw new InvalidOperationException("Spawn Timers step did not execute");
}

static void Init(WorldExecutor executor, WorldSpawnRegistry registry,
    params WorldSpawnDefinition[] defs)
{
    Run(executor, ctx =>
    {
        foreach (var def in defs)
        {
            Require(registry.RegisterStatic(ctx, def) == WorldSpawnResult.Registered,
                "register failed");
            Require(registry.LoadGrid(ctx, new SpatialCellKey(
                (int)Math.Floor(def.Transform.Position.X / 32), 0, 0)) is
                SpatialCellResult.Activated or SpatialCellResult.AlreadyCompleted,
                "Grid activate failed");
        }
    });
}

static void LoadingAndTimers()
{
    var (world, executor, clock, registry) = Create();
    var definition = Def(1, 100);
    Init(executor, registry, definition);
    var original = Step(executor, clock, registry).Single();
    for (var i = 0; i < 6; i++)
    {
        Run(executor, ctx =>
        {
            Require(registry.LoadGrid(ctx, new SpatialCellKey(0, 0, 0)) ==
                SpatialCellResult.AlreadyCompleted, "duplicate load not idempotent");
            Require(registry.RegisterStatic(ctx, definition) ==
                WorldSpawnResult.AlreadyCompleted, "duplicate static register changed state");
        });
        Require(Step(executor, clock, registry).Count == 0, "reloaded twice");
    }
    Run(executor, ctx => Require(registry.Despawn(ctx, Key(1)) ==
        WorldSpawnResult.Despawned, "despawn failed"));
    Require(Step(executor, clock, registry).Count == 0, "respawn before timer");
    var newIdentity = Step(executor, clock, registry).Single().Identity;
    Require(newIdentity.Id == original.Identity.Id &&
        newIdentity.Generation.Value == 2 &&
        world.LiveCount == 1, "new generation or single entity count wrong");
    Require(Step(executor, clock, registry).Count == 0, "duplicate respawn");
}

static void LinkedRespawn()
{
    var (_, executor, clock, registry) = Create();
    Init(executor, registry, Def(1, 10), Def(2, 20), Def(3, 30));
    Run(executor, ctx =>
    {
        Require(registry.SetLinkedRespawn(ctx, Key(1), Key(2)) ==
            WorldSpawnResult.LinkChanged, "parent link failed");
        Require(registry.SetLinkedRespawn(ctx, Key(2), Key(3)) ==
            WorldSpawnResult.LinkChanged, "grandparent link failed");
        Require(registry.SetLinkedRespawn(ctx, Key(3), Key(1)) ==
            WorldSpawnResult.LinkCycle, "cycle incorrectly accepted");
    });
    Require(Step(executor, clock, registry).Select(x => x.Key)
        .SequenceEqual([Key(3), Key(2), Key(1)]),
        "parent-first deterministic linked order wrong");
    Run(executor, ctx =>
    {
        Require(registry.Despawn(ctx, Key(3)) == WorldSpawnResult.Despawned,
            "parent retire failed");
        Require(registry.Despawn(ctx, Key(2)) == WorldSpawnResult.Despawned,
            "linked child retire failed");
    });
    Require(Step(executor, clock, registry).Count == 0, "timer not yet due");
    Require(Step(executor, clock, registry).Select(x => x.Key)
        .SequenceEqual([Key(3), Key(2)]), "linked restart not parent-first");
}

static void GridAndDynamic()
{
    var (world, executor, clock, registry) = Create();
    Init(executor, registry, Def(1, 10, x: -33));
    Require(Step(executor, clock, registry).Count == 1, "static initial spawn missing");
    var cell = new SpatialCellKey(-2, 0, 0);
    Run(executor, ctx => Require(registry.UnloadGrid(ctx, cell) ==
        SpatialCellResult.Unloaded, "Grid unload failed"));
    Require(world.LiveCount == 0, "unload retained entity");
    for (var i = 0; i < 4; i++)
    {
        Require(Step(executor, clock, registry).Count == 0, "unloaded Grid respawned");
    }
    Run(executor, ctx => Require(registry.SpawnDynamic(ctx,
        Def(2, 20, x: -33, kind: WorldSpawnKind.Dynamic)) ==
        WorldSpawnResult.GridUnavailable, "dynamic spawn bypassed unloaded Grid"));
    Run(executor, ctx => Require(registry.LoadGrid(ctx, cell) ==
        SpatialCellResult.Activated, "Grid reload failed"));
    Require(Step(executor, clock, registry).Count == 1, "static missed reload");
    Run(executor, ctx =>
    {
        Require(registry.SpawnDynamic(ctx, Def(2, 20, x: -33,
            kind: WorldSpawnKind.Dynamic)) == WorldSpawnResult.Spawned,
            "explicit dynamic spawn not admitted");
        Require(registry.Despawn(ctx, Key(2)) == WorldSpawnResult.Despawned,
            "dynamic despawn failed");
    });
    Require(Step(executor, clock, registry).Count == 0, "one-shot dynamic resurrected");
    Require(world.LiveCount == 1, "static survivor missing");
}

static void CrashRecovery()
{
    var (world, executor, clock, registry) = Create();
    Init(executor, registry, Def(1, 10));
    var confirmed = Step(executor, clock, registry).Single().Identity;
    WorldSpawnCheckpoint? checkpoint = null;
    Run(executor, ctx => checkpoint = registry.Capture(ctx));
    var (restoredWorld, restoredExecutor, restoredClock, _) = Create(tick: 1);
    WorldSpawnRegistry? restored = null;
    Run(restoredExecutor, ctx =>
    {
        restored = WorldSpawnRegistry.Restore(ctx, restoredWorld, restoredClock, checkpoint!);
        Require(restored.LoadGrid(ctx, new SpatialCellKey(0, 0, 0)) ==
            SpatialCellResult.Activated, "reloaded world Cell failed");
    });
    Require(restored!.TryGet(Key(1), out var record) &&
        record.Status == WorldSpawnStatus.RecoveryHold,
        "confirmed instance without canonical restore was not held");
    for (var i = 0; i < 8; i++)
    {
        Require(Step(restoredExecutor, restoredClock, restored).Count == 0,
            "crash recovery silently spawned unconfirmed duplicate");
    }
    Require(restoredWorld.LiveCount == 0, "recovery unexpectedly mutated canonical world");
    Run(restoredExecutor, ctx =>
    {
        Require(restored.ConfirmRecovered(ctx, Key(1), confirmed) ==
            WorldSpawnResult.RecoveryPending, "absent canonical identity was incorrectly confirmed");
        Require(restoredWorld.Spawn(ctx, new WorldEntitySeed(confirmed,
            Def(1, 10).Transform)) == WorldEntityResult.Spawned,
            "trusted world reconstruction failed");
        Require(restored.ConfirmRecovered(ctx, Key(1), confirmed) ==
            WorldSpawnResult.AlreadyCompleted, "exact recovered identity was not accepted");
    });
    Require(Step(restoredExecutor, restoredClock, restored).Count == 0 &&
        restoredWorld.LiveCount == 1, "recovered confirmed instance was duplicated");
}

static void Replay()
{
    static IReadOnlyList<string> Sequence()
    {
        var (world, executor, clock, registry) = Create();
        Init(executor, registry, Def(1, 10, delay: 50));
        var pipeline = new WorldTickPipeline(world, clock, [registry]);
        var hashes = new List<string>();
        for (var n = 1; n <= 4; n++)
        {
            clock.AdvanceTicks(1);
            Run(executor, ctx =>
            {
                if (n == 2)
                {
                    Require(registry.Despawn(ctx, Key(1)) == WorldSpawnResult.Despawned,
                        "respawn test retirement failed");
                }
                hashes.Add(pipeline.Execute(ctx, new Tick((ulong)n)).StateHash);
            });
        }
        return hashes;
    }
    var one = Sequence();
    var two = Sequence();
    Require(one.SequenceEqual(two) && one.Distinct().Count() >= 2 &&
        one.All(s => s.Length == 64), "P19 phase Spawn sequence not deterministic");
}

internal sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}
