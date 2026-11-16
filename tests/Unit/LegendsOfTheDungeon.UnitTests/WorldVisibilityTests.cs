using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldVisibilityTests
{
    private static (WorldInstance World, WorldExecutor Executor) Create()
    {
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
            new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });
        return (world, executor);
    }

    private static EntityIdentity Id(ulong id, ulong generation = 1,
        ulong world = 1, ulong epoch = 1) =>
        new(new WorldId(world), new WorldEpoch(epoch), new EntityId(id),
            new EntityGeneration(generation));

    private static WorldEntitySeed Seed(EntityIdentity id, double x) =>
        new(id, new Transform(new Position(x, 0, 0), new Angle(0)));

    private static void Spawn(WorldInstance world, IWorldWriteContext ctx,
        EntityIdentity id, double x) =>
        Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id, x)));

    private static void Move(WorldInstance world, IWorldWriteContext ctx,
        EntityIdentity id, double x) =>
        Assert.Equal(WorldEntityResult.Relocated, world.Relocate(ctx, id,
            new Transform(new Position(x, 0, 0), new Angle(0))));

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(action)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    private static WorldVisibilityDelta Refresh(WorldExecutor executor, WorldVisibilitySet set)
    {
        WorldVisibilityDelta? value = null;
        Run(executor, ctx => value = set.Refresh(ctx));
        return Assert.IsType<WorldVisibilityDelta>(value);
    }

    [Fact]
    public void EnterAndLeaveOccurExactlyOnceAcrossRepeatedRefreshes()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(30)));
        Assert.Equal((ulong)0, set.Current.SnapshotVersion);
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 10);
        });
        var first = Refresh(executor, set);
        Assert.Equal((ulong)0, first.PreviousVersion);
        Assert.Equal((ulong)1, first.SnapshotVersion);
        Assert.Equal([target], first.Entered.Select(e => e.Identity));
        Assert.Empty(first.Left);
        Assert.Equal([target], first.View.Members.Select(e => e.Identity));
        Assert.DoesNotContain(first.View.Members, e => e.Identity == observer);
        var same = Refresh(executor, set);
        Assert.Empty(same.Entered);
        Assert.Empty(same.Left);
        Assert.Equal((ulong)2, same.SnapshotVersion);
        Run(executor, ctx => Move(world, ctx, target, 31));
        var leave = Refresh(executor, set);
        Assert.Empty(leave.Entered);
        Assert.Equal([target], leave.Left);
        var again = Refresh(executor, set);
        Assert.Empty(again.Entered);
        Assert.Empty(again.Left);
        Assert.Empty(again.View.Members);
    }

    [Fact]
    public void ExactDistanceBoundaryAndRepeatedOscillationDoNotDuplicateEvents()
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
        Assert.Single(Refresh(executor, set).Entered);
        for (var i = 0; i < 12; i++)
        {
            Run(executor, ctx => Move(world, ctx, target, 32.01));
            Assert.Equal([target], Refresh(executor, set).Left);
            Assert.Empty(Refresh(executor, set).Left);
            Run(executor, ctx => Move(world, ctx, target, 32));
            Assert.Equal([target], Refresh(executor, set).Entered.Select(e => e.Identity));
            Assert.Empty(Refresh(executor, set).Entered);
        }
    }

    [Fact]
    public void TagFilterRespondsToPermanentTemporaryAndExcludedTagChanges()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var filter = new WorldVisibilityFilter(new Distance(60),
            requiredTags: ["npc.elite"], excludedTags: ["hidden"]);
        var set = new WorldVisibilitySet(world, observer, filter);
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 10);
        });
        Assert.Empty(Refresh(executor, set).Entered);
        EntityEffectHandle temporary = default;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagGranted,
            world.GrantTag(ctx, target, new EntityEffectSource(11), "npc.elite",
                out temporary)));
        Assert.Equal([target], Refresh(executor, set).Entered.Select(e => e.Identity));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagChanged,
            world.SetPermanentTag(ctx, target, "hidden", true)));
        Assert.Equal([target], Refresh(executor, set).Left);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagChanged,
            world.SetPermanentTag(ctx, target, "hidden", false)));
        Assert.Single(Refresh(executor, set).Entered);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagRevoked,
            world.RevokeTag(ctx, target, temporary)));
        Assert.Equal([target], Refresh(executor, set).Left);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagChanged,
            world.SetPermanentTag(ctx, target, "npc.elite", true)));
        Assert.Single(Refresh(executor, set).Entered);
    }

    [Fact]
    public void ExcludedControlStateFiltersAndLiftsAfterFinalStunSource()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(50),
                excludedControls: EntityControlEffects.Stunned));
        EntityEffectHandle a = default;
        EntityEffectHandle b = default;
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 10);
        });
        Assert.Single(Refresh(executor, set).Entered);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, target, new EntityEffectSource(1),
                    EntityControlEffects.Stunned, out a));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, target, new EntityEffectSource(2),
                    EntityControlEffects.Stunned, out b));
        });
        Assert.Equal([target], Refresh(executor, set).Left);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.ControlRevoked,
            world.RevokeControl(ctx, target, a)));
        Assert.Empty(Refresh(executor, set).Entered);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.ControlRevoked,
            world.RevokeControl(ctx, target, b)));
        Assert.Equal([target], Refresh(executor, set).Entered.Select(e => e.Identity));
    }

    [Fact]
    public void LifeFilterAndObserverDeathHaveDistinctVisibilitySemantics()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var living = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(60), requireAlive: true));
        var corpses = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(60), requireAlive: false));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 10);
        });
        Assert.Single(Refresh(executor, living).Entered);
        Assert.Single(Refresh(executor, corpses).Entered);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.LifeChanged,
            world.MarkDead(ctx, target)));
        Assert.Equal([target], Refresh(executor, living).Left);
        Assert.Empty(Refresh(executor, corpses).Left);
        Assert.Equal(LifeState.Dead, Assert.Single(corpses.Current.Members).Life);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.LifeChanged,
            world.MarkDead(ctx, observer)));
        Assert.Empty(Refresh(executor, living).View.Members);
        Assert.Equal([target], Refresh(executor, corpses).Left);
        Assert.False(corpses.Current.ObserverActive);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.LifeChanged, world.Revive(ctx, observer));
            Assert.Equal(WorldEntityResult.LifeChanged, world.Revive(ctx, target));
        });
        Assert.Single(Refresh(executor, living).Entered);
        Assert.Single(Refresh(executor, corpses).Entered);
    }

    [Fact]
    public void DespawnAndReusedEntityIdYieldLeaveAndNewGenerationEnter()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var old = Id(2);
        var next = Id(2, 2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(50)));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, old, 10);
        });
        Assert.Equal([old], Refresh(executor, set).Entered.Select(e => e.Identity));
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Despawned, world.Despawn(ctx, old));
            Spawn(world, ctx, next, 10);
        });
        var delta = Refresh(executor, set);
        Assert.Equal([old], delta.Left);
        Assert.Equal([next], delta.Entered.Select(e => e.Identity));
        Assert.Equal((ulong)2, delta.SnapshotVersion);
        Assert.Empty(Refresh(executor, set).Entered);
        Assert.Empty(Refresh(executor, set).Left);
    }

    [Fact]
    public void OldObserverGenerationNeverObservesReplacementAfterRetirement()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var replacement = Id(1, 2);
        var target = Id(2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(30)));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 4);
        });
        Assert.Single(Refresh(executor, set).Entered);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, observer));
            Spawn(world, ctx, replacement, 0);
        });
        var delta = Refresh(executor, set);
        Assert.Equal([target], delta.Left);
        Assert.False(delta.View.ObserverActive);
        Assert.Empty(Refresh(executor, set).Entered);
    }

    [Fact]
    public void PastViewAndDeltaTagsCannotChangeAfterWorldMutation()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(30)));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 10);
            Assert.Equal(WorldEntityResult.TagChanged,
                world.SetPermanentTag(ctx, target, "npc", true));
        });
        var first = Refresh(executor, set);
        var member = Assert.Single(first.Entered);
        Assert.Equal(["npc"], member.Tags);
        Assert.Same(first.View, set.Current);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.TagChanged,
                world.SetPermanentTag(ctx, target, "npc", false));
            Assert.Equal(WorldEntityResult.TagChanged,
                world.SetPermanentTag(ctx, target, "enemy", true));
            Move(world, ctx, target, 12);
        });
        var updated = Refresh(executor, set);
        Assert.Empty(updated.Entered);
        Assert.Empty(updated.Left);
        Assert.Equal(new Position(12, 0, 0), Assert.Single(updated.View.Members).Transform.Position);
        Assert.Equal(["enemy"], Assert.Single(updated.View.Members).Tags);
        Assert.Equal(["npc"], member.Tags);
        Assert.Equal(new Position(10, 0, 0), Assert.Single(first.View.Members).Transform.Position);
        Assert.Equal((ulong)1, first.SnapshotVersion);
        Assert.Equal((ulong)2, updated.SnapshotVersion);
        Assert.IsNotType<WorldEntitySnapshot>(member);
    }

    [Fact]
    public void CapacityExceededDoesNotPublishPartialViewOrAdvanceVersion()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var a = Id(2);
        var b = Id(3);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(40), maximumVisible: 1));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, a, 1);
            Spawn(world, ctx, b, 2);
            Assert.Throws<InvalidOperationException>(() => set.Refresh(ctx));
        });
        Assert.Equal((ulong)0, set.Current.SnapshotVersion);
        Assert.Empty(set.Current.Members);
        Assert.Equal(WorldLifecycle.Running, world.State);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, b)));
        Assert.Equal([a], Refresh(executor, set).Entered.Select(e => e.Identity));
    }

    [Fact]
    public void InvalidFilterConfigurationsAndForeignObserverAreRejected()
    {
        var (world, _) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldVisibilityFilter(new Distance(4097)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldVisibilityFilter(new Distance(1), maximumVisible: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldVisibilityFilter(new Distance(1), maximumVisible: 1025));
        Assert.Throws<ArgumentException>(() =>
            new WorldVisibilityFilter(new Distance(1), requiredTags: ["Enemy"]));
        Assert.Throws<ArgumentException>(() =>
            new WorldVisibilityFilter(new Distance(1), requiredTags: ["npc"],
                excludedTags: ["npc"]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldVisibilityFilter(new Distance(1), excludedControls: (EntityControlEffects)128));
        Assert.Throws<ArgumentException>(() =>
            new WorldVisibilitySet(world, Id(1, world: 2),
                new WorldVisibilityFilter(new Distance(1))));
        Assert.Throws<ArgumentException>(() =>
            new WorldVisibilitySet(world, Id(1, epoch: 2),
                new WorldVisibilityFilter(new Distance(1))));
    }

    [Fact]
    public void LeaseCannotBeCapturedForLaterRefreshAndWrongWorldIsRejected()
    {
        var (world, executor) = Create();
        var set = new WorldVisibilitySet(world, Id(1),
            new WorldVisibilityFilter(new Distance(3)));
        IWorldWriteContext? leaked = null;
        Run(executor, ctx =>
        {
            Spawn(world, ctx, Id(1), 0);
            leaked = ctx;
            Assert.Empty(set.Refresh(ctx).Entered);
        });
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() => set.Refresh(leaked!)).Fault);
        Assert.Equal((ulong)1, set.Current.SnapshotVersion);

        var alienExecutor = new WorldExecutor(new WorldId(2), new WorldEpoch(1),
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        WorldAccessFault? fault = null;
        Run(alienExecutor, ctx =>
        {
            try
            {
                set.Refresh(ctx);
            }
            catch (WorldAccessException ex)
            {
                fault = ex.Fault;
            }
        });
        Assert.Equal(WorldAccessFault.InvalidGeneration, fault);
        Assert.Equal((ulong)1, set.Current.SnapshotVersion);
    }

    [Fact]
    public void FaultedWorldRefusesFreshVisibilityAndPreservesLastGoodView()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(50)));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 3);
        });
        var valid = Refresh(executor, set);
        Run(executor, ctx =>
        {
            Assert.True(world.Fault(ctx));
            Assert.Throws<InvalidOperationException>(() => set.Refresh(ctx));
        });
        Assert.Equal((ulong)1, set.Current.SnapshotVersion);
        Assert.Same(valid.View, set.Current);
    }

    [Fact]
    public void StopTriggersOneLeaveForAllAndViewBecomesInactive()
    {
        var (world, executor) = Create();
        var observer = Id(1);
        var target = Id(2);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(50)));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, observer, 0);
            Spawn(world, ctx, target, 3);
        });
        Assert.Single(Refresh(executor, set).Entered);
        WorldVisibilityDelta? stopped = null;
        Run(executor, ctx =>
        {
            Assert.True(world.BeginDrain(ctx));
            Assert.True(world.Stop(ctx));
            stopped = set.Refresh(ctx);
        });
        Assert.NotNull(stopped);
        Assert.Equal([target], stopped.Left);
        Assert.Empty(stopped.View.Members);
        Assert.False(stopped.View.ObserverActive);
        Assert.Empty(Refresh(executor, set).Left);
    }

    [Fact]
    public void TwoObserversHaveIndependentVersioningAndDistances()
    {
        var (world, executor) = Create();
        var a = Id(1);
        var b = Id(2);
        var target = Id(3);
        var one = new WorldVisibilitySet(world, a,
            new WorldVisibilityFilter(new Distance(20)));
        var two = new WorldVisibilitySet(world, b,
            new WorldVisibilityFilter(new Distance(20)));
        Run(executor, ctx =>
        {
            Spawn(world, ctx, a, 0);
            Spawn(world, ctx, b, 80);
            Spawn(world, ctx, target, 10);
        });
        Assert.Equal([target], Refresh(executor, one).Entered.Select(m => m.Identity));
        Assert.Empty(Refresh(executor, two).Entered);
        Run(executor, ctx => Move(world, ctx, target, 75));
        Assert.Equal([target], Refresh(executor, one).Left);
        Assert.Equal([target], Refresh(executor, two).Entered.Select(m => m.Identity));
        Assert.Equal((ulong)2, one.Current.SnapshotVersion);
        Assert.Equal((ulong)2, two.Current.SnapshotVersion);
    }

    [Fact]
    public void ViewAndDeltaAlwaysCarryWorldEpochObserverAndConsecutiveVersion()
    {
        var (world, executor) = Create();
        var observer = Id(4);
        var set = new WorldVisibilitySet(world, observer,
            new WorldVisibilityFilter(new Distance(1)));
        Run(executor, ctx => Spawn(world, ctx, observer, 0));
        var first = Refresh(executor, set);
        var second = Refresh(executor, set);
        Assert.Equal(world.World, second.World);
        Assert.Equal(world.Epoch, second.Epoch);
        Assert.Equal(observer, second.Observer);
        Assert.Equal(first.SnapshotVersion, second.PreviousVersion);
        Assert.Equal(first.SnapshotVersion + 1, second.SnapshotVersion);
        Assert.Equal(second.SnapshotVersion, second.View.SnapshotVersion);
        Assert.Equal(world.World, second.View.World);
        Assert.Equal(world.Epoch, second.View.Epoch);
        Assert.Equal(observer, second.View.Observer);
    }

    private sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => action(context);
    }
}
