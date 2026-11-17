using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class EntityAttributeTests
{
    private static readonly AttributeKey Power = new("power");
    private static readonly AttributeKey Damage = new("damage");
    private static readonly AttributeKey HealthMax = new("health.max");
    private static readonly AttributeKey Health = new("health");

    private static (WorldInstance World, WorldExecutor Executor, EntityIdentity Identity) Create(
        ulong generation = 1)
    {
        var world = new WorldInstance(new WorldId(11), new WorldEpoch(7),
            new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });
        var identity = Id(generation);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Spawned,
            world.Spawn(ctx, new WorldEntitySeed(identity,
                new Transform(new Position(1, 2, 3), new Angle(0))))));
        return (world, executor, identity);
    }

    private static EntityIdentity Id(ulong generation = 1) =>
        new(new WorldId(11), new WorldEpoch(7), new EntityId(5), new EntityGeneration(generation));

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> callback)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new ActionWork(callback)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    private sealed class ActionWork(Action<IWorldWriteContext> callback) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => callback(context);
    }

    private static EntityAttributeSnapshot Snapshot(WorldInstance world, EntityIdentity identity)
    {
        Assert.True(world.TryGetAttributes(identity, out var snapshot));
        return Assert.IsType<EntityAttributeSnapshot>(snapshot);
    }

    private static decimal Stat(EntityAttributeSnapshot snapshot, AttributeKey key) =>
        Assert.Single(snapshot.Stats, x => x.Key == key).EffectiveValue;

    private static AttributeResourceSnapshot Resource(EntityAttributeSnapshot snapshot, AttributeKey key) =>
        Assert.Single(snapshot.Resources, x => x.Key == key);

    [Fact]
    public void StablePriorityAndDerivedStatsRecomputeFromSource()
    {
        var (world, executor, identity) = Create();
        AttributeModifierHandle a = default;
        AttributeModifierHandle b = default;
        AttributeModifierHandle c = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.AttributeChanged, world.SetBaseAttribute(ctx, identity, Power, 100m));
            Assert.Equal(WorldEntityResult.AttributeChanged,
                world.DefineDerivedAttribute(ctx, identity, Damage, Power, 2m, 5m));
            Assert.Equal(WorldEntityResult.AlreadyCompleted,
                world.DefineDerivedAttribute(ctx, identity, Damage, Power, 2m, 5m));
            Assert.Equal(WorldEntityResult.ModifierAdded, world.GrantAttributeModifier(ctx, identity,
                new AttributeModifierSource(1), Power, AttributeModifierOperation.Flat, 10m, 0, out a));
            Assert.Equal(WorldEntityResult.ModifierAdded, world.GrantAttributeModifier(ctx, identity,
                new AttributeModifierSource(2), Power, AttributeModifierOperation.Percent, 50m, 10, out b));
            Assert.Equal(WorldEntityResult.ModifierAdded, world.GrantAttributeModifier(ctx, identity,
                new AttributeModifierSource(3), Damage, AttributeModifierOperation.Flat, 7m, 5, out c));
        });
        var frozen = Snapshot(world, identity);
        Assert.Equal(165m, Stat(frozen, Power));
        Assert.Equal(342m, Stat(frozen, Damage));
        Assert.Equal(new ulong[] { 1, 2, 3 }, frozen.Modifiers.Select(m => m.Handle.Sequence));
        Assert.Equal(identity, a.Identity);
        Assert.Equal(3UL, frozen.LastModifierSequence);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ModifierRemoved, world.RevokeAttributeModifier(ctx, identity, a));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.RevokeAttributeModifier(ctx, identity, a));
        });
        var updated = Snapshot(world, identity);
        Assert.Equal(150m, Stat(updated, Power));
        Assert.Equal(312m, Stat(updated, Damage));
        Assert.Equal(165m, Stat(frozen, Power));
        Assert.Equal(342m, Stat(frozen, Damage));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<AttributeModifierSnapshot>)updated.Modifiers).Clear());
        Assert.Equal(2, updated.Modifiers.Count);
        Assert.Equal(b, updated.Modifiers[0].Handle);
        Assert.Equal(c, updated.Modifiers[1].Handle);
    }

    [Fact]
    public void MaximumShrinksCurrentWithoutHealingAfterModifierRemoval()
    {
        var (world, executor, identity) = Create();
        AttributeModifierHandle lowering = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.AttributeChanged,
                world.SetBaseAttribute(ctx, identity, HealthMax, 100m));
            Assert.Equal(WorldEntityResult.AttributeChanged,
                world.DefineEntityResource(ctx, identity, Health, HealthMax, 80m));
            Assert.Equal(WorldEntityResult.ModifierAdded,
                world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(1),
                    HealthMax, AttributeModifierOperation.Flat, -60m, 0, out lowering));
        });
        Assert.Equal((40m, 40m),
            (Resource(Snapshot(world, identity), Health).Maximum,
             Resource(Snapshot(world, identity), Health).Current));
        Run(executor, ctx => Assert.Equal(WorldEntityResult.ModifierRemoved,
            world.RevokeAttributeModifier(ctx, identity, lowering)));
        var after = Resource(Snapshot(world, identity), Health);
        Assert.Equal(100m, after.Maximum);
        Assert.Equal(40m, after.Current);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ResourceOutOfRange,
                world.ChangeEntityResource(ctx, identity, Health, -41m));
            Assert.Equal(WorldEntityResult.ResourceOutOfRange,
                world.ChangeEntityResource(ctx, identity, Health, 61m));
            Assert.Equal(WorldEntityResult.ResourceChanged,
                world.ChangeEntityResource(ctx, identity, Health, 60m));
            Assert.Equal(WorldEntityResult.AlreadyCompleted,
                world.ChangeEntityResource(ctx, identity, Health, 0m));
        });
        Assert.Equal(100m, Resource(Snapshot(world, identity), Health).Current);
    }

    [Fact]
    public void DistinctSourcesAndRepeatedRemovalNeverEraseAnotherSource()
    {
        var (world, executor, identity) = Create();
        AttributeModifierHandle one = default;
        AttributeModifierHandle two = default;
        AttributeModifierHandle three = default;
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, identity, Power, 10m);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(1),
                Power, AttributeModifierOperation.Flat, 5m, 0, out one);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(2),
                Power, AttributeModifierOperation.Flat, 6m, 0, out two);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(1),
                Power, AttributeModifierOperation.Flat, 7m, 0, out three);
        });
        Assert.Equal(28m, Stat(Snapshot(world, identity), Power));
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ModifierRemoved,
                world.RevokeAttributeSource(ctx, identity, new AttributeModifierSource(1), out var removed));
            Assert.Equal(2, removed);
            Assert.Equal(WorldEntityResult.AlreadyCompleted,
                world.RevokeAttributeSource(ctx, identity, new AttributeModifierSource(1), out removed));
            Assert.Equal(0, removed);
            Assert.Equal(WorldEntityResult.AlreadyCompleted,
                world.RevokeAttributeModifier(ctx, identity, one));
            Assert.Equal(WorldEntityResult.AlreadyCompleted,
                world.RevokeAttributeModifier(ctx, identity, three));
        });
        var snapshot = Snapshot(world, identity);
        Assert.Equal(16m, Stat(snapshot, Power));
        Assert.Single(snapshot.Modifiers);
        Assert.Equal(two, snapshot.Modifiers[0].Handle);
        Assert.Equal(3UL, snapshot.LastModifierSequence);
    }

    [Fact]
    public void RoundAwayFromZeroAndFixedPrioritySequenceAreDeterministic()
    {
        var (world, executor, identity) = Create();
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, identity, Power, 1.005m);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(1),
                Power, AttributeModifierOperation.Percent, 50m, 0, out _);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(2),
                Power, AttributeModifierOperation.Flat, 0.005m, 0, out _);
        });
        // 1.005 -> 1.01; 50% -> 1.52; +0.005 -> 1.53.
        Assert.Equal(1.53m, Stat(Snapshot(world, identity), Power));
    }

    [Fact]
    public void FailedRecomputeDoesNotPublishPartialStateOrConsumeHandle()
    {
        var (world, executor, identity) = Create();
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, identity, Power, 1_000_000_000m);
            Assert.Throws<OverflowException>(() =>
                world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(1),
                    Power, AttributeModifierOperation.Flat, 1m, 0, out _));
            Assert.Throws<OverflowException>(() =>
                world.DefineDerivedAttribute(ctx, identity, Damage, Power, 2m, 0m));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                world.SetBaseAttribute(ctx, identity, Power, -1m));
            Assert.Throws<ArgumentException>(() =>
                world.SetBaseAttribute(ctx, identity, default, 4m));
        });
        var snapshot = Snapshot(world, identity);
        Assert.Equal(1_000_000_000m, Stat(snapshot, Power));
        Assert.Single(snapshot.Stats);
        Assert.Empty(snapshot.Modifiers);
        Assert.Equal(0UL, snapshot.LastModifierSequence);
        Run(executor, ctx =>
        {
            var handle = default(AttributeModifierHandle);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(2),
                Power, AttributeModifierOperation.Flat, -2m, 0, out handle);
            Assert.Equal(1UL, handle.Sequence);
        });
    }

    [Fact]
    public void OldGenerationAndExpiredLeaseCannotMutateCurrentAttributes()
    {
        var (world, executor, oldIdentity) = Create();
        var newIdentity = Id(2);
        AttributeModifierHandle oldHandle = default;
        IWorldWriteContext? expired = null;
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, oldIdentity, Power, 50m);
            world.GrantAttributeModifier(ctx, oldIdentity, new AttributeModifierSource(1),
                Power, AttributeModifierOperation.Flat, 10m, 0, out oldHandle);
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, oldIdentity));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, new WorldEntitySeed(
                newIdentity, new Transform(new Position(3, 4, 5), new Angle(0)))));
            world.SetBaseAttribute(ctx, newIdentity, Power, 25m);
            expired = ctx;
            Assert.Equal(WorldEntityResult.Stale, world.RevokeAttributeModifier(ctx, newIdentity, oldHandle));
            Assert.Equal(WorldEntityResult.Stale, world.SetBaseAttribute(ctx, oldIdentity, Power, 1m));
        });
        Assert.False(world.TryGetAttributes(oldIdentity, out _));
        Assert.Equal(25m, Stat(Snapshot(world, newIdentity), Power));
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.SetBaseAttribute(expired!, newIdentity, Power, 99m)).Fault);
    }

    [Fact]
    public void DeathBlocksResourceChangesAndResurrectionReenables()
    {
        var (world, executor, identity) = Create();
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, identity, HealthMax, 30m);
            world.DefineEntityResource(ctx, identity, Health, HealthMax, 20m);
            Assert.Equal(WorldEntityResult.LifeChanged, world.MarkDead(ctx, identity));
            Assert.Equal(WorldEntityResult.InvalidState,
                world.ChangeEntityResource(ctx, identity, Health, -3m));
            Assert.Equal(WorldEntityResult.InvalidState,
                world.SetEntityResource(ctx, identity, Health, 30m));
            Assert.Equal(WorldEntityResult.LifeChanged, world.Revive(ctx, identity));
            Assert.Equal(WorldEntityResult.ResourceChanged,
                world.ChangeEntityResource(ctx, identity, Health, 5m));
        });
        Assert.Equal(25m, Resource(Snapshot(world, identity), Health).Current);
    }

    [Fact]
    public void EqualEffectiveValueDifferentSourcesHaveDifferentTickHashes()
    {
        static string Digest(ulong source)
        {
            var (world, executor, identity) = Create();
            Run(executor, ctx =>
            {
                world.SetBaseAttribute(ctx, identity, Power, 15m);
                world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(source),
                    Power, AttributeModifierOperation.Flat, 5m, 0, out _);
            });
            var clock = new FakeClock(new Duration(50),
                new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
            var pipeline = new WorldTickPipeline(world, clock, []);
            clock.AdvanceTicks(1);
            Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
            return pipeline.LastStateHash;
        }

        Assert.Equal(Digest(1), Digest(1));
        Assert.NotEqual(Digest(1), Digest(2));
    }

    [Fact]
    public void IdenticalCanonicalSnapshotsCompareByValueNotArrayIdentity()
    {
        var (world, executor, identity) = Create();
        var emptyA = Snapshot(world, identity);
        var emptyB = Snapshot(world, identity);
        Assert.Equal(emptyA, emptyB);
        Assert.Equal(emptyA.GetHashCode(), emptyB.GetHashCode());
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, identity, Power, 10m);
            world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(7),
                Power, AttributeModifierOperation.Flat, 5m, 0, out _);
        });
        var first = Snapshot(world, identity);
        var second = Snapshot(world, identity);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(emptyA, first);
        Run(executor, ctx => world.SetBaseAttribute(ctx, identity, Power, 20m));
        Assert.NotEqual(first, Snapshot(world, identity));
    }

    [Fact]
    public void KeysAndDefinitionsRejectInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => new AttributeKey(""));
        Assert.Throws<ArgumentException>(() => new AttributeKey("Hp.Max"));
        Assert.Throws<ArgumentException>(() => new AttributeKey(new string('x', 65)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttributeModifierSource(0));
        var (world, executor, identity) = Create();
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, identity, Power, 10m);
            world.DefineDerivedAttribute(ctx, identity, Damage, Power, 2m, 1m);
            Assert.Throws<InvalidOperationException>(() =>
                world.DefineDerivedAttribute(ctx, identity, Power, Damage, 1m, 0m));
            Assert.Throws<InvalidOperationException>(() =>
                world.SetBaseAttribute(ctx, identity, Damage, 12m));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                world.DefineDerivedAttribute(ctx, identity, new AttributeKey("foo"), Power, -1m, 0m));
        });
    }
}
