using System.Diagnostics;
using System.Runtime.InteropServices;
using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

Check("cross-cell-boundaries-unload-and-failed-prepare", Boundaries);
Check("seeded-randomized-range-and-nearby-match-brute-force", Randomized);
Check("concurrent-readers-never-see-half-relocation", ConcurrentReaders);
Check("retire-rebuild-and-shutdown-release-derived-index", Lifecycle);
Check("first-spatial-benchmark-no-unverified-pass-threshold", Benchmark);
Console.WriteLine("P20_WORLD_SPATIAL_INTEGRATION_PASS");

static void Check(string name, Action action)
{
    action();
    Console.WriteLine("PASS " + name);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static (WorldInstance World, WorldExecutor Executor) Start(double size = 32)
{
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
        new WorldOwnershipRegistry(), size);
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 256, 32, 4);
    Run(executor, ctx =>
    {
        Require(world.BeginLoading(ctx), "could not load world");
        Require(world.Start(ctx), "could not run world");
    });
    return (world, executor);
}

static EntityIdentity Id(ulong number, ulong generation = 1) =>
    new(new WorldId(1), new WorldEpoch(1),
        new EntityId(number), new EntityGeneration(generation));

static WorldEntitySeed Seed(EntityIdentity identity, Position position) =>
    new(identity, new Transform(position, new Angle(0)));

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "P18 trusted action rejected");
    var outcome = executor.RunBatch(1);
    Require(outcome.Status == WorldRunStatus.Completed && outcome.Executed == 1,
        "P18 synchronous action failed");
}

static void Boundaries()
{
    var (world, executor) = Start();
    var a = Id(7);
    var left = new Position(-0.1, 0, 0);
    Require(world.TryLocateCell(left, out var old) &&
        old == new SpatialCellKey(-1, 0, 0), "floor cell boundary failed");
    Require(world.TryLocateCell(new Position(32, 0, 0), out var next) &&
        next == new SpatialCellKey(1, 0, 0), "positive exact edge failed");
    Run(executor, ctx =>
    {
        Require(world.Spawn(ctx, Seed(a, left)) == WorldEntityResult.Spawned,
            "spawning into negative cell failed");
        Require(world.Relocate(ctx, a,
            new Transform(new Position(32, 0, 0), new Angle(1))) ==
            WorldEntityResult.Relocated, "cross-cell update failed");
        Require(world.UnloadCell(ctx, old) == SpatialCellResult.Unloaded,
            "empty previous cell cannot unload");
        Require(world.Relocate(ctx, a,
            new Transform(new Position(-0.1, 0, 0), new Angle(0))) ==
            WorldEntityResult.CellUnloaded, "unloaded cell accepted entity");
        Require(world.Relocate(ctx, a,
            new Transform(new Position(1e20, 0, 0), new Angle(0))) ==
            WorldEntityResult.SpatialOutOfBounds, "out of range cell accepted entity");
    });
    Require(world.TryGetSnapshot(a.Id, out var unchanged) &&
        unchanged.Transform.Position == new Position(32, 0, 0),
        "failed relocation changed canonical transform");
    Require(world.QueryRange(new Bounds(
        new Position(32, 0, 0), new Position(32, 0, 0))).Count == 1,
        "failed relocation changed spatial membership");
    Require(world.QueryNearby(new Position(-0.1, 0, 0), default).Count == 0,
        "old cell still contains entity");
    Run(executor, ctx =>
    {
        Require(world.ActivateCell(ctx, old) == SpatialCellResult.Activated,
            "unloaded cell activation failed");
        Require(world.Relocate(ctx, a,
            new Transform(left, new Angle(0))) == WorldEntityResult.Relocated,
            "reactivated cell refused move");
    });
    Require(world.SpatialIndexedCount == 1, "move duplicated entity membership");
}

