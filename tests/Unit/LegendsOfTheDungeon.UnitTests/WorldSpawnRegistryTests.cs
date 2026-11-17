using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldSpawnRegistryTests
{
    private static readonly DateTimeOffset Utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (WorldInstance World, WorldExecutor Executor, FakeClock Clock,
        WorldSpawnRegistry Registry) Create(ulong initialTick = 0)
    {
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
            new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 256, 32, 2);
        var clock = new FakeClock(new Duration(50), Utc, new Tick(initialTick),
            new Duration(checked((long)initialTick * 50)));
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });
        return (world, executor, clock, new WorldSpawnRegistry(world, clock));
    }

    private static WorldSpawnKey Key(ulong id) => new(id);

    private static WorldSpawnDefinition Def(ulong key, ulong entity, double x = 1,
        long delay = 100, WorldSpawnKind kind = WorldSpawnKind.Static) =>
        new(Key(key), new EntityId(entity),
            new Transform(new Position(x, 0, 0), new Angle(0)),
            new Duration(delay), kind);

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(action)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    private static IReadOnlyList<WorldSpawnActivation> Step(WorldExecutor executor,
        FakeClock clock, WorldSpawnRegistry registry)
    {
        clock.AdvanceTicks(1);
        IReadOnlyList<WorldSpawnActivation>? created = null;
        Run(executor, ctx => created = registry.Advance(ctx, clock.CurrentTick));
        return Assert.IsAssignableFrom<IReadOnlyList<WorldSpawnActivation>>(created);
    }

    private static void RegisterAndLoad(WorldInstance world, WorldExecutor executor,
        WorldSpawnRegistry registry, WorldSpawnDefinition definition)
    {
        Run(executor, ctx =>
        {
            Assert.Equal(WorldSpawnResult.Registered,
                registry.RegisterStatic(ctx, definition));
            Assert.True(world.TryLocateCell(definition.Transform.Position, out var cell));
            Assert.True(registry.LoadGrid(ctx, cell) is SpatialCellResult.Activated
                or SpatialCellResult.AlreadyCompleted);
        });
    }

    [Fact]
    public void RegisterAndRepeatedGridLoadNeverDuplicateCanonicalEntity()
    {
        var (world, executor, clock, registry) = Create();
        var definition = Def(7, 100);
        RegisterAndLoad(world, executor, registry, definition);
        var first = Assert.Single(Step(executor, clock, registry));
        Assert.Equal(Key(7), first.Key);
        Assert.Equal(new EntityGeneration(1), first.Identity.Generation);
        for (var n = 0; n < 4; n++)
        {
            Run(executor, ctx =>
            {
                Assert.Equal(WorldSpawnResult.AlreadyCompleted,
                    registry.RegisterStatic(ctx, definition));
                Assert.True(registry.LoadGrid(ctx, new SpatialCellKey(0, 0, 0)) is
                    SpatialCellResult.AlreadyCompleted or SpatialCellResult.Activated);
            });
            Assert.Empty(Step(executor, clock, registry));
        }
        Assert.Equal(1, world.LiveCount);
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(Key(7), out var snapshot));
        Assert.Equal(WorldSpawnStatus.Active, snapshot.Status);
        Assert.Equal(first.Identity, snapshot.Current);
    }

    [Fact]
    public void SpawnRegistryRejectsConflictingKeysAndEntityIds()
    {
        var (_, executor, _, registry) = Create();
        Run(executor, ctx =>
        {
            Assert.Equal(WorldSpawnResult.Registered,
                registry.RegisterStatic(ctx, Def(1, 11)));
            Assert.Equal(WorldSpawnResult.AlreadyCompleted,
                registry.RegisterStatic(ctx, Def(1, 11)));
            Assert.Equal(WorldSpawnResult.Conflict,
                registry.RegisterStatic(ctx, Def(1, 12)));
            Assert.Equal(WorldSpawnResult.Conflict,
                registry.RegisterStatic(ctx, Def(2, 11)));
        });
        Assert.Equal(1, registry.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldSpawnKey(0));
    }

    [Fact]
    public void FakeClockDrivesExactOneShotRespawnAndGeneration()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 100));
        var first = Assert.Single(Step(executor, clock, registry));
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.Despawned,
            registry.Despawn(ctx, Key(1))));
        Assert.True(registry.TryGet(Key(1), out var waiting));
        Assert.Equal(new Tick(3), waiting.DueTick);
        Assert.Equal(WorldSpawnStatus.Waiting, waiting.Status);
        Assert.Empty(Step(executor, clock, registry)); // tick 2
        var second = Assert.Single(Step(executor, clock, registry)); // tick 3
        Assert.Equal(new EntityGeneration(2), second.Identity.Generation);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Empty(Step(executor, clock, registry));
        Assert.Equal(1, world.LiveCount);
        Assert.True(world.TryGetSnapshot(new EntityId(10), out var live));
        Assert.Equal(second.Identity, live.Identity);
        Assert.Equal(WorldEntityLifecycle.Active, live.Lifecycle);
    }

    [Fact]
    public void ZeroDelayDoesNotSpawnTwiceInSameLogicalTick()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 0));
        Assert.Single(Step(executor, clock, registry));
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.Despawned,
            registry.Despawn(ctx, Key(1))));
        Run(executor, ctx => Assert.Empty(registry.Advance(ctx, new Tick(1))));
        Assert.Equal(0, world.LiveCount);
        Assert.Single(Step(executor, clock, registry));
        Assert.Empty(Step(executor, clock, registry));
    }

    [Fact]
    public void GridInactiveDoesNotSpawnAndReloadIsIdempotent()
    {
        var (world, executor, clock, registry) = Create();
        var definition = Def(1, 20, x: -33);
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.Registered,
            registry.RegisterStatic(ctx, definition)));
        Assert.Empty(Step(executor, clock, registry));
        Assert.Equal(0, world.LiveCount);
        Run(executor, ctx =>
        {
            Assert.Equal(SpatialCellResult.Activated,
                registry.LoadGrid(ctx, new SpatialCellKey(-2, 0, 0)));
            Assert.Equal(SpatialCellResult.AlreadyCompleted,
                registry.LoadGrid(ctx, new SpatialCellKey(-2, 0, 0)));
        });
        Assert.Single(Step(executor, clock, registry));
        Assert.Equal(1, world.LiveCount);
    }

    [Fact]
    public void UnloadedGridDespawnsOwnedEntitiesAndDelaysRespawnUntilReactivated()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(2, 22, x: 33, delay: 100));
        Assert.Single(Step(executor, clock, registry)); // tick1
        var cell = new SpatialCellKey(1, 0, 0);
        Run(executor, ctx =>
        {
            Assert.Equal(SpatialCellResult.Unloaded, registry.UnloadGrid(ctx, cell));
            Assert.Equal(SpatialCellResult.AlreadyCompleted, registry.UnloadGrid(ctx, cell));
        });
        Assert.Equal(0, world.LiveCount);
        Assert.True(registry.TryGet(Key(2), out var waiting));
        Assert.Equal(WorldSpawnStatus.Waiting, waiting.Status);
        Assert.Empty(Step(executor, clock, registry)); // tick2
        Assert.Empty(Step(executor, clock, registry)); // tick3 due but Cell unloaded
        Assert.Empty(Step(executor, clock, registry));
        Run(executor, ctx => Assert.Equal(SpatialCellResult.Activated,
            registry.LoadGrid(ctx, cell)));
        var next = Assert.Single(Step(executor, clock, registry));
        Assert.Equal(new EntityGeneration(2), next.Identity.Generation);
        Assert.Equal(1, world.LiveCount);
    }

    [Fact]
    public void ForeignOccupantsBlockGridUnloadWithoutRetiringAnySpawn()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        Assert.Single(Step(executor, clock, registry));
        Run(executor, ctx =>
        {
            var id = new EntityIdentity(world.World, world.Epoch,
                new EntityId(20), new EntityGeneration(1));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx,
                new WorldEntitySeed(id, new Transform(new Position(2, 0, 0), new Angle(0)))));
            Assert.Equal(SpatialCellResult.Occupied,
                registry.UnloadGrid(ctx, new SpatialCellKey(0, 0, 0)));
        });
        Assert.Equal(2, world.LiveCount);
        Assert.True(registry.TryGet(Key(1), out var active));
        Assert.Equal(WorldSpawnStatus.Active, active.Status);
        Assert.Equal(WorldLifecycle.Running, world.State);
    }

    [Fact]
    public void DynamicSpawnIsOneShotAndRequiresExplicitlyLoadedCell()
    {
        var (world, executor, clock, registry) = Create();
        var dynamic = Def(10, 50, x: 35, delay: 0, kind: WorldSpawnKind.Dynamic);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldSpawnResult.GridUnavailable, registry.SpawnDynamic(ctx, dynamic));
            Assert.Equal(0, registry.Count);
            Assert.Equal(SpatialCellResult.Activated,
                registry.LoadGrid(ctx, new SpatialCellKey(1, 0, 0)));
            Assert.Equal(WorldSpawnResult.Spawned, registry.SpawnDynamic(ctx, dynamic));
            Assert.Equal(WorldSpawnResult.AlreadyCompleted,
                registry.SpawnDynamic(ctx, dynamic));
            Assert.Equal(WorldSpawnResult.Despawned, registry.Despawn(ctx, Key(10)));
            Assert.Equal(WorldSpawnResult.AlreadyCompleted, registry.Despawn(ctx, Key(10)));
        });
        for (var i = 0; i < 5; i++)
        {
            Assert.Empty(Step(executor, clock, registry));
        }
        Assert.True(registry.TryGet(Key(10), out var retired));
        Assert.Equal(WorldSpawnStatus.Retired, retired.Status);
        Assert.Equal(0, world.LiveCount);
    }

    [Fact]
    public void LinkedRespawnDetectsSelfAndMultiNodeCycles()
    {
        var (_, executor, _, registry) = Create();
        Run(executor, ctx =>
        {
            for (ulong i = 1; i <= 3; i++)
            {
                Assert.Equal(WorldSpawnResult.Registered,
                    registry.RegisterStatic(ctx, Def(i, i + 50)));
            }
            Assert.Equal(WorldSpawnResult.LinkChanged,
                registry.SetLinkedRespawn(ctx, Key(1), Key(2)));
            Assert.Equal(WorldSpawnResult.LinkChanged,
                registry.SetLinkedRespawn(ctx, Key(2), Key(3)));
            Assert.Equal(WorldSpawnResult.LinkCycle,
                registry.SetLinkedRespawn(ctx, Key(3), Key(1)));
            Assert.Equal(WorldSpawnResult.LinkCycle,
                registry.SetLinkedRespawn(ctx, Key(1), Key(1)));
            Assert.Equal(WorldSpawnResult.NotFound,
                registry.SetLinkedRespawn(ctx, Key(1), Key(999)));
            Assert.Equal(WorldSpawnResult.AlreadyCompleted,
                registry.SetLinkedRespawn(ctx, Key(1), Key(2)));
        });
        Assert.True(registry.TryGet(Key(3), out var third));
        Assert.Null(third.LinkedTo);
    }

    [Fact]
    public void LinkedRespawnUsesParentFirstEvenIfChildHasLowerKey()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 11, x: 1, delay: 100));
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.Registered,
            registry.RegisterStatic(ctx, Def(9, 19, x: 2, delay: 100))));
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.LinkChanged,
            registry.SetLinkedRespawn(ctx, Key(1), Key(9))));
        var activated = Step(executor, clock, registry);
        Assert.Equal([Key(9), Key(1)], activated.Select(x => x.Key));
        Run(executor, ctx =>
        {
            Assert.Equal(WorldSpawnResult.Despawned, registry.Despawn(ctx, Key(9)));
            Assert.Equal(WorldSpawnResult.Despawned, registry.Despawn(ctx, Key(1)));
        });
        Assert.Empty(Step(executor, clock, registry)); // tick2
        Assert.Equal([Key(9), Key(1)], Step(executor, clock, registry)
            .Select(x => x.Key)); // tick3
    }

    [Fact]
    public void ExternalDespawnBeforeDueIsReconciledAndDoesNotDuplicate()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 100));
        var old = Assert.Single(Step(executor, clock, registry));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Despawned,
            world.Despawn(ctx, old.Identity)));
        Assert.Empty(Step(executor, clock, registry)); // tick2 discovers external retirement, due tick4
        Assert.True(registry.TryGet(Key(1), out var waiting));
        Assert.Equal(new Tick(4), waiting.DueTick);
        Assert.Empty(Step(executor, clock, registry)); // tick3
        var fresh = Assert.Single(Step(executor, clock, registry)); // tick4
        Assert.Equal(new EntityGeneration(2), fresh.Identity.Generation);
        Assert.Equal(1, world.LiveCount);
    }

    [Fact]
    public void CheckpointIsFrozenAndRestoreSameWorldAdoptsAlreadyConfirmedInstance()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        var original = Assert.Single(Step(executor, clock, registry));
        WorldSpawnCheckpoint? captured = null;
        Run(executor, ctx => captured = registry.Capture(ctx));
        var checkpoint = Assert.IsType<WorldSpawnCheckpoint>(captured);
        Assert.Single(checkpoint.Entries);
        WorldSpawnRegistry? restored = null;
        Run(executor, ctx => restored = WorldSpawnRegistry.Restore(ctx, world, clock, checkpoint));
        Assert.NotNull(restored);
        Assert.Equal(WorldSpawnStatus.Active, restored.TryGet(Key(1), out var record)
            ? record.Status : WorldSpawnStatus.Retired);
        Assert.Equal(original.Identity, record.Current);
        Assert.Empty(Step(executor, clock, restored));
        Assert.Equal(1, world.LiveCount);
        Assert.Equal(original.Identity, checkpoint.Entries[0].Current);
    }

    [Fact]
    public void CrashRestoreMissingConfirmedEntityFailClosesUntilTrustedReconciliation()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        var original = Assert.Single(Step(executor, clock, registry));
        WorldSpawnCheckpoint? checkpoint = null;
        Run(executor, ctx => checkpoint = registry.Capture(ctx));
        var (freshWorld, freshExecutor, freshClock, _) = Create(initialTick: 1);
        WorldSpawnRegistry? restored = null;
        Run(freshExecutor, ctx => restored = WorldSpawnRegistry.Restore(ctx,
            freshWorld, freshClock, checkpoint!));
        Assert.NotNull(restored);
        Assert.True(restored.TryGet(Key(1), out var held));
        Assert.Equal(WorldSpawnStatus.RecoveryHold, held.Status);
        Run(freshExecutor, ctx => Assert.Equal(SpatialCellResult.Activated,
            restored.LoadGrid(ctx, new SpatialCellKey(0, 0, 0))));
        for (var i = 0; i < 4; i++)
        {
            Assert.Empty(Step(freshExecutor, freshClock, restored));
        }
        Assert.Equal(0, freshWorld.LiveCount);
        Run(freshExecutor, ctx =>
        {
            Assert.Equal(WorldSpawnResult.RecoveryPending,
                restored.ConfirmRecovered(ctx, Key(1), original.Identity));
            // A trusted canonical restore (independent of the Spawn scheduler) reconciles the exact identity.
            Assert.Equal(WorldEntityResult.Spawned, freshWorld.Spawn(ctx,
                new WorldEntitySeed(original.Identity, Def(1, 10).Transform)));
            Assert.Equal(WorldSpawnResult.AlreadyCompleted,
                restored.ConfirmRecovered(ctx, Key(1), original.Identity));
        });
        Assert.Empty(Step(freshExecutor, freshClock, restored));
        Assert.Equal(1, freshWorld.LiveCount);
    }

    [Fact]
    public void WaitingCheckpointRestoresDeadlineAndDoesNotPrematurelyRespawn()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 150));
        Assert.Single(Step(executor, clock, registry)); // tick1
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.Despawned,
            registry.Despawn(ctx, Key(1)))); // due4
        WorldSpawnCheckpoint? checkpoint = null;
        Run(executor, ctx => checkpoint = registry.Capture(ctx));
        Assert.Equal(new Tick(4), checkpoint!.Entries[0].DueTick);
        var (freshWorld, freshExecutor, freshClock, _) = Create(initialTick: 1);
        WorldSpawnRegistry? restored = null;
        Run(freshExecutor, ctx =>
        {
            restored = WorldSpawnRegistry.Restore(ctx, freshWorld, freshClock, checkpoint);
            Assert.Equal(SpatialCellResult.Activated, restored.LoadGrid(ctx,
                new SpatialCellKey(0, 0, 0)));
        });
        Assert.Empty(Step(freshExecutor, freshClock, restored!)); // tick2
        Assert.Empty(Step(freshExecutor, freshClock, restored!)); // tick3
        var respawn = Assert.Single(Step(freshExecutor, freshClock, restored!)); // tick4
        Assert.Equal(new EntityGeneration(2), respawn.Identity.Generation);
        Assert.Equal(1, freshWorld.LiveCount);
    }

    [Fact]
    public void RestoreConflictingLiveEntityNeverProducesUnconfirmedDuplicate()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 100));
        Assert.Single(Step(executor, clock, registry));
        Run(executor, ctx => Assert.Equal(WorldSpawnResult.Despawned,
            registry.Despawn(ctx, Key(1))));
        WorldSpawnCheckpoint? checkpoint = null;
        Run(executor, ctx => checkpoint = registry.Capture(ctx));
        var (freshWorld, freshExecutor, freshClock, _) = Create(initialTick: 1);
        Run(freshExecutor, ctx => Assert.Equal(WorldEntityResult.Spawned,
            freshWorld.Spawn(ctx, new WorldEntitySeed(
                new EntityIdentity(freshWorld.World, freshWorld.Epoch,
                    new EntityId(10), new EntityGeneration(1)),
                Def(1, 10).Transform))));
        WorldSpawnRegistry? recovered = null;
        Run(freshExecutor, ctx =>
        {
            recovered = WorldSpawnRegistry.Restore(ctx, freshWorld, freshClock, checkpoint!);
            Assert.Equal(SpatialCellResult.AlreadyCompleted, recovered.LoadGrid(ctx,
                new SpatialCellKey(0, 0, 0)));
        });
        Assert.True(recovered!.TryGet(Key(1), out var held));
        Assert.Equal(WorldSpawnStatus.RecoveryHold, held.Status);
        for (var i = 0; i < 5; i++)
        {
            Assert.Empty(Step(freshExecutor, freshClock, recovered));
        }
        Assert.Equal(1, freshWorld.LiveCount);
    }

    [Fact]
    public void TimerStepRunsWithinP19PhaseAndReplayIsStable()
    {
        IReadOnlyList<string> Simulate()
        {
            var (world, executor, clock, registry) = Create();
            RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 50));
            var pipeline = new WorldTickPipeline(world, clock, [registry]);
            var hashes = new List<string>();
            for (var n = 1; n <= 4; n++)
            {
                clock.AdvanceTicks(1);
                Run(executor, ctx =>
                {
                    if (n == 2)
                    {
                        Assert.Equal(WorldSpawnResult.Despawned, registry.Despawn(ctx, Key(1)));
                    }
                    pipeline.Execute(ctx, new Tick((ulong)n));
                });
                hashes.Add(pipeline.LastStateHash);
            }
            Assert.Equal(WorldTickPhase.Timers, registry.Phase);
            Assert.Equal("world.spawn-respawn", registry.Key);
            Assert.Equal(new EntityGeneration(2),
                registry.TryGet(Key(1), out var current)
                    ? current.Current!.Value.Generation : default);
            return hashes;
        }
        var one = Simulate();
        var two = Simulate();
        Assert.Equal(one, two);
        Assert.Equal(4, one.Count);
        Assert.Equal(64, one[0].Length);
    }

    [Fact]
    public void ExpiredP18LeaseCannotRegisterLoadDespawnOrCapture()
    {
        var (_, executor, _, registry) = Create();
        IWorldWriteContext? captured = null;
        Run(executor, ctx => captured = ctx);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                registry.RegisterStatic(captured!, Def(1, 10))).Fault);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                registry.LoadGrid(captured!, new SpatialCellKey(0, 0, 0))).Fault);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                registry.Despawn(captured!, Key(1))).Fault);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                registry.Capture(captured!)).Fault);
    }

    [Fact]
    public void SkippedAndHistoricalTimersTicksAreRejectedButDuplicateTickIsIdempotent()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        clock.AdvanceTicks(4);
        Run(executor, ctx =>
        {
            Assert.Throws<InvalidOperationException>(() =>
                registry.Advance(ctx, new Tick(3)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.Advance(ctx, new Tick(5)));
            Assert.Equal((ulong)0, registry.LastProcessedTick.Value);
            Assert.Single(registry.Advance(ctx, new Tick(1)));
            Assert.Empty(registry.Advance(ctx, new Tick(1)));
            Assert.Throws<InvalidOperationException>(() =>
                registry.Advance(ctx, new Tick(0)));
            Assert.Throws<InvalidOperationException>(() =>
                registry.Advance(ctx, new Tick(4)));
        });
        Assert.Equal((ulong)1, registry.LastProcessedTick.Value);
        Assert.Equal(1, world.LiveCount);
    }

    [Fact]
    public void RegistryBudgetIsBoundedWithoutSpawningAnyEntities()
    {
        var (world, executor, _, registry) = Create();
        Run(executor, ctx =>
        {
            for (ulong i = 1; i <= 4096; i++)
            {
                Assert.Equal(WorldSpawnResult.Registered,
                    registry.RegisterStatic(ctx, Def(i, i + 10)));
            }
            Assert.Equal(WorldSpawnResult.CapacityExceeded,
                registry.RegisterStatic(ctx, Def(4097, 4107)));
        });
        Assert.Equal(4096, registry.Count);
        Assert.Equal(0, world.LiveCount);
        Assert.Equal(0, world.SpatialCellCount);
    }

    [Fact]
    public void CapturedCheckpointRemainsImmutableWhenTimerStateChanges()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10, delay: 150));
        var first = Assert.Single(Step(executor, clock, registry));
        WorldSpawnCheckpoint? before = null;
        WorldSpawnCheckpoint? after = null;
        Run(executor, ctx =>
        {
            before = registry.Capture(ctx);
            Assert.Equal(WorldSpawnResult.Despawned, registry.Despawn(ctx, Key(1)));
            after = registry.Capture(ctx);
        });
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(WorldSpawnStatus.Active, before.Entries[0].Status);
        Assert.Equal(first.Identity, before.Entries[0].Current);
        Assert.Null(before.Entries[0].DueTick);
        Assert.Equal(WorldSpawnStatus.Waiting, after.Entries[0].Status);
        Assert.Equal(new Tick(4), after.Entries[0].DueTick);
        Assert.Null(after.Entries[0].Current);
        Assert.True(after.Version > before.Version);
        Assert.Equal(0, world.LiveCount);
    }

    [Fact]
    public void RecoveryRefusesUnverifiedNewGenerationAndRetainsExactlyOneLiveInstance()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        var first = Assert.Single(Step(executor, clock, registry));
        WorldSpawnCheckpoint? checkpoint = null;
        Run(executor, ctx => checkpoint = registry.Capture(ctx));
        var (freshWorld, freshExecutor, freshClock, _) = Create(initialTick: 1);
        var unrelated = new EntityIdentity(freshWorld.World, freshWorld.Epoch,
            first.Identity.Id, new EntityGeneration(2));
        Run(freshExecutor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, freshWorld.Spawn(ctx,
                new WorldEntitySeed(unrelated, Def(1, 10).Transform)));
        });
        WorldSpawnRegistry? restored = null;
        Run(freshExecutor, ctx =>
        {
            restored = WorldSpawnRegistry.Restore(ctx, freshWorld, freshClock, checkpoint!);
            Assert.Equal(WorldSpawnResult.RecoveryPending,
                restored.ConfirmRecovered(ctx, Key(1), first.Identity));
            Assert.Equal(WorldSpawnResult.RecoveryPending,
                restored.ConfirmRecovered(ctx, Key(1), unrelated));
        });
        Assert.True(restored!.TryGet(Key(1), out var held));
        Assert.Equal(WorldSpawnStatus.RecoveryHold, held.Status);
        Assert.Empty(Step(freshExecutor, freshClock, restored));
        Assert.Equal(1, freshWorld.LiveCount);
        Assert.True(freshWorld.TryGetSnapshot(unrelated.Id, out var canonical));
        Assert.Equal(unrelated, canonical.Identity);
    }

    [Fact]
    public void RestoreWithOlderClockThanCheckpointFailsBeforePublishingRegistry()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        Assert.Single(Step(executor, clock, registry));
        WorldSpawnCheckpoint? checkpoint = null;
        Run(executor, ctx => checkpoint = registry.Capture(ctx));
        var (freshWorld, freshExecutor, freshClock, _) = Create();
        Run(freshExecutor, ctx => Assert.Throws<InvalidOperationException>(() =>
            WorldSpawnRegistry.Restore(ctx, freshWorld, freshClock, checkpoint!)));
        Assert.Equal(0, freshWorld.LiveCount);
    }

    [Fact]
    public void FaultedWorldRejectsSpawnRegistryWritesAndLeavesLastCheckpointUnchanged()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        var initial = Assert.Single(Step(executor, clock, registry));
        WorldSpawnCheckpoint? before = null;
        Run(executor, ctx =>
        {
            before = registry.Capture(ctx);
            Assert.True(world.Fault(ctx));
            Assert.Throws<InvalidOperationException>(() => registry.Advance(ctx, new Tick(2)));
            Assert.Throws<InvalidOperationException>(() => registry.Despawn(ctx, Key(1)));
            Assert.Throws<InvalidOperationException>(() =>
                registry.SetLinkedRespawn(ctx, Key(1), null));
            Assert.Throws<InvalidOperationException>(() => registry.Capture(ctx));
        });
        Assert.NotNull(before);
        Assert.True(registry.TryGet(Key(1), out var unchanged));
        Assert.Equal(WorldSpawnStatus.Active, unchanged.Status);
        Assert.Equal(initial.Identity, unchanged.Current);
        Assert.Equal(before.Version, registry.Version);
        Assert.Equal(new Tick(1), registry.LastProcessedTick);
    }

    [Fact]
    public void DrainingWorldDoesNotProcessSpawnTimersAndStopCannotLoadGrid()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        Assert.Single(Step(executor, clock, registry));
        clock.AdvanceTicks(1);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginDrain(ctx));
            Assert.Throws<InvalidOperationException>(() => registry.Advance(ctx, clock.CurrentTick));
            Assert.True(world.Stop(ctx));
            Assert.Throws<InvalidOperationException>(() =>
                registry.LoadGrid(ctx, new SpatialCellKey(0, 0, 0)));
        });
        Assert.Equal(new Tick(1), registry.LastProcessedTick);
        Assert.Equal(0, world.LiveCount);
    }

    [Fact]
    public void DrainingGridUnloadAndRelinkAreRejectedWithoutPartialRetirement()
    {
        var (world, executor, clock, registry) = Create();
        RegisterAndLoad(world, executor, registry, Def(1, 10));
        var spawned = Assert.Single(Step(executor, clock, registry));
        var before = registry.Version;
        Run(executor, ctx =>
        {
            Assert.True(world.BeginDrain(ctx));
            Assert.Equal(SpatialCellResult.InvalidState,
                registry.UnloadGrid(ctx, new SpatialCellKey(0, 0, 0)));
            Assert.Equal(WorldSpawnResult.InvalidState,
                registry.SetLinkedRespawn(ctx, Key(1), null));
            Assert.Equal(WorldSpawnResult.InvalidState,
                registry.ConfirmRecovered(ctx, Key(1), spawned.Identity));
            Assert.Equal(before, registry.Version);
            Assert.Equal(1, world.LiveCount);
            Assert.True(world.Stop(ctx));
        });
        Assert.Equal(WorldLifecycle.Stopped, world.State);
        Assert.Equal(0, world.LiveCount);
    }

    private sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => action(context);
    }
}
