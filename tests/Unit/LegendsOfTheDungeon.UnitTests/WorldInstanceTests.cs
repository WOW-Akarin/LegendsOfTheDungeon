using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldInstanceTests
{
    private static readonly EntityGeneration First = new(1);
    private static readonly EntityGeneration Second = new(2);

    private static WorldExecutor Executor(ulong world = 1, ulong epoch = 1) =>
        new(new WorldId(world), new WorldEpoch(epoch),
            new DenyByDefaultCommandAuthorizer(), 32, 16, 2);

    private static EntityIdentity Identity(ulong id, ulong world = 1,
        ulong epoch = 1, ulong generation = 1) =>
        new(new WorldId(world), new WorldEpoch(epoch),
            new EntityId(id), new EntityGeneration(generation));

    private static WorldEntitySeed Seed(EntityIdentity id) =>
        new(id, new Transform(new Position(1, 2, 3), new Angle(0)));

    private static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
    {
        Assert.Equal(WorldAdmissionStatus.Accepted,
            executor.CreateTrustedProducer().Offer(new Work(action)));
        Assert.Equal(WorldRunStatus.Completed, executor.RunBatch(1).Status);
    }

    private static void Start(WorldExecutor executor, WorldInstance world) =>
        Run(executor, ctx =>
        {
            Assert.True(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
        });

    [Fact]
    public void WorldStateTransitionsRequireRealLeaseAndForbidInvalidOrder()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        IWorldWriteContext? captured = null;
        Run(executor, ctx =>
        {
            captured = ctx;
            Assert.False(world.Start(ctx));
            Assert.False(world.Stop(ctx));
            Assert.True(world.BeginLoading(ctx));
            Assert.False(world.BeginLoading(ctx));
            Assert.True(world.Start(ctx));
            Assert.False(world.Start(ctx));
            Assert.True(world.BeginDrain(ctx));
            Assert.False(world.BeginDrain(ctx));
            Assert.True(world.Stop(ctx));
            Assert.False(world.Stop(ctx));
        });
        Assert.Equal(WorldLifecycle.Stopped, world.State);
        Assert.Equal(0, ownership.RegisteredWorlds);
        Assert.NotNull(captured);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() => world.BeginLoading(captured!)).Fault);
    }

    [Fact]
    public void DuplicateWorldIdsAreRejectedUntilGracefulStop()
    {
        var ownership = new WorldOwnershipRegistry();
        var first = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        Assert.Throws<InvalidOperationException>(() =>
            new WorldInstance(new WorldId(1), new WorldEpoch(2), ownership));
        Assert.Equal(1, ownership.RegisteredWorlds);
        var executor = Executor();
        Start(executor, first);
        Run(executor, ctx =>
        {
            Assert.True(first.BeginDrain(ctx));
            Assert.True(first.Stop(ctx));
        });
        var second = new WorldInstance(new WorldId(1), new WorldEpoch(2), ownership);
        Assert.Equal(WorldLifecycle.Created, second.State);
        Assert.Equal(1, ownership.RegisteredWorlds);
    }

    [Fact]
    public void AttachActivateRemoveAndDisposeHaveExplicitRepeatResults()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var id = Identity(10);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Added, world.Add(ctx, Seed(id)));
            Assert.True(world.TryGetSnapshot(id.Id, out var attached));
            Assert.Equal(WorldEntityLifecycle.Attached, attached.Lifecycle);
            Assert.Equal(WorldEntityResult.AlreadyExists, world.Add(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.InvalidState, world.Relocate(ctx, id, default));
            Assert.Equal(WorldEntityResult.Activated, world.Activate(ctx, id));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.Activate(ctx, id));
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, id));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.Remove(ctx, id));
            Assert.Equal(WorldEntityResult.Despawned, world.Despawn(ctx, id));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.Despawn(ctx, id));
        });
        Assert.Equal(0, ownership.ClaimedEntities);
        Assert.Equal(0, world.LiveCount);
        Assert.True(world.TryGetSnapshot(id.Id, out var disposed));
        Assert.Equal(WorldEntityLifecycle.Disposed, disposed.Lifecycle);
        Assert.Null(disposed.Target);
    }

    [Fact]
    public void DifferentWorldsCannotClaimSameEntityEvenWithOtherGeneration()
    {
        var ownership = new WorldOwnershipRegistry();
        var a = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var b = new WorldInstance(new WorldId(2), new WorldEpoch(1), ownership);
        var ea = Executor();
        var eb = Executor(2);
        Start(ea, a);
        Start(eb, b);
        var id = Identity(7);
        var foreign = Identity(7, world: 2, generation: 2);
        Run(ea, ctx => Assert.Equal(WorldEntityResult.Spawned, a.Spawn(ctx, Seed(id))));
        Run(eb, ctx => Assert.Equal(WorldEntityResult.WorldConflict, b.Spawn(ctx, Seed(foreign))));
        Assert.True(ownership.TryGetOwner(id.Id, out var owner));
        Assert.Equal(id, owner);
        Assert.Equal(1, a.LiveCount);
        Assert.Equal(0, b.LiveCount);
        Run(ea, ctx => Assert.Equal(WorldEntityResult.Removed, a.Remove(ctx, id)));
        Run(eb, ctx => Assert.Equal(WorldEntityResult.Spawned, b.Spawn(ctx, Seed(foreign))));
        Assert.True(ownership.TryGetOwner(id.Id, out owner));
        Assert.Equal(foreign, owner);
    }

    [Fact]
    public void OldEpochAndGenerationCannotReenterAfterRemoval()
    {
        var registry = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), registry);
        var executor = Executor();
        Start(executor, world);
        var old = Identity(15);
        var current = Identity(15, generation: 2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(old)));
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, old));
            Assert.Equal(WorldEntityResult.Stale, world.Spawn(ctx, Seed(old)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(current)));
            Assert.Equal(WorldEntityResult.Stale, world.Remove(ctx, old));
            Assert.Equal(WorldEntityResult.Stale, world.Relocate(ctx, old, default));
            Assert.Equal(WorldEntityResult.WorldConflict,
                world.Add(ctx, Seed(Identity(16, epoch: 2))));
        });
        Assert.True(world.TryGetSnapshot(current.Id, out var view));
        Assert.Equal(current, view.Identity);
        Assert.Equal(WorldEntityLifecycle.Active, view.Lifecycle);
    }

    [Fact]
    public void TargetRequiresCurrentlyActiveAliveRegisteredGenerationAndCleansInbound()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var first = Identity(1);
        var second = Identity(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(first)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(second)));
            Assert.Equal(WorldEntityResult.TargetUnavailable,
                world.SelectTarget(ctx, first, Identity(2, generation: 2)));
            Assert.Equal(WorldEntityResult.TargetUnavailable,
                world.SelectTarget(ctx, first, Identity(2, world: 2)));
            Assert.Equal(WorldEntityResult.TargetUnavailable, world.SelectTarget(ctx, first, first));
            Assert.Equal(WorldEntityResult.TargetChanged, world.SelectTarget(ctx, first, second));
        });
        Assert.True(world.TryGetSnapshot(first.Id, out var before));
        Assert.Equal(second, before.Target);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.LifeChanged, world.MarkDead(ctx, second));
            Assert.Equal(WorldEntityResult.AlreadyCompleted, world.MarkDead(ctx, second));
            Assert.Equal(WorldEntityResult.TargetUnavailable, world.SelectTarget(ctx, first, second));
        });
        Assert.True(world.TryGetSnapshot(first.Id, out var after));
        Assert.Null(after.Target);
        Assert.Equal(second, before.Target);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.LifeChanged, world.Revive(ctx, second));
            Assert.Equal(WorldEntityResult.TargetChanged, world.SelectTarget(ctx, first, second));
            Assert.Equal(WorldEntityResult.Despawned, world.Despawn(ctx, second));
        });
        Assert.True(world.TryGetSnapshot(first.Id, out var removed));
        Assert.Null(removed.Target);
    }

    [Fact]
    public void RelocationUsesOriginalOwnershipAndSnapshotsCannotMutateWorld()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var id = Identity(5);
        var destination = new Transform(new Position(44, 55, 66), new Angle(2));
        IWorldWriteContext? captured = null;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            captured = ctx;
            Assert.Equal(WorldEntityResult.Relocated, world.Relocate(ctx, id, destination));
        });
        Assert.True(world.TryGetSnapshot(id.Id, out var view));
        Assert.Equal(destination, view.Transform);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() => world.Relocate(captured!, id, default)).Fault);
        Assert.True(world.TryGetSnapshot(id.Id, out var after));
        Assert.Equal(destination, after.Transform);
        Assert.False(world.TryGetSnapshot(new EntityId(987), out _));
    }

    [Fact]
    public void CleanupIsBoundedIdempotentOrderedAndStopRetiresRemainingEntities()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var first = Identity(2);
        var second = Identity(1);
        var calls = new List<string>();
        var callback = new Cleanup(id => calls.Add("first-" + id.Id));
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(first)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(second)));
            Assert.Equal(WorldEntityResult.CleanupRegistered, world.RegisterCleanup(ctx, first, callback));
            Assert.Equal(WorldEntityResult.AlreadyExists, world.RegisterCleanup(ctx, first, callback));
            Assert.Equal(WorldEntityResult.CleanupRegistered,
                world.RegisterCleanup(ctx, second, new Cleanup(id => calls.Add("second-" + id.Id))));
            Assert.True(world.BeginDrain(ctx));
            Assert.False(world.BeginLoading(ctx));
            Assert.Equal(WorldEntityResult.InvalidState,
                world.Spawn(ctx, Seed(Identity(3))));
            Assert.True(world.Stop(ctx));
        });
        Assert.Equal(["second-1", "first-2"], calls);
        Assert.Equal(0, world.LiveCount);
        Assert.Equal(0, ownership.ClaimedEntities);
        Assert.Equal(0, ownership.RegisteredWorlds);
    }

    [Fact]
    public void CleanupCallbackFailureFailsClosedWithoutReleasingClaim()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var id = Identity(91);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.Equal(WorldEntityResult.CleanupRegistered,
                world.RegisterCleanup(ctx, id,
                    new Cleanup(_ => throw new InvalidOperationException("fault"))));
        });
        Assert.Equal(WorldAdmissionStatus.Accepted, executor.CreateTrustedProducer()
            .Offer(new Work(ctx => world.Despawn(ctx, id))));
        Assert.Equal(WorldRunStatus.Faulted, executor.RunBatch(1).Status);
        Assert.True(executor.IsFaulted);
        Assert.Equal(1, ownership.ClaimedEntities);
        Assert.True(world.TryGetSnapshot(id.Id, out var snapshot));
        Assert.Equal(WorldEntityLifecycle.Removing, snapshot.Lifecycle);
    }

    [Fact]
    public void RetiredEntityInvalidatesWorkerTicketAndTransferRefuses()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var id = Identity(17);
        IWorldReplyTicket? ticket = null;
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            ticket = ctx.IssueReplyTicket(id.Id, First);
            Assert.Equal(WorldEntityResult.TransferNotSupported,
                world.Transfer(ctx, id, new WorldId(2)));
            Assert.Equal(WorldEntityResult.Removed, world.Remove(ctx, id));
        });
        Assert.NotNull(ticket);
        Assert.Equal(WorldAdmissionStatus.Stale,
            executor.CreateTrustedProducer().OfferReply(ticket!, new Work(_ => { })));
    }

    [Fact]
    public void WrongWorldOrOldEpochWorldScopeIsRejectedBeforeLifecycleMutation()
    {
        var shared = new WorldOwnershipRegistry();
        var a = new WorldInstance(new WorldId(1), new WorldEpoch(1), shared);
        var b = new WorldInstance(new WorldId(2), new WorldEpoch(1), shared);
        var executor = Executor();
        WorldAccessFault? fault = null;
        Run(executor, ctx =>
        {
            Assert.True(a.BeginLoading(ctx));
            try
            {
                b.BeginLoading(ctx);
            }
            catch (WorldAccessException ex)
            {
                fault = ex.Fault;
            }
        });
        Assert.Equal(WorldAccessFault.InvalidGeneration, fault);
        Assert.Equal(WorldLifecycle.Created, b.State);
        var future = new WorldInstance(new WorldId(3), new WorldEpoch(2), shared);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldAccessFault.InvalidGeneration,
                Assert.Throws<WorldAccessException>(() => future.BeginLoading(ctx)).Fault);
        });
    }

    [Fact]
    public void ManualWorldFaultRejectsFurtherEntityWorkAndPreservesClaims()
    {
        var ownership = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), ownership);
        var executor = Executor();
        Start(executor, world);
        var id = Identity(44);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(id)));
            Assert.True(world.Fault(ctx));
            Assert.False(world.Fault(ctx));
            Assert.Equal(WorldEntityResult.InvalidState, world.Despawn(ctx, id));
            Assert.False(world.BeginDrain(ctx));
            Assert.False(world.Stop(ctx));
        });
        Assert.Equal(WorldLifecycle.Faulted, world.State);
        Assert.Equal(1, ownership.ClaimedEntities);
    }

    [Fact]
    public void DisposedSourceMustForgetItsOwnTargetBeforeOtherEntitiesAreStopped()
    {
        var owners = new WorldOwnershipRegistry();
        var world = new WorldInstance(new WorldId(1), new WorldEpoch(1), owners);
        var executor = Executor();
        Start(executor, world);
        var source = Identity(1);
        var target = Identity(2);
        Run(executor, ctx =>
        {
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(source)));
            Assert.Equal(WorldEntityResult.Spawned, world.Spawn(ctx, Seed(target)));
            Assert.Equal(WorldEntityResult.TargetChanged, world.SelectTarget(ctx, source, target));
            Assert.True(world.BeginDrain(ctx));
            Assert.True(world.Stop(ctx));
        });
        Assert.True(world.TryGetSnapshot(source.Id, out var disposed));
        Assert.Equal(WorldEntityLifecycle.Disposed, disposed.Lifecycle);
        Assert.Null(disposed.Target);
    }

    private sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => action(context);
    }

    private sealed class Cleanup(Action<EntityIdentity> action) : IWorldEntityCleanup
    {
        public void OnRemoving(EntityIdentity identity) => action(identity);
    }
}
