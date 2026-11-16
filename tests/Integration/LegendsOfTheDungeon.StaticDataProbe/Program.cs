using System.Data;
using System.Text;
using LegendsOfTheDungeon.Platform.Persistence;
using LegendsOfTheDungeon.StaticData.Abstractions;
using LegendsOfTheDungeon.StaticData.Json;
using LegendsOfTheDungeon.StaticData.Sql;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("LOTD_TEST_POSTGRES")
    ?? throw new InvalidOperationException("LOTD_TEST_POSTGRES is required.");
var factory = new DatabaseConnectionFactory(NpgsqlFactory.Instance, connectionString);
var loader = new PostgreSqlStaticCatalogLoader(factory.OpenAsync);
await InitializeAsync();

await CheckAsync("postgresql-json-same-content-same-sha256", EquivalenceAsync);
await CheckAsync("postgresql-version-guard-is-fail-closed", VersionGuardAsync);
await CheckAsync("postgresql-invalid-fields-identify-source", FieldFailureAsync);
await CheckAsync("postgresql-mismatched-record-id-is-rejected", RecordIdFailureAsync);
await CheckAsync("postgresql-overlimit-payload-does-not-publish", PayloadLimitAsync);
await CheckAsync("postgresql-row-count-hard-bound", RecordLimitAsync);
await CheckAsync("cancelled-database-read-releases-connection", CancellationAsync);
Console.WriteLine("P15_STATICDATA_POSTGRES_PASS");

static StaticCatalogSchema Schema() => new("fixture_catalog", 3,
[
    new StaticFieldSchema("name", StaticFieldType.Text, true, 100),
    new StaticFieldSchema("amount", StaticFieldType.FixedPoint, true),
    new StaticFieldSchema("active", StaticFieldType.Boolean, false)
]);

static void Check(bool condition, string reason)
{
    if (!condition)
    {
        throw new InvalidOperationException(reason);
    }
}

static async Task CheckAsync(string name, Func<Task> probe)
{
    await probe().WaitAsync(TimeSpan.FromSeconds(25));
    Console.WriteLine("PASS " + name);
}

async Task InitializeAsync()
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS public.lotd_static_catalog_versions (
          catalog_name text PRIMARY KEY,
          schema_version integer NOT NULL CHECK (schema_version > 0),
          data_version integer NOT NULL CHECK (data_version > 0)
        );
        CREATE TABLE IF NOT EXISTS public.lotd_static_catalog_rows (
          catalog_name text NOT NULL,
          schema_version integer NOT NULL,
          data_version integer NOT NULL,
          record_id text NOT NULL,
          payload_json jsonb NOT NULL,
          PRIMARY KEY (catalog_name, schema_version, data_version, record_id)
        );
        """;
    await command.ExecuteNonQueryAsync();
}

async Task ResetAsync(int schemaVersion = 3, int dataVersion = 7)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var clear = connection.CreateCommand())
    {
        clear.Transaction = transaction;
        clear.CommandText = """
            DELETE FROM public.lotd_static_catalog_rows WHERE catalog_name = @catalog;
            DELETE FROM public.lotd_static_catalog_versions WHERE catalog_name = @catalog;
            """;
        clear.Parameters.AddWithValue("@catalog", "fixture_catalog");
        await clear.ExecuteNonQueryAsync();
    }

    await using (var insert = connection.CreateCommand())
    {
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO public.lotd_static_catalog_versions
            (catalog_name, schema_version, data_version)
            VALUES (@catalog, @schema_version, @data_version)
            """;
        insert.Parameters.AddWithValue("@catalog", "fixture_catalog");
        insert.Parameters.AddWithValue("@schema_version", schemaVersion);
        insert.Parameters.AddWithValue("@data_version", dataVersion);
        await insert.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

