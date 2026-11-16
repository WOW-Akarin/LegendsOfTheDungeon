using System.Reflection;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.Core;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class MessagingTests
{
    private static readonly MessageVersion V1 = new(1);

    [Fact]
    public async Task DemoModuleCompletesCommandResultQueryAndDeferredEventFlow()
    {
        var state = new DemoState("old");
        var publisher = new RecordingPublisher();
        var commands = new CommandDispatcher(publisher);
        var queries = new QueryDispatcher();

        commands.Register(new RenameDemoHandler(state));
        queries.Register(new GetDemoHandler(state));

        var commandResult = await commands.DispatchAsync(
            new RenameDemoCommand(new CommandId(10), V1, "new"));

        Assert.True(commandResult.IsSuccess);
        Assert.Equal(1, commandResult.Value.Revision);
        Assert.Single(publisher.Batches);

        var envelope = Assert.Single(publisher.Batches[0].Events);
        Assert.Equal(new CommandId(10), envelope.CommandId);
        Assert.Equal(1UL, envelope.Sequence.Value);
        Assert.Null(envelope.ParentSequence);
        Assert.Equal(0, envelope.Depth);

        var changed = Assert.IsType<DemoRenamedEvent>(envelope.DomainEvent);
        Assert.Equal(V1, changed.Version);
        Assert.Equal("new", changed.Name);
        Assert.Equal(1, changed.Revision);

        var queryResult = await queries.DispatchAsync(
            new GetDemoQuery(V1));

        Assert.True(queryResult.IsSuccess);
        Assert.Equal("new", queryResult.Value.Name);
        Assert.Equal(1, queryResult.Value.Revision);
        Assert.NotSame(state, queryResult.Value);
    }

    [Fact]
    public void QuerySnapshotDemoDoesNotExposeMutablePublicState()
    {
        var type = typeof(DemoSnapshot);

        Assert.Empty(
            type.GetFields(BindingFlags.Instance | BindingFlags.Public));

        Assert.All(
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public async Task BusinessFailureDiscardsQueuedEvents()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new CommandDispatcher(publisher);
        dispatcher.Register(new FailingCommandHandler());

        var result = await dispatcher.DispatchAsync(
            new FailingCommand(new CommandId(20), V1));

        Assert.True(result.IsFailure);
        Assert.Equal("demo.rejected", result.Error.Code);
        Assert.Empty(publisher.Batches);
    }

    [Fact]
    public async Task CancellationAndUnexpectedExceptionDoNotPublishEvents()
    {
        var cancellationPublisher = new RecordingPublisher();
        var cancellationDispatcher = new CommandDispatcher(cancellationPublisher);
        cancellationDispatcher.Register(new CancellingHandler());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cancellationDispatcher.DispatchAsync(
                new CancellingCommand(new CommandId(21), V1),
                new CancellationToken(canceled: true)));

        Assert.Empty(cancellationPublisher.Batches);

        var exceptionPublisher = new RecordingPublisher();
        var exceptionDispatcher = new CommandDispatcher(exceptionPublisher);
        exceptionDispatcher.Register(new ThrowingHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await exceptionDispatcher.DispatchAsync(
                new ThrowingCommand(new CommandId(22), V1)));

        Assert.Empty(exceptionPublisher.Batches);
    }

    [Fact]
    public async Task PublisherFailureAfterCommitPropagates()
    {
        var dispatcher = new CommandDispatcher(new ThrowingPublisher());
        dispatcher.Register(new SuccessfulNoopHandler());

        await Assert.ThrowsAsync<PublisherFailedException>(
            async () => await dispatcher.DispatchAsync(
                new SuccessfulNoopCommand(new CommandId(23), V1)));
    }

    [Fact]
    public void DeferredQueueAssignsSequenceCausalityAndDepth()
    {
        var queue = new DeferredEventBuffer(
            new CommandId(30),
            maximumEvents: 4,
            maximumDepth: 2);

        var root = queue.EnqueueRoot(new DemoSignalEvent(V1, "root"));
        var child = queue.EnqueueDerived(root, new DemoSignalEvent(V1, "child"));
        var grandchild = queue.EnqueueDerived(child, new DemoSignalEvent(V1, "grandchild"));
        var batch = queue.Commit();

        Assert.Equal([1UL, 2UL, 3UL], batch.Events.Select(x => x.Sequence.Value));
        Assert.Null(root.ParentSequence);
        Assert.Equal(root.Sequence, child.ParentSequence);
        Assert.Equal(child.Sequence, grandchild.ParentSequence);
        Assert.Equal([0, 1, 2], batch.Events.Select(x => x.Depth));
        Assert.True(queue.IsCompleted);
        Assert.Throws<InvalidOperationException>(
            () => queue.EnqueueRoot(new DemoSignalEvent(V1, "late")));
    }

    [Fact]
    public void DeferredQueueRejectsCountDepthAndForeignParent()
    {
        var countQueue = new DeferredEventBuffer(
            new CommandId(31),
            maximumEvents: 1,
            maximumDepth: 2);
        _ = countQueue.EnqueueRoot(new DemoSignalEvent(V1, "one"));
        Assert.Throws<InvalidOperationException>(
            () => countQueue.EnqueueRoot(new DemoSignalEvent(V1, "two")));

        var depthQueue = new DeferredEventBuffer(
            new CommandId(32),
            maximumEvents: 8,
            maximumDepth: 1);
        var root = depthQueue.EnqueueRoot(new DemoSignalEvent(V1, "root"));
        var child = depthQueue.EnqueueDerived(root, new DemoSignalEvent(V1, "child"));
        Assert.Throws<InvalidOperationException>(
            () => depthQueue.EnqueueDerived(child, new DemoSignalEvent(V1, "too-deep")));

        var otherQueue = new DeferredEventBuffer(
            new CommandId(33),
            maximumEvents: 8,
            maximumDepth: 8);
        var foreignParent = otherQueue.EnqueueRoot(new DemoSignalEvent(V1, "foreign"));

        Assert.Throws<ArgumentException>(
            () => depthQueue.EnqueueDerived(
                foreignParent,
                new DemoSignalEvent(V1, "invalid-parent")));
    }

    [Fact]
    public void DuplicateCommandAndQueryRegistrationFail()
    {
        var state = new DemoState("x");
        var commands = new CommandDispatcher(new RecordingPublisher());
        var commandHandler = new RenameDemoHandler(state);
        commands.Register(commandHandler);

        Assert.Throws<InvalidOperationException>(
            () => commands.Register(commandHandler));

        var queries = new QueryDispatcher();
        var queryHandler = new GetDemoHandler(state);
        queries.Register(queryHandler);

        Assert.Throws<InvalidOperationException>(
            () => queries.Register(queryHandler));
    }

    [Fact]
    public async Task UnknownCommandAndQueryReturnStableErrors()
    {
        var commands = new CommandDispatcher(new RecordingPublisher());
        var commandResult = await commands.DispatchAsync(
            new RenameDemoCommand(new CommandId(40), V1, "new"));

        Assert.True(commandResult.IsFailure);
        Assert.Equal("runtime.command.unknown", commandResult.Error.Code);

        var queries = new QueryDispatcher();
        var queryResult = await queries.DispatchAsync(
            new GetDemoQuery(V1));

        Assert.True(queryResult.IsFailure);
        Assert.Equal("runtime.query.unknown", queryResult.Error.Code);
    }

    [Fact]
    public async Task InvalidMessageMetadataReturnsStableErrors()
    {
        var publisher = new RecordingPublisher();
        var commands = new CommandDispatcher(publisher);
        commands.Register(new RenameDemoHandler(new DemoState("x")));

        var invalidId = await commands.DispatchAsync(
            new RenameDemoCommand(default, V1, "new"));
        Assert.Equal("runtime.command.invalid_id", invalidId.Error.Code);

        var invalidVersion = await commands.DispatchAsync(
            new RenameDemoCommand(new CommandId(41), default, "new"));
        Assert.Equal("runtime.message.invalid_version", invalidVersion.Error.Code);

        var queries = new QueryDispatcher();
        queries.Register(new GetDemoHandler(new DemoState("x")));
        var invalidQueryVersion = await queries.DispatchAsync(
            new GetDemoQuery(default));
        Assert.Equal("runtime.message.invalid_version", invalidQueryVersion.Error.Code);

        Assert.Empty(publisher.Batches);
    }

    [Fact]
    public void CommandIdAndMessageVersionRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageVersion(0));
        Assert.True(default(CommandId).IsEmpty);
        Assert.True(default(MessageVersion).IsEmpty);
        Assert.Equal("42", new CommandId(42).ToString());
    }

    private sealed class DemoState(string name)
    {
        public string Name { get; set; } = name;

        public int Revision { get; set; }
    }

    private sealed record RenameDemoCommand(
        CommandId CommandId,
        MessageVersion Version,
        string Name)
        : ICommand<DemoRenameResult>;

    private sealed class DemoRenameResult(int revision)
    {
        public int Revision { get; } = revision;
    }

    private sealed class RenameDemoHandler(DemoState state)
        : ICommandHandler<RenameDemoCommand, DemoRenameResult>
    {
        public ValueTask<Result<DemoRenameResult>> HandleAsync(
            RenameDemoCommand command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            state.Name = command.Name;
            state.Revision++;

            _ = deferredEvents.EnqueueRoot(
                new DemoRenamedEvent(V1, state.Name, state.Revision));

            return ValueTask.FromResult(
                Result.Success(new DemoRenameResult(state.Revision)));
        }
    }

    private sealed record DemoRenamedEvent(
        MessageVersion MessageVersion,
        string Name,
        int Revision)
        : DomainEvent(MessageVersion);

    private sealed record DemoSignalEvent(
        MessageVersion MessageVersion,
        string Value)
        : DomainEvent(MessageVersion);

    private sealed record GetDemoQuery(MessageVersion Version)
        : IQuery<DemoSnapshot>;

    private sealed class DemoSnapshot : IQuerySnapshot
    {
        public DemoSnapshot(string name, int revision)
        {
            Name = name;
            Revision = revision;
        }

        public string Name { get; }

        public int Revision { get; }
    }

    private sealed class GetDemoHandler(DemoState state)
        : IQueryHandler<GetDemoQuery, DemoSnapshot>
    {
        public ValueTask<Result<DemoSnapshot>> HandleAsync(
            GetDemoQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(
                Result.Success(new DemoSnapshot(state.Name, state.Revision)));
        }
    }

    private sealed record FailingCommand(
        CommandId CommandId,
        MessageVersion Version)
        : ICommand<DemoRenameResult>;

    private sealed class FailingCommandHandler
        : ICommandHandler<FailingCommand, DemoRenameResult>
    {
        public ValueTask<Result<DemoRenameResult>> HandleAsync(
            FailingCommand command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken)
        {
            _ = deferredEvents.EnqueueRoot(
                new DemoSignalEvent(V1, "must-discard"));

            return ValueTask.FromResult(
                Result.Failure<DemoRenameResult>(
                    new KernelError("demo.rejected", "示范 Command 被业务规则拒绝。")));
        }
    }

    private sealed record CancellingCommand(
        CommandId CommandId,
        MessageVersion Version)
        : ICommand<DemoRenameResult>;

    private sealed class CancellingHandler
        : ICommandHandler<CancellingCommand, DemoRenameResult>
    {
        public ValueTask<Result<DemoRenameResult>> HandleAsync(
            CancellingCommand command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken)
        {
            _ = deferredEvents.EnqueueRoot(
                new DemoSignalEvent(V1, "must-discard"));

            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("取消测试应先抛 OperationCanceledException。");
        }
    }

    private sealed record ThrowingCommand(
        CommandId CommandId,
        MessageVersion Version)
        : ICommand<DemoRenameResult>;

    private sealed class ThrowingHandler
        : ICommandHandler<ThrowingCommand, DemoRenameResult>
    {
        public ValueTask<Result<DemoRenameResult>> HandleAsync(
            ThrowingCommand command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken)
        {
            _ = deferredEvents.EnqueueRoot(
                new DemoSignalEvent(V1, "must-discard"));
            throw new InvalidOperationException("示范不可预期 Handler 异常。");
        }
    }

    private sealed record SuccessfulNoopCommand(
        CommandId CommandId,
        MessageVersion Version)
        : ICommand<DemoRenameResult>;

    private sealed class SuccessfulNoopHandler
        : ICommandHandler<SuccessfulNoopCommand, DemoRenameResult>
    {
        public ValueTask<Result<DemoRenameResult>> HandleAsync(
            SuccessfulNoopCommand command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken)
        {
            _ = deferredEvents.EnqueueRoot(
                new DemoSignalEvent(V1, "publisher-will-fail"));

            return ValueTask.FromResult(
                Result.Success(new DemoRenameResult(1)));
        }
    }

    private sealed class RecordingPublisher : IDeferredEventPublisher
    {
        private readonly List<DeferredEventBatch> _batches = [];

        public List<DeferredEventBatch> Batches => _batches;

        public ValueTask PublishAsync(
            DeferredEventBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _batches.Add(batch);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IDeferredEventPublisher
    {
        public ValueTask PublishAsync(
            DeferredEventBatch batch,
            CancellationToken cancellationToken)
        {
            throw new PublisherFailedException();
        }
    }

    private sealed class PublisherFailedException : Exception
    {
    }
}