static void Randomized()
{
    var (world, executor) = Start();
    var random = new Random(20201116);
    var positions = new Dictionary<ulong, Position>();
    Run(executor, ctx =>
    {
        for (ulong i = 1; i <= 150; i++)
        {
            var position = RandomPosition(random);
            positions[i] = position;
            Require(world.Spawn(ctx, Seed(Id(i), position)) ==
                WorldEntityResult.Spawned, "initial index Add failed");
        }
    });
    for (var i = 0; i < 850; i++)
    {
        var entity = (ulong)random.Next(1, 151);
        var next = RandomPosition(random);
        Run(executor, ctx => Require(world.Relocate(ctx, Id(entity),
            new Transform(next, new Angle(0))) == WorldEntityResult.Relocated,
            "index move failed"));
        positions[entity] = next;
        var center = RandomPosition(random);
        var radius = new Distance(random.Next(0, 160));
        var expected = positions.Where(item =>
                DistanceTo(item.Value, center) <= radius.Meters)
            .Select(item => item.Key).OrderBy(static id => id);
        var actual = world.QueryNearby(center, radius)
            .Select(item => item.Identity.Id.Value);
        Require(expected.SequenceEqual(actual), "Nearby differs from brute-force scan");

        var minimum = RandomPosition(random);
        var range = new Bounds(minimum, new Position(
            minimum.X + 120, minimum.Y + 90, minimum.Z + 70));
        expected = positions.Where(item => range.Contains(item.Value))
            .Select(item => item.Key).OrderBy(static id => id);
        actual = world.QueryRange(range).Select(item => item.Identity.Id.Value);
        Require(expected.SequenceEqual(actual), "Range differs from brute-force scan");
    }

    Run(executor, ctx =>
    {
        for (ulong i = 1; i <= 30; i++)
        {
            Require(world.Remove(ctx, Id(i)) == WorldEntityResult.Removed,
                "remove failed");
            positions.Remove(i);
        }
        Require(world.RebuildSpatialIndex(ctx), "rebuild from canonical failed");
    });
    Require(world.SpatialIndexedCount == positions.Count, "rebuild member count mismatch");
    var everything = world.QueryRange(new Bounds(
        new Position(-401, -401, -401), new Position(401, 401, 401)));
    Require(everything.Select(s => s.Identity.Id.Value)
        .SequenceEqual(positions.Keys.OrderBy(static id => id)),
        "removal/rebuild left missing or zombie entities");
}

static void ConcurrentReaders()
{
    var (world, executor) = Start();
    var entity = Id(101);
    Run(executor, ctx => Require(world.Spawn(ctx,
        Seed(entity, new Position(0, 0, 0))) == WorldEntityResult.Spawned,
        "concurrent read setup failed"));

    var gate = new ManualResetEventSlim();
    var queries = Task.Run(() =>
    {
        gate.Set();
        for (var i = 0; i < 3000; i++)
        {
            var inFirst = world.QueryRange(new Bounds(
                new Position(0, 0, 0), new Position(0, 0, 0)));
            var inSecond = world.QueryRange(new Bounds(
                new Position(50, 0, 0), new Position(50, 0, 0)));
            Require(inFirst.All(s => s.Transform.Position == new Position(0, 0, 0)),
                "half-updated Transform observed at first cell");
            Require(inSecond.All(s => s.Transform.Position == new Position(50, 0, 0)),
                "half-updated Transform observed at destination");
        }
    });
    Require(gate.Wait(TimeSpan.FromSeconds(8)), "reader not ready");
    for (var i = 0; i < 500; i++)
    {
        var x = i % 2 == 0 ? 50d : 0d;
        Run(executor, ctx => Require(world.Relocate(ctx, entity,
            new Transform(new Position(x, 0, 0), new Angle(0))) ==
            WorldEntityResult.Relocated, "concurrent writer failed"));
    }
    queries.GetAwaiter().GetResult();
    Require(world.SpatialIndexedCount == 1, "concurrent queries damaged membership");
}

