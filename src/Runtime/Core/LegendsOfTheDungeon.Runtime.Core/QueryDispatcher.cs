using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.Core;

/// <summary>
/// 使用显式类型注册表分发只读 Query。
/// </summary>
public sealed class QueryDispatcher : IQueryDispatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _registrations = [];

    /// <summary>
    /// 显式注册一个具体 Query Handler；同一 Query CLR 类型只能注册一次。
    /// </summary>
    /// <typeparam name="TQuery">具体 Query 类型。</typeparam>
    /// <typeparam name="TResult">只读 Snapshot 类型。</typeparam>
    /// <param name="handler">待注册 Handler。</param>
    /// <exception cref="ArgumentNullException">handler 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">该 Query 类型已经注册时抛出。</exception>
    public void Register<TQuery, TResult>(
        IQueryHandler<TQuery, TResult> handler)
        where TQuery : IQuery<TResult>
        where TResult : IQuerySnapshot
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_registrations.TryAdd(
                typeof(TQuery),
                new Registration<TQuery, TResult>(handler)))
            {
                throw new InvalidOperationException(
                    $"Query Handler 已重复注册: {typeof(TQuery).FullName}。");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result<TResult>> DispatchAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
        where TResult : IQuerySnapshot
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Version.IsEmpty)
        {
            return Result.Failure<TResult>(
                new KernelError(
                    "runtime.message.invalid_version",
                    "Query 消息版本不能为 default/0。"));
        }

        object? rawRegistration;
        lock (_gate)
        {
            _registrations.TryGetValue(query.GetType(), out rawRegistration);
        }

        if (rawRegistration is null)
        {
            return Result.Failure<TResult>(
                new KernelError(
                    "runtime.query.unknown",
                    $"未注册 Query Handler: {query.GetType().FullName}。"));
        }

        if (rawRegistration is not IQueryRegistration<TResult> registration)
        {
            throw new InvalidOperationException(
                $"Query Handler 注册结果类型与消息契约不一致: {query.GetType().FullName}。");
        }

        return await registration
            .HandleAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    private interface IQueryRegistration<TResult>
        where TResult : IQuerySnapshot
    {
        ValueTask<Result<TResult>> HandleAsync(
            IQuery<TResult> query,
            CancellationToken cancellationToken);
    }

    private sealed class Registration<TQuery, TResult>(
        IQueryHandler<TQuery, TResult> handler)
        : IQueryRegistration<TResult>
        where TQuery : IQuery<TResult>
        where TResult : IQuerySnapshot
    {
        public ValueTask<Result<TResult>> HandleAsync(
            IQuery<TResult> query,
            CancellationToken cancellationToken)
        {
            return handler.HandleAsync(
                (TQuery)query,
                cancellationToken);
        }
    }
}
