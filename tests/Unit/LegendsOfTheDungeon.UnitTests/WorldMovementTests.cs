using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldMovementTests
{
    private static readonly AttributeKey Speed = WorldSpeedModel.SpeedStat;

    private static EntityIdentity Identity(ulong generation = 1) =>
        new(new WorldId(3), new WorldEpoch(8), new EntityId(7), new EntityGeneration(generation));

    private static (WorldInstance World, WorldExecutor Executor, EntityIdentity Identity) Start(bool defineSpeed = true)
    {
        var world = new WorldInstance(new WorldId(3), new WorldEpoch(8), new WorldOwnershipRegistry());
        var executor = new WorldExecutor(world.World, world.Epoch,
            new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
            Assert.Equal(WorldEntityResult.Spawned,
                world.Spawn(ctx, new WorldEntitySeed(Identity(),
                    new Transform(new Position(31.75, 0, 0), new Angle(0)))));
            if (defineSpeed)
            {
                Assert.Equal(WorldEntityResult.AttributeChanged,
                    world.SetBaseAttribute(ctx, Identity(), Speed, 10m));
            }
        });
        return (world, executor, Identity());
    }

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new WorldAction(action)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    private sealed class WorldAction(Action<IWorldWriteContext> action) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => action(context);
    }

    private static ulong Bind(WorldInstance world, WorldExecutor executor, EntityIdentity identity,
        string subject = "alice", ulong baseline = 0)
    {
        ulong version = 0;
        Run(executor, ctx => Assert.Equal(WorldEntityResult.MovementBound,
            world.BindMovementSubject(ctx, identity, BoundIdentity.CreateLocalTest(subject), new Tick(baseline),
                new Duration(50), out version)));
        return version;
    }

    private static WorldMovementIntent Intent(EntityIdentity identity, string subject, ulong version,
        ulong sequence, ulong tick, double x, double y = 0, double z = 0) =>
        new(identity, BoundIdentity.CreateLocalTest(subject), version, sequence, new Tick(tick),
            new Transform(new Position(x, y, z), new Angle(0)));

    [Fact]
    public void RealMovementMovesCanonicalTransformAndSpatialTogether()
    {
        var (world, executor, identity) = Start();
        var version = Bind(world, executor, identity);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.MovementAccepted,
            world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 32.25))));
        Assert.True(world.TryGetSnapshot(identity.Id, out var snapshot));
        Assert.Equal(new Position(32.25, 0, 0), snapshot.Transform.Position);
        Assert.Equal(WorldMovementMode.Moving, snapshot.Movement.Mode);
        Assert.Equal(1UL, snapshot.Movement.LastSequence);
        Assert.Equal(new Tick(1), snapshot.Movement.LastTick);
        Assert.Empty(world.QueryRange(new Bounds(new Position(31.75, 0, 0), new Position(31.75, 0, 0))));
        Assert.Single(world.QueryRange(new Bounds(new Position(32.25, 0, 0), new Position(32.25, 0, 0))));
        Assert.True(world.TryGetMovement(identity, out var copied));
        Assert.Equal(snapshot.Movement, copied);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.MovementAccepted,
            world.SubmitMovement(ctx, Intent(identity, "alice", version, 2, 2, 32.25))));
        Assert.True(world.TryGetMovement(identity, out copied));
        Assert.Equal(WorldMovementMode.Idle, copied.Mode);
        Assert.Equal(new Tick(2), copied.LastTick);
    }

    [Fact]
    public void ForgedSubjectOldSequenceAndOldTickNeverConsumeBaseline()
    {
        var (world, executor, identity) = Start();
        var version = Bind(world, executor, identity);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.MovementSubjectMismatch,
                world.SubmitMovement(ctx, Intent(identity, "intruder", version, 1, 1, 32)));
            Assert.Equal(WorldEntityResult.MovementSubjectMismatch,
                world.SubmitMovement(ctx, Intent(identity, "alice", version + 1, 1, 1, 32)));
            Assert.Equal(WorldEntityResult.MovementAccepted,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 2, 1, 32)));
            Assert.Equal(WorldEntityResult.MovementOutOfOrder,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 2, 2, 32.1)));
            Assert.Equal(WorldEntityResult.MovementOutOfOrder,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 3, 1, 32.1)));
            Assert.Equal(WorldEntityResult.MovementTickGapExceeded,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 3, 6, 32.1)));
        });
        Assert.True(world.TryGetMovement(identity, out var state));
        Assert.Equal(2UL, state.LastSequence);
        Assert.Equal(new Tick(1), state.LastTick);
        Assert.Equal(new Position(32, 0, 0), world.QueryNearby(
            new Position(32, 0, 0), default)[0].Transform.Position);
    }

    [Fact]
    public void SpeedModifiersStackAndRevokeWithoutRemovingOtherSources()
    {
        var (world, executor, identity) = Start();
        var version = Bind(world, executor, identity);
        AttributeModifierHandle bonus = default;
        AttributeModifierHandle slow = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.MovementSpeedExceeded,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 32.5)));
            Assert.Equal(WorldEntityResult.ModifierAdded,
                world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(1),
                    Speed, AttributeModifierOperation.Flat, 10, 0, out bonus));
            Assert.Equal(WorldEntityResult.ModifierAdded,
                world.GrantAttributeModifier(ctx, identity, new AttributeModifierSource(2),
                    Speed, AttributeModifierOperation.Percent, -50, 10, out slow));
        });
        Assert.True(world.TryGetMovement(identity, out var state));
        Assert.Equal(10m, state.EffectiveSpeed);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ModifierRemoved,
                world.RevokeAttributeModifier(ctx, identity, slow));
            Assert.Equal(WorldEntityResult.MovementAccepted,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 32.5)));
            Assert.Equal(WorldEntityResult.ModifierRemoved,
                world.RevokeAttributeModifier(ctx, identity, bonus));
        });
        Assert.True(world.TryGetMovement(identity, out state));
        Assert.Equal(10m, state.EffectiveSpeed);
        Assert.Equal(new Position(32.5, 0, 0), world.QueryNearby(
            new Position(32.5, 0, 0), default)[0].Transform.Position);
    }

    [Fact]
    public void ControlsDeathAndSpatialUnloadedCellRejectWithoutMutation()
    {
        var (world, executor, identity) = Start();
        var version = Bind(world, executor, identity);
        EntityEffectHandle root = default;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.ControlGranted,
                world.GrantControl(ctx, identity, new EntityEffectSource(1),
                    EntityControlEffects.Rooted, out root));
            Assert.Equal(WorldEntityResult.MovementControlled,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 32)));
            Assert.Equal(WorldEntityResult.ControlRevoked, world.RevokeControl(ctx, identity, root));
            Assert.Equal(WorldEntityResult.MovementSpeedExceeded,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 100)));
        });
        Assert.True(world.TryLocateCell(new Position(32.1, 0, 0), out var nextCell));
        Run(executor, ctx =>
        {
            Assert.Equal(SpatialCellResult.Activated, world.ActivateCell(ctx, nextCell));
            Assert.Equal(SpatialCellResult.Unloaded, world.UnloadCell(ctx, nextCell));
            Assert.Equal(WorldEntityResult.CellUnloaded,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 32.1)));
        });
        Assert.True(world.TryGetMovement(identity, out var after));
        Assert.Equal(0UL, after.LastSequence);
        Assert.Equal(new Position(31.75, 0, 0), world.QueryNearby(
            new Position(31.75, 0, 0), default)[0].Transform.Position);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.LifeChanged, world.MarkDead(ctx, identity));
            Assert.Equal(WorldEntityResult.MovementUnbound,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 31.75)));
        });
    }

    [Fact]
    public void ManualTeleportAndRebindingInvalidateOldMovementSession()
    {
        var (world, executor, identity) = Start();
        var version = Bind(world, executor, identity);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.Relocated,
            world.Relocate(ctx, identity, new Transform(new Position(10, 0, 0), new Angle(0)))));
        Assert.True(world.TryGetMovement(identity, out var state));
        Assert.False(state.Bound);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.MovementUnbound,
            world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 10.5))));
        var next = Bind(world, executor, identity, "bob", baseline: 10);
        Assert.Equal(version + 1, next);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.MovementSubjectMismatch,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 11, 10.5)));
            Assert.Equal(WorldEntityResult.MovementAccepted,
                world.SubmitMovement(ctx, Intent(identity, "bob", next, 1, 11, 10.5)));
        });
        Assert.Equal(new Position(10.5, 0, 0), world.QueryNearby(
            new Position(10.5, 0, 0), default)[0].Transform.Position);
    }

    [Fact]
    public void ExpiredWorldLeaseAndRetiredGenerationCannotApplyMovement()
    {
        var (world, executor, identity) = Start();
        var version = Bind(world, executor, identity);
        IWorldWriteContext? expired = null;
        Run(executor, ctx => expired = ctx);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                world.SubmitMovement(expired!, Intent(identity, "alice", version, 1, 1, 32))).Fault);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, identity));
            Assert.Equal(WorldEntityResult.Spawned,
                world.Spawn(ctx, new WorldEntitySeed(Identity(2),
                    new Transform(new Position(1, 0, 0), new Angle(0)))));
            world.SetBaseAttribute(ctx, Identity(2), Speed, 10m);
            Assert.Equal(WorldEntityResult.Stale,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 32)));
            Assert.Equal(WorldEntityResult.MovementUnbound,
                world.SubmitMovement(ctx, Intent(Identity(2), "alice", version, 1, 1, 1.5)));
        });
        Assert.False(world.TryGetMovement(identity, out _));
        Assert.True(world.TryGetMovement(Identity(2), out var replacement));
        Assert.Equal(0UL, replacement.SessionVersion);
    }

    [Fact]
    public void NonFiniteAndZeroSequenceAreRejectedAtInputConstruction()
    {
        var identity = Identity();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position(double.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position(0, double.PositiveInfinity, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Angle(double.NegativeInfinity));
        Assert.Throws<ArgumentException>(() =>
            Intent(identity, "Alice", 1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Intent(identity, "alice", 1, 0, 1, 1));
        var (world, executor, actual) = Start();
        Run(executor, ctx =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                world.BindMovementSubject(ctx, actual, BoundIdentity.CreateLocalTest("alice"), new Tick(0),
                    new Duration(0), out _));
        });
    }

    [Fact]
    public void MissingOrInvalidSpeedCannotBindAndZeroSpeedOnlyAllowsIdleInput()
    {
        var (world, executor, identity) = Start(defineSpeed: false);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.MovementInvalidSpeed,
                world.BindMovementSubject(ctx, identity, BoundIdentity.CreateLocalTest("alice"),
                    new Tick(0), new Duration(50), out var refused));
            Assert.Equal(0UL, refused);
            world.SetBaseAttribute(ctx, identity, Speed, 101m);
            Assert.Equal(WorldEntityResult.MovementInvalidSpeed,
                world.BindMovementSubject(ctx, identity, BoundIdentity.CreateLocalTest("alice"),
                    new Tick(0), new Duration(50), out refused));
            world.SetBaseAttribute(ctx, identity, Speed, 0m);
        });
        var version = Bind(world, executor, identity);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.MovementSpeedExceeded,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 31.76)));
            Assert.Equal(WorldEntityResult.MovementAccepted,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 1, 1, 31.75)));
            world.SetBaseAttribute(ctx, identity, Speed, 101m);
            Assert.Equal(WorldEntityResult.MovementInvalidSpeed,
                world.SubmitMovement(ctx, Intent(identity, "alice", version, 2, 2, 31.75)));
        });
        Assert.True(world.TryGetMovement(identity, out var after));
        Assert.Equal(1UL, after.LastSequence);
        Assert.Equal(new Tick(1), after.LastTick);
    }

    [Fact]
    public void RevivalDoesNotRestoreDeadMovementSession()
    {
        var (world, executor, identity) = Start();
        var session = Bind(world, executor, identity);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.LifeChanged, world.MarkDead(ctx, identity));
            Assert.Equal(WorldEntityResult.LifeChanged, world.Revive(ctx, identity));
            Assert.Equal(WorldEntityResult.MovementUnbound,
                world.SubmitMovement(ctx, Intent(identity, "alice", session, 1, 1, 32)));
        });
        var next = Bind(world, executor, identity, baseline: 10);
        Assert.Equal(session + 1, next);
        Run(executor, ctx => Assert.Equal(WorldEntityResult.MovementAccepted,
            world.SubmitMovement(ctx, Intent(identity, "alice", next, 1, 11, 32))));
        Assert.True(world.TryGetMovement(identity, out var state));
        Assert.Equal(1UL, state.LastSequence);
        Assert.Equal(next, state.SessionVersion);
    }

    [Fact]
    public void SameStateAndDifferentSubjectSequenceYieldSourceSensitiveReplayHash()
    {
        static string Hash(string subject, ulong sequence)
        {
            var (world, executor, identity) = Start();
            var version = Bind(world, executor, identity, subject);
            Run(executor, ctx =>
            {
                Assert.Equal(WorldEntityResult.MovementAccepted,
                    world.SubmitMovement(ctx, Intent(identity, subject, version, sequence, 1, 31.75)));
            });
            var clock = new FakeClock(new Duration(50),
                new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
            var pipeline = new WorldTickPipeline(world, clock, []);
            clock.AdvanceTicks(1);
            Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
            return pipeline.LastStateHash;
        }

        Assert.Equal(Hash("alice", 1), Hash("alice", 1));
        Assert.NotEqual(Hash("alice", 1), Hash("bob", 1));
        Assert.NotEqual(Hash("alice", 1), Hash("alice", 3));
    }
}
