using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.Core;

/// <summary>
/// 使用显式类型注册表分发 Command，并只在 Handler 成功返回后发布 DeferredEvent。
/// </summary>
/// <remarks>
/// 注册用于组合期；该类型不提供任意服务解析能力，因此不是 Service Locator。
/// </remarks>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _registrations = [];
    private readonly IDeferredEventPublisher _publisher;
    private readonly int _maximumDeferredEvents;
    private readonly int _maximumEventDepth;

    /// <summary>
    /// 创建 Command dispatcher。
    /// </summary>
    /// <param name="publisher">提交后事件发布端口。</param>
    /// <param name="maximumDeferredEvents">单 Command 最大延迟事件数量。</param>
    /// <param name="maximumEventDepth">单 Command 最大事件派生深度。</param>
    /// <exception cref="ArgumentNullException">publisher 为 null 时抛出。</exception>
    /// <exception cref="ArgumentOutOfRangeException">事件数量小于 1 或深度小于 0 时抛出。</exception>
    public CommandDispatcher(
        IDeferredEventPublisher publisher,
        int maximumDeferredEvents = 256,
        int maximumEventDepth = 8)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        if (maximumDeferredEvents < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDeferredEvents),
                maximumDeferredEvents,
                "maximumDeferredEvents 必须大于 0。");
        }

        if (maximumEventDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEventDepth),
                maximumEventDepth,
                "maximumEventDepth 不能为负数。");
        }

        _publisher = publisher;
        _maximumDeferredEvents = maximumDeferredEvents;
        _maximumEventDepth = maximumEventDepth;
    }

    /// <summary>
    /// 显式注册一个具体 Command Handler；同一 Command CLR 类型只能注册一次。
    /// </summary>
    /// <typeparam name="TCommand">具体 Command 类型。</typeparam>
    /// <typeparam name="TResult">Command 成功结果类型。</typeparam>
    /// <param name="handler">待注册 Handler。</param>
    /// <exception cref="ArgumentNullException">handler 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">该 Command 类型已经注册时抛出。</exception>
    public void Register<TCommand, TResult>(
        ICommandHandler<TCommand, TResult> handler)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_registrations.TryAdd(
                typeof(TCommand),
                new Registration<TCommand, TResult>(handler)))
            {
                throw new InvalidOperationException(
                    $"Command Handler 已重复注册: {typeof(TCommand).FullName}。");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result<TResult>> DispatchAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.CommandId.IsEmpty)
        {
            return Result.Failure<TResult>(
                new KernelError(
                    "runtime.command.invalid_id",
                    "CommandId 不能为 default/empty。"));
        }

        if (command.Version.IsEmpty)
        {
            return Result.Failure<TResult>(
                new KernelError(
                    "runtime.message.invalid_version",
                    "Command 消息版本不能为 default/0。"));
        }

        object? rawRegistration;
        lock (_gate)
        {
            _registrations.TryGetValue(command.GetType(), out rawRegistration);
        }

        if (rawRegistration is null)
        {
            return Result.Failure<TResult>(
                new KernelError(
                    "runtime.command.unknown",
                    $"未注册 Command Handler: {command.GetType().FullName}。"));
        }

        if (rawRegistration is not ICommandRegistration<TResult> registration)
        {
            throw new InvalidOperationException(
                $"Command Handler 注册结果类型与消息契约不一致: {command.GetType().FullName}。");
        }

        var deferredEvents = new DeferredEventBuffer(
            command.CommandId,
            _maximumDeferredEvents,
            _maximumEventDepth);

        Result<TResult> result;

        try
        {
            result = await registration
                .HandleAsync(command, deferredEvents, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                deferredEvents.Discard();
                return result;
            }
        }
        catch
        {
            if (!deferredEvents.IsCompleted)
            {
                deferredEvents.Discard();
            }

            throw;
        }

        var batch = deferredEvents.Commit();

        if (!batch.IsEmpty)
        {
            await _publisher
                .PublishAsync(batch, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private interface ICommandRegistration<TResult>
    {
        ValueTask<Result<TResult>> HandleAsync(
            ICommand<TResult> command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken);
    }

    private sealed class Registration<TCommand, TResult>(
        ICommandHandler<TCommand, TResult> handler)
        : ICommandRegistration<TResult>
        where TCommand : ICommand<TResult>
    {
        public ValueTask<Result<TResult>> HandleAsync(
            ICommand<TResult> command,
            DeferredEventBuffer deferredEvents,
            CancellationToken cancellationToken)
        {
            return handler.HandleAsync(
                (TCommand)command,
                deferredEvents,
                cancellationToken);
        }
    }
}