static void Lifecycle()
{
    var (world, executor) = Start();
    var identity = Id(44);
    Run(executor, ctx =>
    {
        Require(world.Spawn(ctx, Seed(identity, new Position(10, 0, 0))) ==
            WorldEntityResult.Spawned, "spawn failed");
        Require(world.Remove(ctx, identity) == WorldEntityResult.Removed, "remove failed");
        Require(world.Spawn(ctx, Seed(Id(44, 2), new Position(90, 0, 0))) ==
            WorldEntityResult.Spawned, "new generation failed to claim");
        Require(world.Relocate(ctx, identity, new Transform(
            new Position(1, 0, 0), new Angle(0))) == WorldEntityResult.Stale,
            "old generation relocated current entity");
        Require(world.RebuildSpatialIndex(ctx), "rebuild failed");
        Require(world.BeginDrain(ctx) && world.Stop(ctx), "stop failed");
    });
    Require(world.SpatialIndexedCount == 0 && world.SpatialCellCount == 0,
        "shutdown retained spatial entries");
}

static void Benchmark()
{
    const int entityCount = 1600;
    const int moveCount = 700;
    const int queryCount = 400;
    const int seed = 2071;
    var (world, executor) = Start();
    var random = new Random(seed);
    var allocationsBefore = GC.GetAllocatedBytesForCurrentThread();
    var setup = Stopwatch.StartNew();
    Run(executor, ctx =>
    {
        for (ulong id = 1; id <= entityCount; id++)
        {
            var p = RandomPosition(random);
            Require(world.Spawn(ctx, Seed(Id(id), p)) == WorldEntityResult.Spawned,
                "benchmark initial entity Add rejected");
        }
    });
    setup.Stop();
    var moveSamples = new double[moveCount];
    var querySamples = new double[queryCount];
    for (var i = 0; i < moveCount; i++)
    {
        var identity = Id((ulong)random.Next(1, entityCount + 1));
        var next = RandomPosition(random);
        var stopwatch = Stopwatch.StartNew();
        Run(executor, ctx => Require(world.Relocate(ctx, identity,
            new Transform(next, new Angle(0))) == WorldEntityResult.Relocated,
            "benchmark move failed"));
        stopwatch.Stop();
        moveSamples[i] = stopwatch.Elapsed.TotalMilliseconds;
    }

    var totalResults = 0;
    for (var i = 0; i < queryCount; i++)
    {
        var center = RandomPosition(random);
        var radius = new Distance(random.Next(1, 180));
        var stopwatch = Stopwatch.StartNew();
        totalResults += world.QueryNearby(center, radius).Count;
        stopwatch.Stop();
        querySamples[i] = stopwatch.Elapsed.TotalMilliseconds;
    }

    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationsBefore;
    Require(world.SpatialIndexedCount == entityCount, "benchmark ended with missing entities");
    Require(moveSamples.All(double.IsFinite) && querySamples.All(double.IsFinite),
        "benchmark timer returned invalid duration");
    Console.WriteLine(FormattableString.Invariant(
        $"P20_BENCHMARK_V1 sdk=net10.0 os={RuntimeInformation.OSDescription} cpus={Environment.ProcessorCount} build=Release cells={world.SpatialCellCount} entities={entityCount} seed={seed} setup_ms={setup.Elapsed.TotalMilliseconds:F3} move_count={moveCount} move_p50_ms={Percentile(moveSamples, .50):F4} move_p95_ms={Percentile(moveSamples, .95):F4} move_p99_ms={Percentile(moveSamples, .99):F4} query_count={queryCount} query_p50_ms={Percentile(querySamples, .50):F4} query_p95_ms={Percentile(querySamples, .95):F4} query_p99_ms={Percentile(querySamples, .99):F4} query_result_count={totalResults} thread_allocated_bytes={allocated} gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)}"));

}

static double Percentile(double[] samples, double quantile)
{
    var values = samples.Order().ToArray();
    var index = (int)Math.Ceiling(values.Length * quantile) - 1;
    return values[Math.Clamp(index, 0, values.Length - 1)];
}

static Position RandomPosition(Random random) =>
    new(random.Next(-400, 401), random.Next(-400, 401), random.Next(-150, 151));

static double DistanceTo(Position first, Position second)
{
    var dx = first.X - second.X;
    var dy = first.Y - second.Y;
    var dz = first.Z - second.Z;
    return Math.Sqrt(dx * dx + dy * dy + dz * dz);
}

internal sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}
