using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class EntityCapabilitiesTests
{
    private static readonly WorldId World = new(1);
    private static readonly WorldEpoch Epoch = new(1);
    private static readonly EntityGeneration First = new(1);
    private static readonly EntityGeneration Second = new(2);

    private static EntityIdentity Identity(ulong id, ulong world = 1, ulong epoch = 1, ulong generation = 1) =>
        new(new WorldId(world), new WorldEpoch(epoch), new EntityId(id), new EntityGeneration(generation));

    private static EntityCapabilities Entity(EntityIdentity identity) =>
        new(identity, new Transform(new Position(1, 2, 3), new Angle(0)));

    private static WorldExecutor Executor() =>
        new(World, Epoch, new DenyByDefaultCommandAuthorizer(), 16, 8, 2);

    private sealed class ActionWork(Action<IWorldWriteContext> body) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => body(context);
    }

    [Fact]
    public void IdentityRequiresFullOwnerVersionAndContainerIsTyped()
    {
        Assert.Throws<ArgumentException>(() => new EntityIdentity(default, Epoch, new EntityId(1), First));
        Assert.Throws<ArgumentException>(() => new EntityIdentity(World, default, new EntityId(1), First));
        Assert.Throws<ArgumentException>(() => new EntityIdentity(World, Epoch, default, First));
        Assert.Throws<ArgumentException>(() => new EntityIdentity(World, Epoch, new EntityId(1), default));
        Assert.Throws<ArgumentException>(() => Entity(default));
        var owner = Identity(1);
        var entity = Entity(owner);
        Assert.Equal(owner, entity.Identity.Value);
        Assert.Equal(LifeState.Alive, entity.Life.State);
        Assert.Null(entity.Target.State.Selected);
        Assert.Equal(new Position(1, 2, 3), entity.Transform.Value.Position);
    }

    [Fact]
    public void RelocationRequiresRealLeaseAndOriginalWorldEpochGeneration()
    {
        var entity = Entity(Identity(10));
        var anotherWorldEntity = Entity(Identity(10, world: 2));
        var next = new Transform(new Position(5, 6, 7), new Angle(1));
        var executor = Executor();
        IWorldWriteContext? captured = null;
        WorldAccessFault? foreignFault = null;
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new ActionWork(context =>
            {
                context.BindEntity(new EntityId(10), First);
                captured = context;
                entity.Transform.Relocate(context, next);
                try
                {
                    anotherWorldEntity.Transform.Relocate(context, next);
                }
                catch (WorldAccessException ex)
                {
                    foreignFault = ex.Fault;
                }
            })));
        Assert.Equal(1, executor.RunBatch(1).Executed);
        Assert.Equal(WorldAccessFault.InvalidGeneration, foreignFault);
        Assert.Equal(next, entity.Transform.Value);
        Assert.NotNull(captured);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                entity.Transform.Relocate(captured!, new Transform(new Position(9, 9, 9), new Angle(0)))).Fault);
        Assert.Equal(new Position(1, 2, 3), anotherWorldEntity.Transform.Value.Position);
        Assert.Equal(next, entity.Transform.Value);
    }

    [Fact]
    public void DeathClearsTargetAndDisallowsSelectionUntilRevived()
    {
        var owner = Entity(Identity(1));
        var target = Identity(2);
        var executor = Executor();
        executor.CreateTrustedProducer().Offer(new ActionWork(context =>
        {
            context.BindEntity(owner.Identity.Value.Id, First);
            context.BindEntity(target.Id, First);
            owner.Target.Select(context, target);
            Assert.Equal(target, owner.Target.State.Selected);
            Assert.True(owner.MarkDead(context));
            Assert.False(owner.MarkDead(context));
            Assert.Equal(LifeState.Dead, owner.Life.State);
            Assert.Null(owner.Target.State.Selected);
            Assert.Throws<InvalidOperationException>(() => owner.Target.Select(context, target));
            Assert.True(owner.Revive(context));
            Assert.False(owner.Revive(context));
            owner.Target.Select(context, target);
        }));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
        Assert.Equal(target, owner.Target.State.Selected);
        Assert.Equal(LifeState.Alive, owner.Life.State);
    }

    [Fact]
    public void TargetRejectsSelfOtherWorldAndOldEpochWithoutMutating()
    {
        var entity = Entity(Identity(1));
        var executor = Executor();
        executor.CreateTrustedProducer().Offer(new ActionWork(context =>
        {
            context.BindEntity(new EntityId(1), First);
            Assert.Throws<ArgumentException>(() => entity.Target.Select(context, Identity(1, generation: 2)));
            Assert.Throws<ArgumentException>(() => entity.Target.Select(context, Identity(2, world: 3)));
            Assert.Throws<ArgumentException>(() => entity.Target.Select(context, Identity(2, epoch: 2)));
            Assert.Null(entity.Target.State.Selected);
        }));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    [Fact]
    public void RetiredGenerationCannotModifyReboundEntityAndSnapshotsAreValues()
    {
        var old = Entity(Identity(4));
        var renewed = Entity(Identity(4, generation: 2));
        var original = old.Transform.Value;
        var next = new Transform(new Position(11, 12, 13), new Angle(2));
        WorldAccessFault? staleFault = null;
        var executor = Executor();
        executor.CreateTrustedProducer().Offer(new ActionWork(context =>
        {
            context.BindEntity(new EntityId(4), First);
            old.Transform.Relocate(context, next);
            context.RetireEntity(new EntityId(4), First);
            context.BindEntity(new EntityId(4), Second);
            try
            {
                old.Transform.Relocate(context, original);
            }
            catch (WorldAccessException ex)
            {
                staleFault = ex.Fault;
            }

            renewed.Transform.Relocate(context, original);
        }));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
        Assert.Equal(WorldAccessFault.InvalidGeneration, staleFault);
        Assert.Equal(new Position(1, 2, 3), original.Position);
        Assert.Equal(next, old.Transform.Value);
        Assert.Equal(original, renewed.Transform.Value);
    }

    [Fact]
    public void UnboundEntityCannotChangeLifeOrTarget()
    {
        var entity = Entity(Identity(77));
        var executor = Executor();
        executor.CreateTrustedProducer().Offer(new ActionWork(context =>
        {
            Assert.Equal(WorldAccessFault.InvalidGeneration,
                Assert.Throws<WorldAccessException>(() => entity.MarkDead(context)).Fault);
            Assert.Equal(WorldAccessFault.InvalidGeneration,
                Assert.Throws<WorldAccessException>(() => entity.Target.Select(context, Identity(78))).Fault);
        }));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
        Assert.Equal(LifeState.Alive, entity.Life.State);
        Assert.Null(entity.Target.State.Selected);
    }
}
