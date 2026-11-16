using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldControlOwnershipTests
{
    private static (WorldInstance World, WorldExecutor Executor) Create()
    {
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });
        return (world, executor);
    }

    private static EntityIdentity Id(ulong id, ulong generation = 1, ulong world = 1,
        ulong epoch = 1) =>
        new(new WorldId(world), new WorldEpoch(epoch),
            new EntityId(id), new EntityGeneration(generation));

    private static WorldEntitySeed Seed(EntityIdentity id) =>
        new(id, new Transform(new Position((double)id.Id.Value, 0, 0), new Angle(0)));

    private static EntityEffectSource Source(ulong value) => new(value);

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> body)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(body)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    [Fact]
    public void TwoIndependentStunSourcesRequireTwoIndependentRevocations()
    {
        var (world, executor) = Create();
        var id = Id(10);
        EntityEffectHandle first = default, second = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, id, Source(1), EntityControlFlag.Stunned, out first));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, id, Source(2), EntityControlFlag.Stunned, out second));
            Assert.Equal(WorldEntityResult.AlreadyExists,
                world.GrantControl(ctx, id, Source(1), EntityControlFlag.Stunned, out var replay));
            Assert.Equal(first, replay);
        });
        Assert.NotEqual(first, second);
        Assert.True(world.TryGetSnapshot(id.Id, out var snapshot));
        Assert.Equal(EntityControlFlag.Stunned, snapshot.Control);

        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ControlRevoked, world.RevokeControl(ctx, id, first));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.RevokeControl(ctx, id, first));
        });
        Assert.True(world.TryGetSnapshot(id.Id, out snapshot));
        Assert.Equal(EntityControlFlag.Stunned, snapshot.Control);

        Run(executor, ctx => Assert.Equal(WorldEntityResult.ControlRevoked,
            world.RevokeControl(ctx, id, second)));
        Assert.True(world.TryGetSnapshot(id.Id, out snapshot));
        Assert.Equal(EntityControlFlag.None, snapshot.Control);
    }

    [Fact]
    public void DifferentControlKindsAreIndependentlyReversible()
    {
        var (world, executor) = Create();
        var id = Id(1);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, id, Source(4), EntityControlFlag.Rooted, out var rooted));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, id, Source(4), EntityControlFlag.Silenced, out _));
            Assert.Equal(EntityControlFlag.Rooted | EntityControlFlag.Silenced,
                worldSnapshot().Control);
            Assert.Equal(WorldEntityResult.ControlRevoked, world.RevokeControl(ctx, id, rooted));
        });
        Assert.Equal(EntityControlFlag.Silenced, worldSnapshot().Control);

        WorldEntitySnapshot worldSnapshot()
        {
            Assert.True(world.TryGetSnapshot(id.Id, out var snapshot));
            return snapshot;
        }
    }

    [Fact]
    public void TagSourcesAndPermanentTagsDoNotCancelOtherSources()
    {
        var (world, executor) = Create();
        var id = Id(2);
        EntityEffectHandle a = default, b = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.TagChanged, world.SetPermanentTag(ctx, id, "npc", true));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.SetPermanentTag(ctx, id, "npc", true));
            Assert.Equal(WorldEntityResult.TagGranted, world.GrantTag(ctx, id, Source(1), "stunned",
                out a));
            Assert.Equal(WorldEntityResult.TagGranted, world.GrantTag(ctx, id, Source(2), "stunned",
                out b));
            Assert.Equal(WorldEntityResult.AlreadyExists, world.GrantTag(ctx, id, Source(1),
                "stunned", out var replay));
            Assert.Equal(a, replay);
            Assert.Equal(EntityEffectKind.Tag, a.Kind);
        });
        Assert.True(world.TryGetSnapshot(id.Id, out var initial));
        Assert.Equal(["npc", "stunned"], initial.Tags);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.TagRevoked, world.RevokeTag(ctx, id, a));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.RevokeTag(ctx, id, a));
        });
        Assert.Equal(["npc", "stunned"], world.QueryByTag("stunned").Single().Tags);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.TagRevoked, world.RevokeTag(ctx, id, b));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.SetPermanentTag(ctx, id, "missing", false));
        });
        Assert.Empty(world.QueryByTag("stunned"));
        Assert.Equal(["npc"], world.QueryByTag("npc").Single().Tags);
        // The already-exported snapshot must never mutate when later sources are revoked.
        Assert.Equal(["npc", "stunned"], initial.Tags);
    }

    [Fact]
    public void SameSequenceAcrossControlAndTagCannotRevokeWrongCapability()
    {
        var (world, executor) = Create();
        var id = Id(3);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, id, Source(1), EntityControlFlag.Stunned, out var c));
            Assert.Equal(WorldEntityResult.TagGranted, world.GrantTag(ctx, id, Source(1), "a", out var t));
            Assert.Equal(c.Sequence, t.Sequence);
            Assert.NotEqual(c, t);
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.RevokeControl(ctx, id, t));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.RevokeTag(ctx, id, c));
            Assert.True(world.TryGetSnapshot(id.Id, out var snapshot));
            Assert.Equal(EntityControlFlag.Stunned, snapshot.Control);
            Assert.Equal(["a"], snapshot.Tags);
        });
    }

    [Fact]
    public void TagQueryIsOrdinalSortedImmutableAndOnlyActive()
    {
        var (world, executor) = Create();
        var a = Id(50);
        var b = Id(2);
        var inactive = Id(4);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(a)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(b)));
            Assert.Equal(WorldEntityResult.Added, world.Add(ctx, Seed(inactive)));
            Assert.Equal(WorldEntityResult.TagChanged, world.SetPermanentTag(ctx, a, "enemy.elite", true));
            Assert.Equal(WorldEntityResult.TagChanged, world.SetPermanentTag(ctx, b, "enemy.elite", true));
        });
        var before = world.QueryByTag("enemy.elite");
        Assert.Equal([2UL, 50UL], before.Select(x => x.Identity.Id.Value));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagChanged,
            world.SetPermanentTag(ctx, b, "enemy.elite", false)));
        Assert.Equal([50UL], world.QueryByTag("enemy.elite").Select(x => x.Identity.Id.Value));
        Assert.Equal([2UL, 50UL], before.Select(x => x.Identity.Id.Value));
        Assert.Equal(["enemy.elite"], before[0].Tags);
    }

    [Fact]
    public void FourRelationRolesAreDistinctAndReplaceOnlySelectedRole()
    {
        var (world, executor) = Create();
        var child = Id(5);
        var owner = Id(10);
        var creator = Id(11);
        var controller = Id(12);
        var summoner = Id(13);
        Run(executor, ctx =>
        {
            foreach (var id in new[] { child, owner, creator, controller, summoner })
            {
                Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            }

            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, child, EntityRelationKind.Owner, owner));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, child, EntityRelationKind.Creator, creator));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, child, EntityRelationKind.Controller, controller));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, child, EntityRelationKind.Summoner, summoner));
            Assert.Equal(WorldEntityResult.AlreadyCompleted,
                world.SetRelation(ctx, child, EntityRelationKind.Owner, owner));
        });
        Assert.True(world.TryGetSnapshot(child.Id, out var s));
        Assert.Equal(new EntityRelations(owner, creator, controller, summoner), s.Relationships);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.RelationChanged,
            world.SetRelation(ctx, child, EntityRelationKind.Controller, null)));
        Assert.True(world.TryGetSnapshot(child.Id, out s));
        Assert.Equal(new EntityRelations(owner, creator, null, summoner), s.Relationships);
    }

    [Fact]
    public void CrossRoleCyclesIncludingLongerAndSelfCycleAreRejected()
    {
        var (world, executor) = Create();
        var a = Id(1);
        var b = Id(2);
        var c = Id(3);
        Run(executor, ctx =>
        {
            foreach (var id in new[] { a, b, c })
            {
                Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            }

            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, a, EntityRelationKind.Owner, b));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, b, EntityRelationKind.Creator, c));
            Assert.Equal(WorldEntityResult.RelationCycle,
                world.SetRelation(ctx, c, EntityRelationKind.Summoner, a));
            Assert.Equal(WorldEntityResult.RelationCycle,
                world.SetRelation(ctx, a, EntityRelationKind.Controller, a));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, b, EntityRelationKind.Creator, null));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, c, EntityRelationKind.Summoner, a));
        });
        Assert.True(world.TryGetSnapshot(c.Id, out var snapshot));
        Assert.Equal(a, snapshot.Relationships.Summoner);
    }

    [Fact]
    public void WrongWorldOldEpochAndGenerationCannotBeRelationshipTargets()
    {
        var (world, executor) = Create();
        var source = Id(1);
        var target = Id(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(source)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(target)));
            Assert.Equal(WorldEntityResult.RelationUnavailable,
                world.SetRelation(ctx, source, EntityRelationKind.Owner, Id(2, world: 2)));
            Assert.Equal(WorldEntityResult.RelationUnavailable,
                world.SetRelation(ctx, source, EntityRelationKind.Owner, Id(2, epoch: 2)));
            Assert.Equal(WorldEntityResult.RelationUnavailable,
                world.SetRelation(ctx, source, EntityRelationKind.Owner, Id(2, generation: 2)));
            Assert.Equal(WorldEntityResult.RelationUnavailable,
                world.SetRelation(ctx, source, EntityRelationKind.Owner, Id(99)));
        });
        Assert.True(world.TryGetSnapshot(source.Id, out var snapshot));
        Assert.Equal(default, snapshot.Relationships);
    }

    [Fact]
    public void DestroyedTargetClearsAllInboundRolesAndOwnOutgoingReferences()
    {
        var (world, executor) = Create();
        var owner = Id(1);
        var a = Id(2);
        var b = Id(3);
        var creator = Id(4);
        Run(executor, ctx =>
        {
            foreach (var id in new[] { owner, a, b, creator })
            {
                Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            }

            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, a, EntityRelationKind.Owner, owner));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, b, EntityRelationKind.Controller, owner));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, b, EntityRelationKind.Summoner, owner));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, owner, EntityRelationKind.Creator, creator));
            Assert.Equal(WorldEntityResult.Despawned, world.Despawn(ctx, owner));
        });
        foreach (var id in new[] { owner, a, b, creator })
        {
            Assert.True(world.TryGetSnapshot(id.Id, out var snapshot));
            Assert.Equal(default, snapshot.Relationships);
        }
    }

    [Fact]
    public void StaleGenerationEffectHandleCannotRevokeReusedEntity()
    {
        var (world, executor) = Create();
        var first = Id(4);
        var next = Id(4, 2);
        EntityEffectHandle previous = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(first)));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, first, Source(1), EntityControlFlag.Stunned, out previous));
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, first));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(next)));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, next, Source(1), EntityControlFlag.Stunned, out _));
            Assert.Equal(WorldEntityResult.Stale, world.RevokeControl(ctx, next, previous));
            Assert.Equal(WorldEntityResult.Stale, world.RevokeControl(ctx, first, previous));
        });
        Assert.True(world.TryGetSnapshot(next.Id, out var result));
        Assert.Equal(EntityControlFlag.Stunned, result.Control);
    }

    [Fact]
    public void RemovingAndShutdownClearAllCapabilityHandlesAndRelations()
    {
        var (world, executor) = Create();
        var child = Id(1);
        var owner = Id(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(child)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(owner)));
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, child, Source(1), EntityControlFlag.Feared, out _));
            Assert.Equal(WorldEntityResult.TagGranted,
                world.GrantTag(ctx, child, Source(2), "minion", out _));
            Assert.Equal(WorldEntityResult.RelationChanged,
                world.SetRelation(ctx, child, EntityRelationKind.Owner, owner));
            Assert.True(world.BeginDrain(ctx));
            Assert.True(world.Stop(ctx));
        });
        Assert.True(world.TryGetSnapshot(child.Id, out var snapshot));
        Assert.Equal(WorldEntityLifecycle.Disposed, snapshot.Lifecycle);
        Assert.Equal(EntityControlFlag.None, snapshot.Control);
        Assert.Empty(snapshot.Tags);
        Assert.Equal(default, snapshot.Relationships);
    }

    [Fact]
    public void ControlAndTagsAreCappedAndDoNotOverwriteOtherSources()
    {
        var (world, executor) = Create();
        var id = Id(7);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            for (ulong i = 1; i <= 64; i++)
            {
                Assert.Equal(WorldEntityResult.ControlGranted,
                    world.GrantControl(ctx, id, Source(i), EntityControlFlag.Rooted, out _));
                Assert.Equal(WorldEntityResult.TagGranted,
                    world.GrantTag(ctx, id, Source(i), "busy", out _));
            }
            Assert.Equal(WorldEntityResult.CapacityExceeded,
                world.GrantControl(ctx, id, Source(65), EntityControlFlag.Rooted, out _));
            Assert.Equal(WorldEntityResult.CapacityExceeded,
                world.GrantTag(ctx, id, Source(65), "busy", out _));
        });
        Assert.True(world.TryGetSnapshot(id.Id, out var snapshot));
        Assert.Equal(EntityControlFlag.Rooted, snapshot.Control);
        Assert.Equal(["busy"], snapshot.Tags);
    }

    [Fact]
    public void InvalidTagInputsAreRejectedWithoutLeakingMutableState()
    {
        foreach (var bad in new[] { "", "UPPER", " no", "a b", new string('a', 49), ":bad", "a😀" })
        {
            Assert.Throws<ArgumentException>(() => EntityTagCapability.ValidateTag(bad));
        }

        EntityTagCapability.ValidateTag("npc.elite-boss:01");
        Assert.Throws<ArgumentException>(() => new EntityEffectHandle(default, 0, EntityEffectKind.Tag));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityEffectSource(0));
    }

    [Fact]
    public void ExpiredP18WorldLeaseCannotGrantEffectsOrWriteRelationships()
    {
        var (world, executor) = Create();
        var id = Id(1);
        IWorldWriteContext? captured = null;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            captured = ctx;
        });
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.GrantControl(captured!, id, Source(1), EntityControlFlag.Rooted, out _)).Fault);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.SetPermanentTag(captured!, id, "npc", true)).Fault);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.SetRelation(captured!, id, EntityRelationKind.Owner, null)).Fault);
    }

    [Fact]
    public void ControlTagAndRelationshipChangesAffectDeterministicWorldTickHash()
    {
        var (world, executor) = Create();
        var id = Id(1);
        var owner = Id(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(owner)));
        });
        var clock = new FakeClock(new Duration(50),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new WorldTickPipeline(world, clock, []);
        clock.AdvanceTicks(4);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
        var first = pipeline.LastStateHash;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.ControlGranted,
            world.GrantControl(ctx, id, Source(1), EntityControlFlag.Stunned, out _)));
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(2)));
        var control = pipeline.LastStateHash;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.TagChanged,
            world.SetPermanentTag(ctx, id, "npc", true)));
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(3)));
        var tagged = pipeline.LastStateHash;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.RelationChanged,
            world.SetRelation(ctx, id, EntityRelationKind.Owner, owner)));
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(4)));
        var related = pipeline.LastStateHash;
        Assert.Equal(4, new[] { first, control, tagged, related }.Distinct().Count());
    }

    private sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => action(context);
    }
}
