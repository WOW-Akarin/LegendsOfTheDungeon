using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LegendsOfTheDungeon.Platform.Persistence;

/// <summary>模块 Infrastructure 提供的不可变有序数据库迁移步骤。</summary>
public sealed class MigrationStep
{
    /// <summary>构造一个顺序版本的迁移步骤。</summary>
    /// <param name="moduleId">稳定、安全的模块标识。</param>
    /// <param name="version">从 1 开始连续递增的版本。</param>
    /// <param name="sql">模块 Infrastructure 拥有的固定 SQL，不含未经绑定的外部数据。</param>
    public MigrationStep(string moduleId, int version, string sql)
    {
        if (!IsValidModuleId(moduleId))
        {
            throw new ArgumentException("迁移模块标识格式无效。", nameof(moduleId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        ModuleId = moduleId;
        Version = version;
        Sql = sql;
        Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)))
            .ToLowerInvariant();
    }

    /// <summary>提供迁移 SQL 的业务模块。</summary>
    public string ModuleId { get; }

    /// <summary>每个模块从 1 开始连续递增的版本。</summary>
    public int Version { get; }

    /// <summary>静态迁移 SQL；技术层不解释业务表。</summary>
    public string Sql { get; }

    /// <summary>SQL 文本的 SHA-256 校验和。</summary>
    public string Checksum { get; }

    private static bool IsValidModuleId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.' or '_' or '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>PostgreSQL 技术迁移器：事务锁和校验和账本与业务 SQL 分离。</summary>
public sealed class PostgreSqlMigrationRunner
{
    private readonly DatabaseConnectionFactory _factory;

    /// <summary>使用选定 PostgreSQL Provider 连接工厂初始化迁移器。</summary>
    /// <param name="factory">连接工厂。</param>
    public PostgreSqlMigrationRunner(DatabaseConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>串行应用每个模块的完整迁移计划；冲突或失败时事务回滚并阻止继续启动。</summary>
    /// <param name="steps">完整、版本连续的模块迁移清单。</param>
    /// <param name="cancellationToken">停止迁移的信号。</param>
    public async Task ApplyAsync(
        IEnumerable<MigrationStep> steps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var array = steps.ToArray();
        if (array.Length == 0)
        {
            return;
        }

        var groups = array
            .GroupBy(static step => step.ModuleId, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => group.OrderBy(static step => step.Version).ToArray())
            .ToArray();

        foreach (var group in groups)
        {
            for (var index = 0; index < group.Length; index++)
            {
                if (group[index].Version != index + 1)
                {
                    throw new ArgumentException("迁移版本必须从 1 开始连续且不可重复。", nameof(steps));
                }
            }
        }

        await using var transaction = await _factory.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // 全库共享的事务级 advisory lock，必须先锁定再创建技术版本账本。
        await transaction.ExecuteScalarAsync(
            new ParameterizedCommand("SELECT pg_advisory_xact_lock(702110011)", []),
            cancellationToken);
        await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "CREATE TABLE IF NOT EXISTS public.lotd_platform_migrations "
            + "(module_id varchar(64) NOT NULL, version integer NOT NULL, "
            + "checksum char(64) NOT NULL, applied_at timestamptz NOT NULL DEFAULT now(), "
            + "PRIMARY KEY (module_id, version))", []), cancellationToken);

        foreach (var group in groups)
        {
            var module = group[0].ModuleId;
            var maximum = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
                "SELECT MAX(version) FROM public.lotd_platform_migrations WHERE module_id=@module",
                [new SqlParameterValue("@module", DbType.String, module)]), cancellationToken);
            var appliedMaximum = maximum is null or DBNull
                ? 0 : Convert.ToInt32(maximum, CultureInfo.InvariantCulture);
            if (appliedMaximum > group.Length)
            {
                throw new InvalidOperationException("数据库迁移版本比当前程序更新，禁止降级启动。");
            }

            foreach (var step in group)
            {
                var existing = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
                    "SELECT checksum FROM public.lotd_platform_migrations "
                    + "WHERE module_id=@module AND version=@version",
                    [new SqlParameterValue("@module", DbType.String, step.ModuleId),
                     new SqlParameterValue("@version", DbType.Int32, step.Version)]),
                    cancellationToken);

                if (existing is not null and not DBNull)
                {
                    if (!string.Equals(Convert.ToString(existing, CultureInfo.InvariantCulture),
                        step.Checksum, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("已应用迁移校验和冲突，禁止修改历史迁移。");
                    }

                    continue;
                }

                if (step.Version <= appliedMaximum)
                {
                    throw new InvalidOperationException("已应用迁移账本存在版本空洞，禁止自动修复。");
                }

                // 业务 SQL 来自调用模块的静态版本文件，技术层只执行。
                await transaction.ExecuteNonQueryAsync(
                    new ParameterizedCommand(step.Sql, []), cancellationToken);
                await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
                    "INSERT INTO public.lotd_platform_migrations (module_id, version, checksum) "
                    + "VALUES (@module, @version, @checksum)",
                    [new SqlParameterValue("@module", DbType.String, step.ModuleId),
                     new SqlParameterValue("@version", DbType.Int32, step.Version),
                     new SqlParameterValue("@checksum", DbType.String, step.Checksum)]),
                    cancellationToken);
            }
        }

        if (await transaction.CommitAsync(cancellationToken) != CommitOutcome.Committed)
        {
            throw new InvalidOperationException(
                "数据库迁移提交结果未知，必须检查版本账本，禁止假定已回滚。");
        }
    }
}
