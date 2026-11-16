using System.Data.Common;

namespace LegendsOfTheDungeon.Platform.Persistence;

/// <summary>持久化事务状态，明确区分确认与未知。</summary>
public enum PersistenceTransactionState
{
    /// <summary>尚可执行命令。</summary>
    Active,
    /// <summary>已确认提交。</summary>
    Committed,
    /// <summary>已确认回滚。</summary>
    RolledBack,
    /// <summary>可能已经提交或回滚，必须由上层业务账本消歧。</summary>
    OutcomeUnknown,
    /// <summary>已经释放连接和事务。</summary>
    Disposed
}

/// <summary>数据库提交结果，未知不能被当成失败重试。</summary>
public enum CommitOutcome
{
    /// <summary>数据库确认提交。</summary>
    Committed,
    /// <summary>提交发出后失去可靠确认。</summary>
    Unknown
}

/// <summary>拥有数据库连接和事务的生命周期，避免离开作用域遗留活跃事务。</summary>
public sealed class PersistenceTransaction : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IPersistenceFailureInjector? _injector;

    internal PersistenceTransaction(DbConnection connection, DbTransaction transaction,
        IPersistenceFailureInjector? injector)
    {
        _connection = connection;
        _transaction = transaction;
        _injector = injector;
    }

    /// <summary>获得事务状态。</summary>
    public PersistenceTransactionState State { get; private set; } = PersistenceTransactionState.Active;

    /// <summary>在当前事务中执行参数化写入。</summary>
    /// <param name="command">模块提供的 SQL 命令。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>受影响行数。</returns>
    public async Task<int> ExecuteNonQueryAsync(
        ParameterizedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureActive();
        using var dbCommand = command.CreateDbCommand(_connection, _transaction);
        return await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>在当前事务中执行参数化标量读取。</summary>
    /// <param name="command">模块提供的 SQL 命令。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>数据库原始标量。</returns>
    public async Task<object?> ExecuteScalarAsync(
        ParameterizedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureActive();
        using var dbCommand = command.CreateDbCommand(_connection, _transaction);
        return await dbCommand.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>确认提交；请求发出后的取消、超时或数据库故障一律报告未知。</summary>
    /// <param name="cancellationToken">提交前取消直接抛出，提交中取消结果可能未知。</param>
    /// <returns>提交确认或 Unknown。</returns>
    public async Task<CommitOutcome> CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        _injector?.Trigger(PersistenceFailurePoint.BeforeCommit);
        State = PersistenceTransactionState.OutcomeUnknown;
        try
        {
            await _transaction.CommitAsync(cancellationToken);
            _injector?.Trigger(PersistenceFailurePoint.AfterCommit);
            State = PersistenceTransactionState.Committed;
            return CommitOutcome.Committed;
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or TimeoutException or DbException)
        {
            return CommitOutcome.Unknown;
        }
    }

    /// <summary>显式回滚活跃事务，失败时不假定已成功回滚。</summary>
    /// <param name="cancellationToken">取消信号。</param>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        State = PersistenceTransactionState.OutcomeUnknown;
        await _transaction.RollbackAsync(cancellationToken);
        _injector?.Trigger(PersistenceFailurePoint.AfterRollback);
        State = PersistenceTransactionState.RolledBack;
    }

    /// <summary>未完成的事务退出时尝试回滚；不重试未知提交，始终释放连接。</summary>
    public async ValueTask DisposeAsync()
    {
        if (State == PersistenceTransactionState.Disposed)
        {
            return;
        }

        var rollback = State == PersistenceTransactionState.Active;
        State = PersistenceTransactionState.Disposed;
        try
        {
            if (rollback)
            {
                await _transaction.RollbackAsync(CancellationToken.None);
            }
        }
        finally
        {
            try
            {
                await _transaction.DisposeAsync();
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }

    private void EnsureActive()
    {
        if (State != PersistenceTransactionState.Active)
        {
            throw new InvalidOperationException("数据库事务已结束或结果未知，不能继续操作。");
        }
    }
}
