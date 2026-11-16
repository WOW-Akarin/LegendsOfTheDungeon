using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class WorldExecutorTests
{
    private static WorldExecutor Create(
        ulong world = 1, ulong epoch = 1,
        int normal = 16, int critical = 8, int burst = 2, int tickets = 16) =>
        new(new WorldId(world), new WorldEpoch(epoch),
            new DenyByDefaultCommandAuthorizer(), normal, critical, burst, tickets);

    private static readonly EntityId Entity = new(100);
    private static readonly EntityGeneration Gen1 = new(1);
    private static readonly EntityGeneration Gen2 = new(2);

    private static WorldRunReport One(WorldExecutor executor) => executor.RunBatch(1);

    [Fact]
    public void SynchronousScopeProtectsEntityWritesAndExpiresAfterCallback()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        var model = new GuardedModel();
        IWorldWriteContext? captured = null;

        Assert.Equal(WorldAdmissionStatus.Accepted, port.Offer(new Work(ctx =>
        {
            captured = ctx;
            ctx.BindEntity(Entity, Gen1);
            model.Change(ctx, Entity, Gen1, 7);
        })));
        Assert.Equal(1, One(world).Executed);
        Assert.Equal(7, model.Value);
        Assert.NotNull(captured);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() =>
                model.Change(captured!, Entity, Gen1, 8)).Fault);
        Assert.Equal(7, model.Value);
    }

    [Fact]
    public void TaskRunInheritedAsyncLocalCannotWriteWhileScopeStillValid()
    {
        var world = Create();
        var model = new GuardedModel();
        var attempted = false;
        using var observed = new ManualResetEventSlim();
        Assert.Equal(WorldAdmissionStatus.Accepted,
            world.CreateTrustedProducer().Offer(new Work(ctx =>
            {
                ctx.BindEntity(Entity, Gen1);
                _ = Task.Run(() =>
                {
                    try
                    {
                        model.Change(ctx, Entity, Gen1, 123);
                    }
                    catch (WorldAccessException ex)
                        when (ex.Fault == WorldAccessFault.LeaseRequired)
                    {
                        attempted = true;
                    }
                    finally
                    {
                        observed.Set();
                    }
                });

                if (!observed.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new InvalidOperationException("worker never executed");
                }

                model.Change(ctx, Entity, Gen1, 5);
            })));
        Assert.Equal(WorldRunStatus.Completed, One(world).Status);
        Assert.True(attempted);
        Assert.Equal(5, model.Value);
    }

    [Fact]
    public void SameWorldReentryIsBusyAndOtherAcceptedWorkRemainsQueued()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        WorldRunReport? nested = null;
        port.Offer(new Work(_ => nested = world.RunBatch(10)));
        port.Offer(new Work(_ => { }));
        Assert.Equal(1, One(world).Executed);
        Assert.Equal(WorldRunStatus.Busy, nested!.Status);
        Assert.Equal(1, nested.Remaining);
        Assert.Equal(1, One(world).Executed);
    }

    [Fact]
    public async Task DifferentWorldExecutorsCanExecuteOnDifferentWorkers()
    {
        var a = Create(1);
        var b = Create(2);
        using var gate = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        a.CreateTrustedProducer().Offer(new Work(_ => { gate.Signal(); release.Wait(TimeSpan.FromSeconds(5)); }));
        b.CreateTrustedProducer().Offer(new Work(_ => { gate.Signal(); release.Wait(TimeSpan.FromSeconds(5)); }));
        var ra = Task.Run(() => a.RunBatch(1));
        var rb = Task.Run(() => b.RunBatch(1));
        Assert.True(gate.Wait(TimeSpan.FromSeconds(5)));
        release.Set();
        Assert.Equal(WorldRunStatus.Completed, (await ra).Status);
        Assert.Equal(WorldRunStatus.Completed, (await rb).Status);
    }

    [Fact]
    public void CriticalRepliesHaveReservedCapacityAndDoNotStarveNormalInput()
    {
        var world = Create(normal: 4, critical: 3, burst: 2);
        var port = world.CreateTrustedProducer();
        var order = new List<string>();
        var replyTickets = new List<IWorldReplyTicket>();
        port.Offer(new Work(ctx =>
        {
            ctx.BindEntity(new EntityId(1), Gen1);
            ctx.BindEntity(new EntityId(2), Gen1);
            ctx.BindEntity(new EntityId(3), Gen1);
            replyTickets.Add(ctx.IssueReplyTicket(new EntityId(1), Gen1));
            replyTickets.Add(ctx.IssueReplyTicket(new EntityId(2), Gen1));
            replyTickets.Add(ctx.IssueReplyTicket(new EntityId(3), Gen1));
        }));
        One(world);
        for (var i = 0; i < 4; i++)
        {
            var label = $"n{i}";
            Assert.Equal(WorldAdmissionStatus.Accepted, port.Offer(new Work(_ => order.Add(label))));
        }

        Assert.Equal(WorldAdmissionStatus.Busy, port.Offer(new Work(_ => { })));
        for (var i = 0; i < 3; i++)
        {
            var label = $"c{i}";
            Assert.Equal(WorldAdmissionStatus.Accepted,
                port.OfferReply(replyTickets[i], new Work(_ => order.Add(label))));
        }

        Assert.Equal(WorldAdmissionStatus.Busy,
            port.OfferReply(replyTickets[0], new Work(_ => { })) is WorldAdmissionStatus.Stale
                ? WorldAdmissionStatus.Busy : WorldAdmissionStatus.Stale);
        var report = world.RunBatch(7);
        Assert.Equal(7, report.Executed);
        Assert.Equal(["c0", "c1", "n0", "c2", "n1", "n2", "n3"], order);
    }

    [Fact]
    public void RetiredGenerationCannotAcceptOrExecuteLateReply()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        IWorldReplyTicket? ticket = null;
        port.Offer(new Work(ctx =>
        {
            ctx.BindEntity(Entity, Gen1);
            ticket = ctx.IssueReplyTicket(Entity, Gen1);
        }));
        One(world);
        port.Offer(new Work(ctx =>
        {
            ctx.RetireEntity(Entity, Gen1);
            ctx.BindEntity(Entity, Gen2);
        }));
        One(world);
        Assert.Equal(WorldAdmissionStatus.Stale,
            port.OfferReply(ticket!, new Work(_ => throw new Exception("must not run"))));
    }

    [Fact]
    public void RequestSequenceSupersedesOldReplyAndDuplicateIsRejected()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        IWorldReplyTicket? old = null;
        IWorldReplyTicket? newer = null;
        port.Offer(new Work(ctx =>
        {
            ctx.BindEntity(Entity, Gen1);
            old = ctx.IssueReplyTicket(Entity, Gen1);
            newer = ctx.IssueReplyTicket(Entity, Gen1);
        }));
        One(world);
        Assert.Equal(WorldAdmissionStatus.Stale,
            port.OfferReply(old!, new Work(_ => { })));
        Assert.Equal(WorldAdmissionStatus.Accepted,
            port.OfferReply(newer!, new Work(ctx => ctx.Demand(Entity, Gen1))));
        Assert.Equal(WorldAdmissionStatus.Stale,
            port.OfferReply(newer!, new Work(_ => { })));
        Assert.Equal(1, One(world).Executed);
        Assert.Equal(WorldAdmissionStatus.Stale,
            port.OfferReply(newer!, new Work(_ => { })));
    }

    [Fact]
    public void FakeTicketAndForeignWorldTicketCannotGrantAnyWritePermission()
    {
        var world = Create(1);
        var other = Create(2);
        var producer = world.CreateTrustedProducer();
        IWorldReplyTicket? foreign = null;
        other.CreateTrustedProducer().Offer(new Work(ctx =>
        {
            ctx.BindEntity(Entity, Gen1);
            foreign = ctx.IssueReplyTicket(Entity, Gen1);
        }));
        One(other);
        Assert.Equal(WorldAdmissionStatus.Stale, producer.OfferReply(foreign!, new Work(_ => { })));
        Assert.Equal(WorldAdmissionStatus.Stale,
            producer.OfferReply(new ForgedTicket(), new Work(_ => { })));
    }

    [Fact]
    public void EpochChangeDropsQueuedOldWorkAndInvalidatesTickets()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        IWorldReplyTicket? ticket = null;
        port.Offer(new Work(ctx =>
        {
            ctx.BindEntity(Entity, Gen1);
            ticket = ctx.IssueReplyTicket(Entity, Gen1);
        }));
        One(world);
        var executed = 0;
        port.Offer(new Work(_ => executed++));
        world.AdvanceEpoch(new WorldEpoch(2));
        Assert.Equal(WorldAdmissionStatus.Stale,
            port.OfferReply(ticket!, new Work(_ => executed++)));
        Assert.Equal(1, world.RunBatch(2).Stale);
        Assert.Equal(0, executed);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AdvanceEpoch(new WorldEpoch(2)));
        port.Offer(new Work(ctx => ctx.BindEntity(Entity, Gen1)));
        Assert.Equal(1, One(world).Executed);
    }

    [Fact]
    public void P60SecurityDeniesUnboundAndLocalTestIdentitiesBeforeAdmission()
    {
        var world = Create();
        var payload = new Work(_ => throw new InvalidOperationException("should not execute"));
        Assert.Equal(WorldAdmissionStatus.Denied, world.OfferExternal(null, payload));
        Assert.Equal(WorldAdmissionStatus.Denied,
            world.OfferExternal(BoundIdentity.CreateLocalTest("testuser"), payload));
        Assert.Equal(0, world.RunBatch(4).Executed);
    }

    [Fact]
    public void TrustedPortIsNotAmbientContextAndWorkerCannotMutateDirectly()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        IWorldWriteContext? leaked = null;
        port.Offer(new Work(ctx =>
        {
            ctx.BindEntity(Entity, Gen1);
            leaked = ctx;
        }));
        One(world);
        Assert.Equal(WorldAccessFault.LeaseRequired,
            Assert.Throws<WorldAccessException>(() => leaked!.RetireEntity(Entity, Gen1)).Fault);
        Assert.Equal(WorldAdmissionStatus.Accepted,
            port.Offer(new Work(ctx => ctx.RetireEntity(Entity, Gen1))));
        Assert.Equal(1, One(world).Executed);
    }

    [Fact]
    public void UnexpectedWorldCallbackFailureStopsWorldWithoutDequeuingNextItem()
    {
        var world = Create();
        var port = world.CreateTrustedProducer();
        var writes = 0;
        port.Offer(new Work(_ =>
        {
            writes++;
            throw new InvalidOperationException("secret-exception-body");
        }));
        port.Offer(new Work(_ => writes++));
        var result = world.RunBatch(10);
        Assert.Equal(WorldRunStatus.Faulted, result.Status);
        Assert.True(world.IsFaulted);
        Assert.Equal(1, writes);
        Assert.Equal(1, result.Remaining);
        Assert.Equal(WorldAdmissionStatus.Closed, port.Offer(new Work(_ => { })));
        Assert.Equal(WorldRunStatus.Faulted, world.RunBatch(10).Status);
    }

    [Fact]
    public void ReplyTicketCountIsHardBoundedAndDoesNotLeakOnRetire()
    {
        var world = Create(tickets: 1);
        var port = world.CreateTrustedProducer();
        WorldAccessFault? expected = null;
        port.Offer(new Work(ctx =>
        {
            ctx.BindEntity(new EntityId(1), Gen1);
            ctx.BindEntity(new EntityId(2), Gen1);
            _ = ctx.IssueReplyTicket(new EntityId(1), Gen1);
            try
            {
                _ = ctx.IssueReplyTicket(new EntityId(2), Gen1);
            }
            catch (WorldAccessException ex)
            {
                expected = ex.Fault;
            }

            ctx.RetireEntity(new EntityId(1), Gen1);
            _ = ctx.IssueReplyTicket(new EntityId(2), Gen1);
        }));
        Assert.Equal(WorldRunStatus.Completed, One(world).Status);
        Assert.Equal(WorldAccessFault.LimitExceeded, expected);
    }

    [Fact]
    public void CompleteRejectsNewInputButDrainsAlreadyAcceptedMessages()
    {
        var world = Create();
        var producer = world.CreateTrustedProducer();
        var counter = 0;
        Assert.Equal(WorldAdmissionStatus.Accepted,
            producer.Offer(new Work(_ => counter++)));
        world.Complete();
        Assert.Equal(WorldAdmissionStatus.Closed,
            producer.Offer(new Work(_ => counter++)));
        Assert.Equal(WorldRunStatus.Closed, world.RunBatch(2).Status);
        Assert.Equal(1, counter);
    }

    private sealed class Work(Action<IWorldWriteContext> act) : IWorldAction
    {
        public void Execute(IWorldWriteContext context) => act(context);
    }

    private sealed class GuardedModel
    {
        public int Value { get; private set; }

        public void Change(IWorldWriteContext context, EntityId entity, EntityGeneration generation, int value)
        {
            context.Demand(entity, generation);
            Value = value;
        }
    }

    private sealed class ForgedTicket : IWorldReplyTicket
    {
        public WorldId World => new(1);
        public WorldEpoch Epoch => new(1);
        public EntityId Entity => new(100);
        public EntityGeneration Generation => new(1);
        public long RequestSequence => 1;
    }
}
