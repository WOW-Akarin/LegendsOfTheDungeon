using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

Case("boundary-oscillation-exactly-once-enter-leave", Boundaries);
Case("tag-life-stun-filter-and-readonly-snapshot", Filters);
Case("despawn-generation-and-observer-invalidations", Retirement);
Case("observer-isolation-and-world-versioned-deltas", Observers);
Case("bounded-relevance-and-real-lease-rejection", GuardAndCapacity);
Console.WriteLine("P21_WORLD_VISIBILITY_INTEGRATION_PASS");

static void Case(string title, Action check)
{
    check();
    Console.WriteLine("PASS " + title);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static EntityIdentity Id(ulong id, ulong generation = 1) =>
    new(new WorldId(1), new WorldEpoch(1), new EntityId(id),
        new EntityGeneration(generation));

static (WorldInstance World, WorldExecutor Executor) Create()
{
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
        new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
    Run(executor, ctx => Require(world.BeginLoading(ctx) && world.Start(ctx),
        "world startup failed"));
    return (world, executor);
}

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "P18 admission failed");
    Require(executor.RunBatch(1).Status == WorldRunStatus.Completed,
        "P18 synchronous batch did not commit");
}

static void Spawn(WorldInstance world, IWorldWriteContext ctx,
    EntityIdentity id, double x) =>
    Require(world.Spawn(ctx, new WorldEntitySeed(id,
        new Transform(new Position(x, 0, 0), new Angle(0)))) ==
        WorldEntityResult.Spawned, "spawn failed");

static void Move(WorldInstance world, IWorldWriteContext ctx,
    EntityIdentity id, double x) =>
    Require(world.Relocate(ctx, id, new Transform(
        new Position(x, 0, 0), new Angle(0))) == WorldEntityResult.Relocated,
        "move rejected");

static WorldVisibilityDelta Refresh(WorldExecutor executor, WorldVisibilitySet set)
{
    WorldVisibilityDelta? delta = null;
    Run(executor, ctx => delta = set.Refresh(ctx));
    return delta ?? throw new InvalidOperationException("refresh not invoked");
}

static void Boundaries()
{
    var (world, executor) = Create();
    var observer = Id(1);
    var target = Id(2);
    var set = new WorldVisibilitySet(world, observer,
        new WorldVisibilityFilter(new Distance(32)));
    Run(executor, ctx =>
    {
        Spawn(world, ctx, observer, 0);
        Spawn(world, ctx, target, 32);
    });
    var enter = Refresh(executor, set);
    Require(enter.Entered.Select(m => m.Identity).SequenceEqual([target]) &&
        enter.Left.Count == 0 && enter.SnapshotVersion == 1,
        "boundary target did not enter once");
    for (var index = 0; index < 32; index++)
    {
        Run(executor, ctx => Move(world, ctx, target, 32.1));
        var leave = Refresh(executor, set);
        Require(leave.Left.SequenceEqual([target]) && leave.Entered.Count == 0,
            "boundary leave missing or duplicate");
        Require(Refresh(executor, set).Left.Count == 0, "duplicate leave");
        Run(executor, ctx => Move(world, ctx, target, 32));
        Require(Refresh(executor, set).Entered.Select(m => m.Identity)
            .SequenceEqual([target]), "boundary enter missing");
        Require(Refresh(executor, set).Entered.Count == 0, "duplicate enter");
    }
}

static void Filters()
{
    var (world, executor) = Create();
    var observer = Id(1);
    var target = Id(2);
    var set = new WorldVisibilitySet(world, observer,
        new WorldVisibilityFilter(new Distance(50), ["npc"],
            excludedTags: ["hidden"], excludedControls: EntityControlEffects.Stunned));
    EntityEffectHandle stun = default;
    Run(executor, ctx =>
    {
        Spawn(world, ctx, observer, 0);
        Spawn(world, ctx, target, 10);
    });
    Require(Refresh(executor, set).Entered.Count == 0, "untagged target was relevant");
    Run(executor, ctx => Require(world.SetPermanentTag(ctx, target, "npc", true) ==
        WorldEntityResult.TagChanged, "tag not accepted"));
    var initial = Refresh(executor, set);
    Require(initial.Entered.Count == 1 && initial.View.Members.Count == 1,
        "tagged target did not enter");
    Run(executor, ctx =>
    {
        Require(world.GrantControl(ctx, target, new EntityEffectSource(3),
            EntityControlEffects.Stunned, out stun) == WorldEntityResult.ControlGranted,
            "stun failed");
    });
    Require(Refresh(executor, set).Left.SequenceEqual([target]), "stunned target did not leave");
    Run(executor, ctx => Require(world.RevokeControl(ctx, target, stun) ==
        WorldEntityResult.ControlRevoked, "stun revoke failed"));
    Require(Refresh(executor, set).Entered.Count == 1, "unstunned target failed to return");
    Run(executor, ctx => Require(world.MarkDead(ctx, target) ==
        WorldEntityResult.LifeChanged, "life state failed"));
    Require(Refresh(executor, set).Left.SequenceEqual([target]), "dead target failed to leave");
    Require(initial.View.Members[0].Tags.SequenceEqual(["npc"]) &&
        initial.View.Members[0].Life == LifeState.Alive &&
        initial.SnapshotVersion == 2,
        "historical read-only view mutated");
}

