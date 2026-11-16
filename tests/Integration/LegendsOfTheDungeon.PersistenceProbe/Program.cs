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
