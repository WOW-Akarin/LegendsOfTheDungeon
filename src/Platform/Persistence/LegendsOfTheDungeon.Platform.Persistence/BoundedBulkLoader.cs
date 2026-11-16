namespace LegendsOfTheDungeon.Platform.Persistence;

/// <summary>在已有事务内执行有界参数化命令批次；不是 PostgreSQL COPY 的性能替代品。</summary>
public static class BoundedBulkLoader
{
    /// <summary>执行不超过硬限制的命令列表；任一失败将回滚整个事务。</summary>
    /// <param name="transaction">由调用方拥有的活跃事务。</param>
    /// <param name="commands">预构造的参数化写入命令，不支持流式无限枚举。</param>
    /// <param name="maximumCommands">严格正数的命令上限，执行前完整校验。</param>
    /// <param name="maximumParameters">本批全部参数总数上限，执行前完整校验。</param>
    /// <param name="cancellationToken">取消信号；执行中取消将回滚全部本事务写入。</param>
    /// <returns>执行完成的命令数，不代表事务最终已经提交。</returns>
    public static async Task<int> ExecuteAsync(
        PersistenceTransaction transaction,
        IReadOnlyList<ParameterizedCommand> commands,
        int maximumCommands,
        int maximumParameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCommands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParameters);
        if (commands.Count > maximumCommands)
        {
            throw new ArgumentException("批量命令数超出允许上限。", nameof(commands));
        }

        long totalParameters = 0;
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            totalParameters += command.Parameters.Count;
            if (totalParameters > maximumParameters)
            {
                throw new ArgumentException("批量参数总数超出允许上限。", nameof(commands));
            }
        }

        try
        {
            foreach (var command in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.ExecuteNonQueryAsync(command, cancellationToken);
            }

            return commands.Count;
        }
        catch
        {
            if (transaction.State == PersistenceTransactionState.Active)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }
}