static void Retirement()
{
    var (world, executor) = Create();
    var observer = Id(1);
    var target = Id(2);
    var next = Id(2, 2);
    var set = new WorldVisibilitySet(world, observer,
        new WorldVisibilityFilter(new Distance(50)));
    Run(executor, ctx =>
    {
        Spawn(world, ctx, observer, 0);
        Spawn(world, ctx, target, 10);
    });
    Require(Refresh(executor, set).Entered.Count == 1, "spawn not visible");
    Run(executor, ctx =>
    {
        Require(world.Despawn(ctx, target) == WorldEntityResult.Despawned, "despawn failed");
        Spawn(world, ctx, next, 10);
    });
    var replacement = Refresh(executor, set);
    Require(replacement.Left.SequenceEqual([target]) &&
        replacement.Entered.Select(m => m.Identity).SequenceEqual([next]),
        "new generation did not cause old leave/new enter");
    Run(executor, ctx => Require(world.Remove(ctx, observer) ==
        WorldEntityResult.Removed, "observer retire failed"));
    var invalid = Refresh(executor, set);
    Require(!invalid.View.ObserverActive && invalid.Left.SequenceEqual([next]),
        "retired observer retained relevance");
    Require(Refresh(executor, set).Left.Count == 0, "repeat stale observer leave");
}

static void Observers()
{
    var (world, executor) = Create();
    var a = Id(1);
    var b = Id(2);
    var nearby = Id(3);
    var nearA = new WorldVisibilitySet(world, a,
        new WorldVisibilityFilter(new Distance(12)));
    var nearB = new WorldVisibilitySet(world, b,
        new WorldVisibilityFilter(new Distance(12)));
    Run(executor, ctx =>
    {
        Spawn(world, ctx, a, 0);
        Spawn(world, ctx, b, 80);
        Spawn(world, ctx, nearby, 3);
    });
    var aFirst = Refresh(executor, nearA);
    var bFirst = Refresh(executor, nearB);
    Require(aFirst.Entered.Select(m => m.Identity).SequenceEqual([nearby]) &&
        bFirst.Entered.Count == 0, "view mixed observers");
    Run(executor, ctx => Move(world, ctx, nearby, 78));
    var aNext = Refresh(executor, nearA);
    var bNext = Refresh(executor, nearB);
    Require(aNext.Left.SequenceEqual([nearby]) &&
        bNext.Entered.Select(m => m.Identity).SequenceEqual([nearby]),
        "move produced wrong per-observer delta");
    Require(aNext.World == world.World && aNext.Epoch == world.Epoch &&
        aNext.Observer == a && aNext.PreviousVersion == 1 &&
        aNext.SnapshotVersion == 2 && bNext.Observer == b &&
        bNext.SnapshotVersion == 2,
        "world/epoch/observer snapshot version invalid");
    Require(aFirst.View.Members[0].Transform.Position == new Position(3, 0, 0),
        "previous frozen view unexpectedly followed world movement");
}

static void GuardAndCapacity()
{
    var (world, executor) = Create();
    var observer = Id(1);
    var a = Id(2);
    var b = Id(3);
    var set = new WorldVisibilitySet(world, observer,
        new WorldVisibilityFilter(new Distance(50), maximumVisible: 1));
    IWorldWriteContext? captured = null;
    Run(executor, ctx =>
    {
        Spawn(world, ctx, observer, 0);
        Spawn(world, ctx, a, 1);
        Spawn(world, ctx, b, 2);
        captured = ctx;
        try
        {
            set.Refresh(ctx);
            throw new InvalidOperationException("capacity overflow succeeded");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith(
            "Visibility capacity exceeded", StringComparison.Ordinal))
        {
            // Expected: neither partial publication nor world fault.
        }
    });
    Require(set.Current.SnapshotVersion == 0 && set.Current.Members.Count == 0 &&
        world.State == WorldLifecycle.Running, "capacity rejection published partial view");
    try
    {
        set.Refresh(captured!);
        throw new InvalidOperationException("expired P18 lease succeeded");
    }
    catch (WorldAccessException ex) when (ex.Fault == WorldAccessFault.LeaseRequired)
    {
        // Correct: only the current synchronous Scope can refresh.
    }
    Run(executor, ctx => Require(world.Remove(ctx, b) ==
        WorldEntityResult.Removed, "cannot remove excess target"));
    Require(Refresh(executor, set).Entered.Select(m => m.Identity)
        .SequenceEqual([a]), "view did not recover after removing excess target");
}

internal sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}