async Task AddAsync(string recordId, string payload)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO public.lotd_static_catalog_rows
          (catalog_name, schema_version, data_version, record_id, payload_json)
        VALUES (@catalog, 3, 7, @id, @payload::jsonb)
        """;
    command.Parameters.AddWithValue("@catalog", "fixture_catalog");
    command.Parameters.AddWithValue("@id", recordId);
    command.Parameters.AddWithValue("@payload", payload);
    await command.ExecuteNonQueryAsync();
}

async Task EquivalenceAsync()
{
    await ResetAsync();
    await AddAsync("b", """{"active":true,"amount":1.00,"name":"Beta","id":"b"}""");
    await AddAsync("a", """{"amount":2.50,"id":"a","name":"Café"}""");
    // SQL 的物理 ID 使用分解 Unicode，而 JSON 来源使用 NFC，仍是同一逻辑记录。
    await AddAsync("e\u0301", """{"amount":3.0,"id":"e\u0301","name":"Accent"}""");
    var sql = await loader.LoadAsync(Schema(), 7, "postgres_fixture");
    var json = JsonStaticCatalogLoader.Load(Schema(), 7, Encoding.UTF8.GetBytes("""
        {"records":[
          {"id":"a","name":"Cafe\u0301","amount":2.5},
          {"name":"Beta","id":"b","amount":1e0,"active":true},
          {"name":"Accent","id":"é","amount":3}],
         "catalog":"fixture_catalog","dataVersion":7,"schemaVersion":3}
        """), "json_fixture");
    Check(sql.ContentHash == json.ContentHash,
        "same PostgreSQL JSONB and JSON-file data must have same hash");
    Check(sql.Records.Count == 3 && sql.Records[0].Id == "a" && sql.Records[1].Id == "b"
        && sql.Records[2].Id == "é",
        "SQL query must normalize record order");
    Check(sql.Find("a")?.Fields["name"].CanonicalText == "Café",
        "Unicode normalization should match data source");
    Check(sql.Find("e\u0301")?.Id == "é", "SQL record IDs must normalize to NFC");
    Check(sql.Find("b")?.Fields["amount"].CanonicalText == "1",
        "Decimal 1.00 must have normalized canonical representation");
}

async Task VersionGuardAsync()
{
    var error = await FailAsync(
        () => loader.LoadAsync(Schema(), 6, "postgres_fixture"));
    Check(error.Fault == StaticCatalogFault.VersionMismatch
        && error.FieldName == "data_version", "wrong data version not rejected");
    var outdatedSchema = new StaticCatalogSchema("fixture_catalog", 4, Schema().Fields);
    error = await FailAsync(
        () => loader.LoadAsync(outdatedSchema, 7, "postgres_fixture"));
    Check(error.Fault == StaticCatalogFault.VersionMismatch
        && error.FieldName == "schema_version", "wrong schema version not rejected");
    // 之前已冻结的 Catalog 不受失败加载或数据库版本头变动影响。
    Check((await loader.LoadAsync(Schema(), 7, "postgres_fixture"))
        .Records.Count == 3, "failed load damaged catalog content");
}

async Task FieldFailureAsync()
{
    await ResetAsync();
    await AddAsync("invalid", """{"id":"invalid","name":"broken","amount":2,"surprise":"x"}""");
    var ex = await FailAsync(() => loader.LoadAsync(Schema(), 7, "postgres_fixture"));
    Check(ex.Fault == StaticCatalogFault.UnknownField
        && ex.SourceName == "postgres_fixture"
        && ex.RowIndex == 1 && ex.FieldName == "surprise",
        "SQL payload validation must reveal safe field/row/source");
    Check(!ex.Message.Contains("broken", StringComparison.Ordinal),
        "source data must not appear in error messages");
}

async Task RecordIdFailureAsync()
{
    await ResetAsync();
    await AddAsync("db_id", """{"id":"payload_id","name":"x","amount":1}""");
    var ex = await FailAsync(() => loader.LoadAsync(Schema(), 7, "postgres_fixture"));
    Check(ex.Fault == StaticCatalogFault.InvalidValue
        && ex.FieldName == "id" && ex.RowIndex == 1,
        "SQL record_id and JSONB id mismatch must be rejected");
}

async Task PayloadLimitAsync()
{
    await ResetAsync();
    var longName = new string('a', StaticCatalogLimits.MaximumDocumentBytes + 1);
    await AddAsync("oversize", $$"""{"id":"oversize","name":"{{longName}}","amount":1}""");
    var ex = await FailAsync(() => loader.LoadAsync(Schema(), 7, "postgres_fixture"));
    Check(ex.Fault == StaticCatalogFault.LimitExceeded
        && ex.FieldName == "document" && ex.RowIndex == 1,
        "oversize JSONB payload should be rejected before allocation");
}

async Task RecordLimitAsync()
{
    await ResetAsync();
    // 池上限为 1；写入连接在加载器打开新连接之前必须完全释放。
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO public.lotd_static_catalog_rows
              (catalog_name, schema_version, data_version, record_id, payload_json)
            SELECT @catalog, 3, 7, g::text,
              jsonb_build_object('id',g::text,'name','x','amount',1)
            FROM generate_series(1,10001) AS g
            """;
        insert.Parameters.AddWithValue("@catalog", "fixture_catalog");
        await insert.ExecuteNonQueryAsync();
    }

    var error = await FailAsync(() => loader.LoadAsync(Schema(), 7, "postgres_fixture"));
    Check(error.Fault == StaticCatalogFault.LimitExceeded
        && error.FieldName == "records"
        && error.RowIndex == StaticCatalogLimits.MaximumRecords + 1,
        "SQL maxRows+1 sentinel must detect oversized catalog");
}

async Task CancellationAsync()
{
    await ResetAsync();
    await AddAsync("safe", """{"id":"safe","name":"safe","amount":1}""");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    var observed = false;
    try
    {
        await loader.LoadAsync(Schema(), 7, "postgres_fixture", cancelled.Token);
    }
    catch (OperationCanceledException)
    {
        observed = true;
    }

    Check(observed, "pre-cancelled SQL read must throw cancellation");
    var result = await loader.LoadAsync(Schema(), 7, "postgres_fixture");
    Check(result.Records.Count == 1 && result.Records[0].Id == "safe",
        "cancelled SQL read leaked pool connection or corrupted next read");
}

static async Task<StaticCatalogException> FailAsync(Func<Task<LegendsOfTheDungeon.StaticData.Core.FrozenStaticCatalog>> action)
{
    try
    {
        _ = await action();
    }
    catch (StaticCatalogException e)
    {
        return e;
    }

    throw new InvalidOperationException("expected stable StaticCatalogException");
}
