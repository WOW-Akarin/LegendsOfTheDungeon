using System.Data;
using System.Data.Common;
using System.Text;
using LegendsOfTheDungeon.Platform.Persistence;
using LegendsOfTheDungeon.StaticData.Abstractions;
using LegendsOfTheDungeon.StaticData.Core;
using LegendsOfTheDungeon.StaticData.Json;

namespace LegendsOfTheDungeon.StaticData.Sql;

/// <summary>固定技术表的 PostgreSQL 来源 Adapter；借助 P11 ADO.NET 工厂与参数化命令。</summary>
public sealed class PostgreSqlStaticCatalogLoader
{
    private const string VersionQuery = """
        SELECT schema_version, data_version
        FROM public.lotd_static_catalog_versions
        WHERE catalog_name = @catalog
        """;
    private const string RowsQuery = """
        SELECT record_id,
               CASE WHEN octet_length(payload_json::text) <= @max_bytes
                    THEN payload_json::text ELSE NULL END,
               octet_length(payload_json::text)
        FROM public.lotd_static_catalog_rows
        WHERE catalog_name = @catalog
          AND schema_version = @schema_version
          AND data_version = @data_version
        ORDER BY record_id COLLATE "C"
        LIMIT @record_limit
        """;

    private readonly DatabaseConnectionFactory _factory;

    /// <summary>仅注入 P11 通用连接工厂；Npgsql 由组合根选择，不进入生产项目包引用。</summary>
    /// <param name="factory">会释放连接并隐藏连接串的 P11 工厂。</param>
    public PostgreSqlStaticCatalogLoader(DatabaseConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>在只读逻辑的可重复读事务中校验版本并取得有限行，统一 JSON 规范化与冻结 Hash。</summary>
    /// <param name="schema">领域拥有的通用 Schema。</param>
    /// <param name="expectedDataVersion">固定正整数数据版本。</param>
    /// <param name="sourceName">安全的审计来源标识。</param>
    /// <param name="cancellationToken">I/O 取消令牌。</param>
    /// <returns>完整读取/校验并释放数据库资源后的冻结快照。</returns>
    public async Task<FrozenStaticCatalog> LoadAsync(
        StaticCatalogSchema schema, int expectedDataVersion, string sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!StaticIdentifier.IsValid(sourceName))
        {
            throw new ArgumentException("SQL 来源标识必须为 ASCII 小写稳定名称。", nameof(sourceName));
        }

        if (expectedDataVersion <= 0)
        {
            throw new StaticCatalogException(
                StaticCatalogFault.VersionMismatch, sourceName, 0, "data_version");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await _factory.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);

            await VerifyVersionAsync(
                connection, transaction, schema, expectedDataVersion, sourceName,
                cancellationToken);

            var rows = await ReadRowsAsync(
                connection, transaction, schema, expectedDataVersion, sourceName,
                cancellationToken);

            // 任何失败必须在 Build 前离开，不发布部分 Catalog；
            // 可重复读事务防止版本头和 SQL 行在两次 SELECT 间混合。
            var snapshot = StaticCatalogBuilder.Build(
                schema, expectedDataVersion, rows, sourceName);
            await transaction.CommitAsync(cancellationToken);
            return snapshot;
        }
        catch (DbException)
        {
            // 数据库 Provider 异常可能包含 SQL/连接串/参数，禁止复制原始 Message。
            throw new StaticCatalogException(
                StaticCatalogFault.SourceUnavailable, sourceName, 0, "database");
        }
    }

    private static async Task VerifyVersionAsync(
        DbConnection connection, DbTransaction transaction,
        StaticCatalogSchema schema, int expectedDataVersion,
        string sourceName, CancellationToken cancellationToken)
    {
        var request = new ParameterizedCommand(VersionQuery,
        [
            new SqlParameterValue("@catalog", DbType.String, schema.CatalogName)
        ]);
        using var command = request.CreateDbCommand(connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new StaticCatalogException(
                StaticCatalogFault.VersionMismatch, sourceName, 0, "catalog");
        }

        var schemaVersion = reader.GetInt32(0);
        var dataVersion = reader.GetInt32(1);
        if (schemaVersion != schema.SchemaVersion)
        {
            throw new StaticCatalogException(
                StaticCatalogFault.VersionMismatch, sourceName, 0, "schema_version");
        }

        if (dataVersion != expectedDataVersion)
        {
            throw new StaticCatalogException(
                StaticCatalogFault.VersionMismatch, sourceName, 0, "data_version");
        }
    }

    private static async Task<IReadOnlyList<StaticDataRow>> ReadRowsAsync(
        DbConnection connection, DbTransaction transaction,
        StaticCatalogSchema schema, int expectedDataVersion,
        string sourceName, CancellationToken cancellationToken)
    {
        var commandSpec = new ParameterizedCommand(RowsQuery,
        [
            new SqlParameterValue("@catalog", DbType.String, schema.CatalogName),
            new SqlParameterValue("@schema_version", DbType.Int32, schema.SchemaVersion),
            new SqlParameterValue("@data_version", DbType.Int32, expectedDataVersion),
            new SqlParameterValue("@max_bytes", DbType.Int32, StaticCatalogLimits.MaximumDocumentBytes),
            new SqlParameterValue("@record_limit", DbType.Int32, StaticCatalogLimits.MaximumRecords + 1)
        ]);

        using var command = commandSpec.CreateDbCommand(connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StaticDataRow>();
        var totalBytes = 0L;
        while (await reader.ReadAsync(cancellationToken))
        {
            var index = rows.Count + 1;
            if (index > StaticCatalogLimits.MaximumRecords)
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.LimitExceeded, sourceName, index, "records");
            }

            var bytes = reader.GetInt32(2);
            if (bytes < 0 || bytes > StaticCatalogLimits.MaximumDocumentBytes
                || bytes > StaticCatalogLimits.MaximumDocumentBytes - totalBytes
                || reader.IsDBNull(1))
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.LimitExceeded, sourceName, index, "document");
            }

            var rowId = reader.GetString(0);
            var payload = Encoding.UTF8.GetBytes(reader.GetString(1));
            totalBytes += payload.Length;
            if (payload.Length != bytes || totalBytes > StaticCatalogLimits.MaximumDocumentBytes)
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.LimitExceeded, sourceName, index, "document");
            }

            var row = JsonStaticCatalogLoader.ParseRecord(schema, payload, sourceName, index);
            if (!string.Equals(row.Id, rowId, StringComparison.Ordinal))
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.InvalidValue, sourceName, index, "id");
            }

            rows.Add(row);
        }

        return rows;
    }
}
