using System.Data;
using System.Data.Common;

namespace LegendsOfTheDungeon.Platform.Persistence;

/// <summary>使用可注入 ADO.NET Provider 打开数据库连接的技术工厂。</summary>
public sealed class DatabaseConnectionFactory
{
    private readonly DbProviderFactory _provider;
    private readonly string _connectionString;

    /// <summary>保存连接配置；连接串是秘密，禁止打印或作为诊断内容。</summary>
    /// <param name="provider">数据库驱动提供程序工厂。</param>
    /// <param name="connectionString">秘密数据库连接串。</param>
    public DatabaseConnectionFactory(DbProviderFactory provider, string connectionString)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>打开新连接；失败时主动释放；成功时由调用方负责释放。</summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已打开的 ADO.NET 连接。</returns>
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = _provider.CreateConnection()
            ?? throw new InvalidOperationException("数据库提供程序未能创建连接。");
        try
        {
            connection.ConnectionString = _connectionString;
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>以明确指定的隔离级别开启拥有连接的事务作用域。</summary>
    /// <param name="isolationLevel">不能使用 Unspecified。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>拥有连接和事务的可异步释放作用域。</returns>
    public async Task<PersistenceTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(isolationLevel) || isolationLevel == IsolationLevel.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(isolationLevel));
        }

        var connection = await OpenAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
            return new PersistenceTransaction(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
