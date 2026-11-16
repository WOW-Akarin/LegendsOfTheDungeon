using System.Data;
using System.Globalization;
using LegendsOfTheDungeon.Platform.Persistence;
using Npgsql;

const string schema = "lotd_p11_probe";
var connectionString = Environment.GetEnvironmentVariable("LOTD_TEST_POSTGRES")
    ?? throw new InvalidOperationException("缺少 P11 专用测试数据库环境变量。");
var factory = new DatabaseConnectionFactory(NpgsqlFactory.Instance, connectionString);

await using (var connection = await factory.OpenAsync())
{
    using var version = connection.CreateCommand();
    version.CommandText = "SHOW server_version_num";
    var number = Convert.ToInt32(await version.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    Check(number is >= 180000 and < 190000, "集成必须连接 PostgreSQL 18。");

    using var prepare = connection.CreateCommand();
    prepare.CommandText = "CREATE SCHEMA IF NOT EXISTS " + schema + "; "
        + "CREATE TABLE IF NOT EXISTS " + schema
        + ".version_probe (id INTEGER PRIMARY KEY, version INTEGER NOT NULL, payload INTEGER NOT NULL); "
        + "CREATE TABLE IF NOT EXISTS " + schema
        + ".idempotency_probe (request_id TEXT PRIMARY KEY, result INTEGER NOT NULL); "
        + "TRUNCATE TABLE " + schema + ".version_probe, " + schema + ".idempotency_probe";
    await prepare.ExecuteNonQueryAsync();
}

try
{
    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var affected = await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.version_probe (id, version, payload) VALUES (@id, 0, @payload)",
            [new SqlParameterValue("@id", DbType.Int32, 101),
             new SqlParameterValue("@payload", DbType.Int32, 5)]));
        Check(affected == 1, "预备的回滚写入应成功。");
        await transaction.RollbackAsync();
        Check(transaction.State == PersistenceTransactionState.RolledBack, "回滚后状态不正确。");
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var count = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT count(*) FROM lotd_p11_probe.version_probe WHERE id=@id",
            [new SqlParameterValue("@id", DbType.Int32, 101)]));
        Check(Convert.ToInt64(count, CultureInfo.InvariantCulture) == 0, "Rollback 遗留写入。");
        await transaction.CommitAsync();
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var isolation = await transaction.ExecuteScalarAsync(
            new ParameterizedCommand("SHOW transaction_isolation", []));
        Check(string.Equals(Convert.ToString(isolation, CultureInfo.InvariantCulture), "serializable",
            StringComparison.Ordinal), "Serializable 隔离级别没有真正启用。");
        await transaction.RollbackAsync();
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.version_probe (id, version, payload) VALUES (@id, 0, 10)",
            [new SqlParameterValue("@id", DbType.Int32, 202)]));
        Check(await transaction.CommitAsync() == CommitOutcome.Committed,
            "初始版本写入未获得提交确认。");
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var updated = await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "UPDATE lotd_p11_probe.version_probe SET version=version+1, payload=@payload "
            + "WHERE id=@id AND version=@expected",
            [new SqlParameterValue("@payload", DbType.Int32, 20),
             new SqlParameterValue("@id", DbType.Int32, 202),
             new SqlParameterValue("@expected", DbType.Int32, 0)]));
        Check(updated == 1, "合法版本写应命中一次。");
        Check(await transaction.CommitAsync() == CommitOutcome.Committed, "版本写未确认。");
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var stale = await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "UPDATE lotd_p11_probe.version_probe SET version=version+1, payload=999 "
            + "WHERE id=@id AND version=@expected",
            [new SqlParameterValue("@id", DbType.Int32, 202),
             new SqlParameterValue("@expected", DbType.Int32, 0)]));
        Check(stale == 0, "旧版本错误地覆盖了新版本。");
        await transaction.RollbackAsync();
    }

    for (var attempt = 0; attempt < 2; attempt++)
    {
        await using var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var affected = await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.idempotency_probe (request_id, result) "
            + "VALUES (@request_id, 7) ON CONFLICT (request_id) DO NOTHING",
            [new SqlParameterValue("@request_id", DbType.String, "fixed-request-001")]));
        Check(affected == (attempt == 0 ? 1 : 0), "幂等请求不应产生第二次写入。");
        Check(await transaction.CommitAsync() == CommitOutcome.Committed, "幂等写未确认。");
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var count = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT count(*) FROM lotd_p11_probe.idempotency_probe", []));
        Check(Convert.ToInt64(count, CultureInfo.InvariantCulture) == 1, "重复请求写出了多条账本。");

        var value = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT payload FROM lotd_p11_probe.version_probe WHERE id=@id",
            [new SqlParameterValue("@id", DbType.Int32, 202)]));
        Check(Convert.ToInt32(value, CultureInfo.InvariantCulture) == 20, "旧版本覆盖了已确认状态。");
        await transaction.CommitAsync();
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await transaction.CommitAsync(cancellation.Token);
            throw new InvalidOperationException("提交前取消应该阻止提交。");
        }
        catch (OperationCanceledException)
        {
            Check(transaction.State == PersistenceTransactionState.Active,
                "提交前已取消不得进入 Unknown。");
        }

        await transaction.RollbackAsync();
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        await transaction.ExecuteNonQueryAsync(
            new ParameterizedCommand("SET LOCAL statement_timeout = '50ms'", []));
        try
        {
            await transaction.ExecuteScalarAsync(
                new ParameterizedCommand("SELECT pg_sleep(0.25)", []));
            throw new InvalidOperationException("服务器 SQL 超时未触发。");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            // 真实 PostgreSQL statement_timeout 已终止 SQL，需明确回滚整个事务。
        }

        await transaction.RollbackAsync();
    }

    var migrations = new PostgreSqlMigrationRunner(factory);
    var migrationPlan = new[]
    {
        new MigrationStep("p11.probe", 1,
            "CREATE TABLE lotd_p11_probe.migration_probe (id integer PRIMARY KEY, payload integer NOT NULL)"),
        new MigrationStep("p11.probe", 2,
            "INSERT INTO lotd_p11_probe.migration_probe (id, payload) VALUES (1, 1)")
    };
    await migrations.ApplyAsync(migrationPlan);
    await migrations.ApplyAsync(migrationPlan);
    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var count = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT COUNT(*) FROM lotd_p11_probe.migration_probe", []));
        Check(Convert.ToInt64(count, CultureInfo.InvariantCulture) == 1, "迁移重放写入重复数据。");
        await transaction.RollbackAsync();
    }

    try
    {
        await migrations.ApplyAsync([
            migrationPlan[0],
            new MigrationStep("p11.probe", 2,
                "INSERT INTO lotd_p11_probe.migration_probe (id, payload) VALUES (1, 2)")
        ]);
        throw new InvalidOperationException("历史迁移变更未被拒绝。");
    }
    catch (InvalidOperationException exception) when (
        exception.Message.Contains("校验和冲突", StringComparison.Ordinal))
    {
        // 已应用的 SQL checksum 不允许变更。
    }

    try
    {
        await migrations.ApplyAsync([
            migrationPlan[0], migrationPlan[1],
            new MigrationStep("p11.probe", 3,
                "INSERT INTO lotd_p11_probe.migration_probe (id, payload) VALUES (2, 2); SELECT 1/0")
        ]);
        throw new InvalidOperationException("错误迁移应该失败并回滚。");
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.DivisionByZero)
    {
        // 后续核查 DML 和 migration 账本全部回滚。
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var count = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT COUNT(*) FROM lotd_p11_probe.migration_probe WHERE id=2", []));
        Check(Convert.ToInt64(count, CultureInfo.InvariantCulture) == 0, "错误迁移未完整回滚。");
        var applied = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT COUNT(*) FROM public.lotd_platform_migrations "
            + "WHERE module_id=@module AND version=3",
            [new SqlParameterValue("@module", DbType.String, "p11.probe")]));
        Check(Convert.ToInt64(applied, CultureInfo.InvariantCulture) == 0,
            "错误迁移写入了版本账本。");
        await transaction.RollbackAsync();
    }

    var firstWrite = new ParameterizedCommand(
        "INSERT INTO lotd_p11_probe.migration_probe (id, payload) VALUES (@id, @payload)",
        [new SqlParameterValue("@id", DbType.Int32, 3),
         new SqlParameterValue("@payload", DbType.Int32, 30)]);
    var duplicateWrite = new ParameterizedCommand(
        "INSERT INTO lotd_p11_probe.migration_probe (id, payload) VALUES (@id, @payload)",
        [new SqlParameterValue("@id", DbType.Int32, 3),
         new SqlParameterValue("@payload", DbType.Int32, 31)]);

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        try
        {
            await BoundedBulkLoader.ExecuteAsync(transaction, [firstWrite, duplicateWrite], maximumCommands: 1);
            throw new InvalidOperationException("有界批量写入没有拒绝超限。");
        }
        catch (ArgumentException exception) when (
            exception.Message.Contains("超出允许上限", StringComparison.Ordinal))
        {
            // 超限在任何 SQL 运行前被拒绝。
        }

        await transaction.RollbackAsync();
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        try
        {
            await BoundedBulkLoader.ExecuteAsync(transaction, [firstWrite, duplicateWrite], maximumCommands: 2);
            throw new InvalidOperationException("重复主键应该触发整批回滚。");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            Check(transaction.State == PersistenceTransactionState.RolledBack,
                "有界写入失败没有将整个事务回滚。");
        }
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var count = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT COUNT(*) FROM lotd_p11_probe.migration_probe WHERE id=3", []));
        Check(Convert.ToInt64(count, CultureInfo.InvariantCulture) == 0,
            "批量写入失败遗留了第一条数据。");
        await transaction.RollbackAsync();
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var batch = await BoundedBulkLoader.ExecuteAsync(transaction,
            [firstWrite], maximumCommands: 2);
        Check(batch == 1, "成功批量写入数量不正确。");
        Check(await transaction.CommitAsync() == CommitOutcome.Committed,
            "成功批量写入必须由调用方明确提交。");
    }

    await using (var transaction = await factory.BeginTransactionWithFaultInjectionAsync(
        IsolationLevel.ReadCommitted, new FailAt(PersistenceFailurePoint.BeforeCommit)))
    {
        await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.version_probe (id, version, payload) VALUES (777, 0, 1)", []));
        try
        {
            await transaction.CommitAsync();
            throw new InvalidOperationException("提交前故障没有终止事务。");
        }
        catch (TimeoutException)
        {
            Check(transaction.State == PersistenceTransactionState.Active,
                "提交前故障应保持可回滚状态。");
        }
    }

    await using (var transaction = await factory.BeginTransactionWithFaultInjectionAsync(
        IsolationLevel.ReadCommitted, new FailAt(PersistenceFailurePoint.AfterCommit)))
    {
        await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.idempotency_probe (request_id, result) "
            + "VALUES (@request_id, 9) ON CONFLICT (request_id) DO NOTHING",
            [new SqlParameterValue("@request_id", DbType.String, "lost-ack-001")]));
        Check(await transaction.CommitAsync() == CommitOutcome.Unknown,
            "已落库但确认回执丢失必须报告 Unknown。");
        Check(transaction.State == PersistenceTransactionState.OutcomeUnknown,
            "Unknown 提交必须冻结当前会话。");
        try
        {
            await transaction.ExecuteScalarAsync(new ParameterizedCommand("SELECT 1", []));
            throw new InvalidOperationException("Unknown 事务不应再执行命令。");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("结果未知", StringComparison.Ordinal))
        {
            // Unknown 后必须重新建连接并查询持久账本。
        }
    }

    await using (var transaction = await factory.BeginTransactionWithFaultInjectionAsync(
        IsolationLevel.ReadCommitted, new FailAt(PersistenceFailurePoint.AfterRollback)))
    {
        await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.version_probe (id, version, payload) VALUES (778, 0, 1)", []));
        try
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("回滚结果丢失故障未触发。");
        }
        catch (TimeoutException)
        {
            Check(transaction.State == PersistenceTransactionState.OutcomeUnknown,
                "回滚确认丢失不允许报告 RolledBack。");
        }
    }

    await using (var transaction = await factory.BeginTransactionAsync(IsolationLevel.ReadCommitted))
    {
        var uncommitted = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT COUNT(*) FROM lotd_p11_probe.version_probe WHERE id IN (777,778)", []));
        Check(Convert.ToInt64(uncommitted, CultureInfo.InvariantCulture) == 0,
            "提交前/回滚后故障遗留了不应存在的行。");

        var persisted = await transaction.ExecuteScalarAsync(new ParameterizedCommand(
            "SELECT COUNT(*) FROM lotd_p11_probe.idempotency_probe "
            + "WHERE request_id=@request_id",
            [new SqlParameterValue("@request_id", DbType.String, "lost-ack-001")]));
        Check(Convert.ToInt64(persisted, CultureInfo.InvariantCulture) == 1,
            "丢失确认后必须查询到真实提交的幂等账本。");
        var retry = await transaction.ExecuteNonQueryAsync(new ParameterizedCommand(
            "INSERT INTO lotd_p11_probe.idempotency_probe (request_id, result) "
            + "VALUES (@request_id, 999) ON CONFLICT (request_id) DO NOTHING",
            [new SqlParameterValue("@request_id", DbType.String, "lost-ack-001")]));
        Check(retry == 0, "Unknown 消歧后重复请求不应再次写入。");
        Check(await transaction.CommitAsync() == CommitOutcome.Committed,
            "消歧事务应成功提交。");
    }

    Console.WriteLine("P11 failure injection PASS: before commit, after actual commit, "
        + "after actual rollback, unknown-receipt ledger recovery.");

    Console.WriteLine("P11 migration and bounded bulk PASS: repeat, checksum, "
        + "failed DDL rollback, prevalidation, atomic batch failure.");

    Console.WriteLine("P11 PostgreSQL 18 integration PASS: rollback, isolation, optimistic version, "
        + "idempotency, pre-commit cancellation, statement timeout, commit confirmation.");
}
finally
{
    await using var connection = await factory.OpenAsync();
    using var cleanup = connection.CreateCommand();
    cleanup.CommandText = "DROP SCHEMA IF EXISTS " + schema + " CASCADE";
    await cleanup.ExecuteNonQueryAsync();
}

static void Check(bool condition, string reason)
{
    if (!condition)
    {
        throw new InvalidOperationException(reason);
    }
}

internal sealed class FailAt : IPersistenceFailureInjector
{
    private readonly PersistenceFailurePoint _point;

    public FailAt(PersistenceFailurePoint point)
    {
        _point = point;
    }

    public void Trigger(PersistenceFailurePoint point)
    {
        if (point == _point)
        {
            throw new TimeoutException("专用测试故障点：模拟数据库确认回执丢失。");
        }
    }
}
