using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldSpatialTests
{
    private static (WorldInstance World, WorldExecutor Executor) Create(double size = 32)
    {
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
            new WorldOwnershipRegistry(), size);
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });
        return (world, executor);
    }

    private static EntityIdentity Id(ulong id, ulong generation = 1) =>
        new(new WorldId(1), new WorldEpoch(1),
            new EntityId(id), new EntityGeneration(generation));

    private static WorldEntitySeed Seed(EntityIdentity id, double x, double y = 0, double z = 0) =>
        new(id, new Transform(new Position(x, y, z), new Angle(0)));

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(action)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    [Fact]
    public void CellFloorHandlesNegativePositionsAndExactBoundaries()
    {
        var (world, _) = Create();
        Assert.True(world.TryLocateCell(new Position(-0.01, 32, -32), out var key));
        Assert.Equal(new SpatialCellKey(-1, 1, -1), key);
        Assert.True(world.TryLocateCell(new Position(-32, 31.999, 0), out key));
        Assert.Equal(new SpatialCellKey(-1, 0, 0), key);
        Assert.True(world.TryLocateCell(new Position(0, 0, 0), out key));
        Assert.Equal(new SpatialCellKey(0, 0, 0), key);
        Assert.False(world.TryLocateCell(new Position(1e20, 0, 0), out _));
    }

    [Fact]
    public void CellSizeRejectsInvalidAndExtremeSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(4097));
        var (world, _) = Create(64);
        Assert.Equal(64, world.CellSize);
    }

    [Fact]
    public void SpawnAttachActivationAndRemoveMaintainDerivedMembership()
    {
        var (world, executor) = Create();
        var first = Id(1);
        var attached = Id(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(first, 0)));
            Assert.Equal(WorldEntityResult.Added, world.Add(ctx, Seed(attached, 2)));
        });
        Assert.Equal(2, world.SpatialIndexedCount);
        Assert.Single(world.QueryNearby(new Position(0, 0, 0), new Distance(4)));
        Assert.True(world.TryGetCell(new SpatialCellKey(0, 0, 0), out var cell));
        Assert.Equal(2, cell.Count);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Activated, world.Activate(ctx, attached)));
        Assert.Equal([1UL, 2UL], world.QueryNearby(new Position(0, 0, 0), new Distance(4))
            .Select(s => s.Identity.Id.Value));
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, first));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.Remove(ctx, first));
        });
        Assert.Equal(1, world.SpatialIndexedCount);
        Assert.Equal([2UL], world.QueryRange(new Bounds(
            new Position(0, -1, -1), new Position(3, 1, 1)))
            .Select(s => s.Identity.Id.Value));
        Assert.True(world.TryGetCell(new SpatialCellKey(0, 0, 0), out cell));
        Assert.Equal(1, cell.Count);
    }

    [Fact]
    public void PrepareRejectsInvalidPositionWithoutChangingTransformOrIndex()
    {
        var (world, executor) = Create();
        var id = Id(7);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Spawned,
            world.Spawn(ctx, Seed(id, 2))));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.SpatialOutOfBounds,
            world.Relocate(ctx, id, new Transform(new Position(1e20, 0, 0), new Angle(0)))));
        Assert.True(world.TryGetSnapshot(id.Id, out var after));
        Assert.Equal(new Position(2, 0, 0), after.Transform.Position);
        Assert.Equal([7UL], world.QueryNearby(new Position(2, 0, 0), new Distance(0))
            .Select(s => s.Identity.Id.Value));
        Assert.Equal(1, world.SpatialIndexedCount);
        Assert.Equal(WorldLifecycle.Running, world.State);
    }

    [Fact]
    public void CrossCellRelocationUpdatesQueriesAndMaintainsSingleTruth()
    {
        var (world, executor) = Create();
        var id = Id(1);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Spawned,
            world.Spawn(ctx, Seed(id, -0.01))));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Relocated,
            world.Relocate(ctx, id, new Transform(new Position(32, 1, 0), new Angle(1)))));
        Assert.Empty(world.QueryRange(new Bounds(
            new Position(-1, -1, -1), new Position(0, 1, 1))));
        var one = Assert.Single(world.QueryRange(new Bounds(
            new Position(32, 1, 0), new Position(32, 1, 0))));
        Assert.Equal(new Position(32, 1, 0), one.Transform.Position);
        Assert.True(world.TryGetSnapshot(id.Id, out var canonical));
        Assert.Equal(one.Transform, canonical.Transform);
        Assert.True(world.TryGetCell(new SpatialCellKey(-1, 0, 0), out var old));
        Assert.Equal(0, old.Count);
        Assert.True(world.TryGetCell(new SpatialCellKey(1, 0, 0), out var current));
        Assert.Equal(1, current.Count);
    }

    [Fact]
    public void SameCellMoveOnlyChangesCanonicalTransformAndRangeMembership()
    {
        var (world, executor) = Create();
        var id = Id(9);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id, 1)));
            Assert.Equal(WorldEntityResult.Relocated, world.Relocate(ctx, id,
                new Transform(new Position(3, 0, 0), new Angle(2))));
        });
        Assert.Empty(world.QueryNearby(new Position(1, 0, 0), default));
        Assert.Single(world.QueryNearby(new Position(3, 0, 0), default));
        Assert.Equal(1, world.SpatialIndexedCount);
        Assert.Equal(1, world.SpatialCellCount);
    }

    [Fact]
    public void EmptyCellCanUnloadButOccupiedCellCannotAndRequiresExplicitReactivation()
    {
        var (world, executor) = Create();
        var id = Id(4);
        var cell = new SpatialCellKey(2, 0, 0);
        Run(executor, ctx =>
        {
            Assert.Equal(SpatialCellResult.Activated, world.ActivateCell(ctx, cell));
            Assert.Equal(SpatialCellResult.AlreadyCompleted, world.ActivateCell(ctx, cell));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id, 65)));
            Assert.Equal(SpatialCellResult.Occupied, world.UnloadCell(ctx, cell));
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, id));
            Assert.Equal(SpatialCellResult.Unloaded, world.UnloadCell(ctx, cell));
            Assert.Equal(SpatialCellResult.AlreadyCompleted, world.UnloadCell(ctx, cell));
            Assert.Equal(WorldEntityResult.CellUnloaded, world.Spawn(ctx, Seed(Id(5), 65)));
            Assert.Equal(SpatialCellResult.Activated, world.ActivateCell(ctx, cell));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(Id(5), 65)));
        });
        Assert.True(world.TryGetCell(cell, out var info));
        Assert.Equal(SpatialCellLifecycle.Active, info.Lifecycle);
        Assert.Equal(1, info.Count);
        Assert.Equal([5UL], world.QueryNearby(new Position(65, 0, 0), default)
            .Select(s => s.Identity.Id.Value));
    }

    [Fact]
    public void DestinationUnloadedRejectsMoveWithoutIndexOrTransformHalfUpdate()
    {
        var (world, executor) = Create();
        var id = Id(20);
        var destination = new SpatialCellKey(3, 0, 0);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id, 2)));
            Assert.Equal(SpatialCellResult.Activated, world.ActivateCell(ctx, destination));
            Assert.Equal(SpatialCellResult.Unloaded, world.UnloadCell(ctx, destination));
            Assert.Equal(WorldEntityResult.CellUnloaded, world.Relocate(ctx, id,
                new Transform(new Position(100, 0, 0), new Angle(0))));
        });
        Assert.True(world.TryGetSnapshot(id.Id, out var current));
        Assert.Equal(new Position(2, 0, 0), current.Transform.Position);
        Assert.Empty(world.QueryNearby(new Position(100, 0, 0), default));
        Assert.Single(world.QueryNearby(new Position(2, 0, 0), default));
        Assert.True(world.TryGetCell(destination, out var cell));
        Assert.Equal(SpatialCellLifecycle.Unloaded, cell.Lifecycle);
        Assert.Equal(0, cell.Count);
    }

    [Fact]
    public void StaleGenerationCannotRelocateOrDeleteNewOccupants()
    {
        var (world, executor) = Create();
        var old = Id(8);
        var current = Id(8, generation: 2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(old, 1)));
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, old));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(current, 36)));
            Assert.Equal(WorldEntityResult.Stale, world.Relocate(ctx, old,
                new Transform(new Position(0, 0, 0), new Angle(0))));
            Assert.Equal(WorldEntityResult.Stale, world.Remove(ctx, old));
        });
        Assert.Equal(1, world.SpatialIndexedCount);
        var snapshot = Assert.Single(world.QueryRange(new Bounds(
            new Position(36, 0, 0), new Position(36, 0, 0))));
        Assert.Equal(current, snapshot.Identity);
    }

    [Fact]
    public void RebuildFromCanonicalTransformIsEquivalentAndPreservesEmptyUnloadedCell()
    {
        var (world, executor) = Create();
        var unload = new SpatialCellKey(10, 0, 0);
        Run(executor, ctx =>
        {
            Assert.Equal(SpatialCellResult.Activated, world.ActivateCell(ctx, unload));
            Assert.Equal(SpatialCellResult.Unloaded, world.UnloadCell(ctx, unload));
            for (ulong id = 1; id <= 30; id++)
            {
                Assert.Equal(WorldEntityResult.Spawned,
                    world.Spawn(ctx, Seed(Id(id), (double)id * 3)));
            }
        });
        var before = world.QueryRange(new Bounds(
            new Position(0, -1, -1), new Position(100, 1, 1))).ToArray();
        Run(executor, ctx => Assert.True(world.RebuildSpatialIndex(ctx)));
        var after = world.QueryRange(new Bounds(
            new Position(0, -1, -1), new Position(100, 1, 1))).ToArray();
        Assert.Equal(before, after);
        Assert.Equal(30, world.SpatialIndexedCount);
        Assert.True(world.TryGetCell(unload, out var cell));
        Assert.Equal(SpatialCellLifecycle.Unloaded, cell.Lifecycle);
    }

    [Fact]
    public void RandomizedCrossCellMovesAndRangeQueriesMatchCanonicalBruteScan()
    {
        var (world, executor) = Create();
        var random = new Random(19091);
        var positions = new Dictionary<ulong, Position>();
        Run(executor, ctx =>
        {
            for (ulong id = 1; id <= 120; id++)
            {
                var at = RandomPosition(random);
                positions.Add(id, at);
                Assert.Equal(WorldEntityResult.Spawned,
                    world.Spawn(ctx, new WorldEntitySeed(Id(id),
                        new Transform(at, new Angle(0)))));
            }
        });

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var entity = (ulong)random.Next(1, 121);
            var next = RandomPosition(random);
            Run(executor, ctx => Assert.Equal(WorldEntityResult.Relocated,
                world.Relocate(ctx, Id(entity), new Transform(next, new Angle(0)))));
            positions[entity] = next;
            var center = RandomPosition(random);
            var radius = new Distance(random.Next(0, 120));
            var actual = world.QueryNearby(center, radius).Select(s => s.Identity.Id.Value);
            var expected = positions
                .Where(pair => DistanceBetween(pair.Value, center) <= radius.Meters)
                .Select(pair => pair.Key).OrderBy(static id => id);
            Assert.Equal(expected, actual);

            var minimum = RandomPosition(random);
            var bounds = new Bounds(minimum, new Position(
                minimum.X + 150, minimum.Y + 100, minimum.Z + 70));
            var rangeActual = world.QueryRange(bounds).Select(s => s.Identity.Id.Value);
            var rangeExpected = positions.Where(pair => bounds.Contains(pair.Value))
                .Select(pair => pair.Key).OrderBy(static id => id);
            Assert.Equal(rangeExpected, rangeActual);
        }

        Assert.Equal(120, world.SpatialIndexedCount);
        Run(executor, ctx => Assert.True(world.RebuildSpatialIndex(ctx)));
    }

    [Fact]
    public void ShutdownDisposesEverySpatialEntryAndUnloadsAllCells()
    {
        var (world, executor) = Create();
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(Id(1), 1)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(Id(2), 99)));
            Assert.True(world.BeginDrain(ctx));
            Assert.True(world.Stop(ctx));
            Assert.False(world.RebuildSpatialIndex(ctx));
        });
        Assert.Equal(0, world.SpatialIndexedCount);
        Assert.Equal(0, world.SpatialCellCount);
        Assert.Empty(world.QueryNearby(new Position(1, 0, 0), new Distance(100)));
        Assert.Equal(WorldLifecycle.Stopped, world.State);
    }

    [Fact]
    public void OldLeaseOrWrongWorldCannotManipulateGrid()
    {
        var (world, executor) = Create();
        IWorldWriteContext? captured = null;
        Run(executor, ctx => captured = ctx);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.ActivateCell(captured!, new SpatialCellKey(0, 0, 0))).Fault);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.RebuildSpatialIndex(captured!)).Fault);
    }

    private static Position RandomPosition(Random random) =>
        new(random.Next(-300, 301), random.Next(-300, 301), random.Next(-120, 121));

    private static double DistanceBetween(Position one, Position other)
    {
        var dx = one.X - other.X;
        var dy = one.Y - other.Y;
        var dz = one.Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => action(context);
    }
}
