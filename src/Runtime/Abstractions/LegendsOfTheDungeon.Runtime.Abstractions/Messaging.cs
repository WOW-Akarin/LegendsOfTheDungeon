using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Runtime.Abstractions;

/// <summary>
/// 处理具体 Command，并在成功提交前将事实事件写入当前 DeferredEventBuffer。
/// </summary>
/// <typeparam name="TCommand">具体 Command 类型。</typeparam>
/// <typeparam name="TResult">Command 成功结果类型。</typeparam>
public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// 执行 Command；成功返回前必须完成该 Handler 所拥有领域状态的提交。
    /// </summary>
    /// <param name="command">待执行 Command。</param>
    /// <param name="deferredEvents">当前 Command 独占的延迟事件队列。</param>
    /// <param name="cancellationToken">标准取消令牌；取消应传播 OperationCanceledException。</param>
    /// <returns>业务成功值或稳定 KernelError。</returns>
    ValueTask<Result<TResult>> HandleAsync(
        TCommand command,
        DeferredEventBuffer deferredEvents,
        CancellationToken cancellationToken);
}

/// <summary>
/// 处理只读 Query，并返回与可变领域状态断开的 Snapshot。
/// </summary>
/// <typeparam name="TQuery">具体 Query 类型。</typeparam>
/// <typeparam name="TResult">只读 Snapshot 类型。</typeparam>
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : IQuerySnapshot
{
    /// <summary>
    /// 执行只读 Query。
    /// </summary>
    /// <param name="query">待执行 Query。</param>
    /// <param name="cancellationToken">标准取消令牌。</param>
    /// <returns>只读 Snapshot 或稳定 KernelError。</returns>
    ValueTask<Result<TResult>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// 提供 Command 分发端口；调用方不解析或获取 Handler 实例。
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// 分发具体 Command。
    /// </summary>
    /// <typeparam name="TResult">Command 成功结果类型。</typeparam>
    /// <param name="command">实现 ICommand 的具体消息实例。</param>
    /// <param name="cancellationToken">标准取消令牌。</param>
    /// <returns>Handler 返回的业务结果。</returns>
    ValueTask<Result<TResult>> DispatchAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供只读 Query 分发端口。
/// </summary>
public interface IQueryDispatcher
{
    /// <summary>
    /// 分发具体 Query。
    /// </summary>
    /// <typeparam name="TResult">Query Snapshot 类型。</typeparam>
    /// <param name="query">实现 IQuery 的具体消息实例。</param>
    /// <param name="cancellationToken">标准取消令牌。</param>
    /// <returns>只读 Snapshot 或稳定 KernelError。</returns>
    ValueTask<Result<TResult>> DispatchAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : IQuerySnapshot;
}

/// <summary>
/// 提交后发布 DeferredEventBatch 的运行时端口。
/// </summary>
public interface IDeferredEventPublisher
{
    /// <summary>
    /// 发布已经完成领域提交的事件批。
    /// </summary>
    /// <param name="batch">不可变延迟事件快照。</param>
    /// <param name="cancellationToken">标准取消令牌。</param>
    /// <returns>异步发布完成信号。</returns>
    ValueTask PublishAsync(
        DeferredEventBatch batch,
        CancellationToken cancellationToken);
}
