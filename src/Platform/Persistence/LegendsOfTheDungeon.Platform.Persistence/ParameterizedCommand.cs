using System.Data;
using System.Data.Common;

namespace LegendsOfTheDungeon.Platform.Persistence;

/// <summary>由参数名、数据库类型和值组成的安全绑定描述；值不得拼接到 SQL 文本。</summary>
public sealed class SqlParameterValue
{
    /// <summary>构造需要由 Provider 真正绑定的 SQL 参数。</summary>
    /// <param name="name">以 @ 起始的安全小写 ASCII 参数名。</param>
    /// <param name="dbType">显式的 ADO.NET 数据类型。</param>
    /// <param name="value">参数值；null 将作为 DBNull 绑定。</param>
    public SqlParameterValue(string name, DbType dbType, object? value)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException("SQL 参数名格式无效。", nameof(name));
        }

        if (!Enum.IsDefined(dbType))
        {
            throw new ArgumentOutOfRangeException(nameof(dbType));
        }

        Name = name;
        DbType = dbType;
        Value = value;
    }

    /// <summary>安全参数名。</summary>
    public string Name { get; }

    /// <summary>ADO.NET 参数类型。</summary>
    public DbType DbType { get; }

    /// <summary>只用于参数绑定的值，不允许进入日志。</summary>
    public object? Value { get; }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length is < 2 or > 64 ||
            name[0] != '@' || name[1] is < 'a' or > 'z')
        {
            return false;
        }

        for (var i = 2; i < name.Length; i++)
        {
            if (name[i] is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>冻结模块提供的 SQL 文本和参数快照；技术层不定义业务表或 SQL。</summary>
public sealed class ParameterizedCommand
{
    private readonly IReadOnlyList<SqlParameterValue> _parameters;

    /// <summary>创建不允许重名参数的命令规格。</summary>
    /// <param name="sql">模块 Infrastructure 持有的静态 SQL 模板。</param>
    /// <param name="parameters">预先构造的类型化参数。</param>
    public ParameterizedCommand(string sql, IEnumerable<SqlParameterValue> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        var values = new List<SqlParameterValue>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in parameters)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!names.Add(item.Name))
            {
                throw new ArgumentException("SQL 参数名不能重复。", nameof(parameters));
            }

            values.Add(item);
        }

        Sql = sql;
        _parameters = values.AsReadOnly();
    }

    /// <summary>不含参数值的 SQL 模板。</summary>
    public string Sql { get; }

    /// <summary>参数快照，不允许增删。</summary>
    public IReadOnlyList<SqlParameterValue> Parameters => _parameters;

    /// <summary>创建带有真正 DbParameter 对象的命令，调用方负责释放。</summary>
    /// <param name="connection">已有连接。</param>
    /// <param name="transaction">可选的同连接事务。</param>
    /// <returns>未执行的提供程序命令。</returns>
    public DbCommand CreateDbCommand(DbConnection connection, DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (transaction is not null && !ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException("事务必须属于传入的连接。", nameof(transaction));
        }

        var command = connection.CreateCommand();
        try
        {
            command.CommandType = CommandType.Text;
            command.CommandText = Sql;
            command.Transaction = transaction;
            foreach (var item in _parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = item.Name;
                parameter.DbType = item.DbType;
                parameter.Value = item.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }
}
