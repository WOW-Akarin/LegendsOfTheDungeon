using System.Data;
using System.Text;
using LegendsOfTheDungeon.Platform.Persistence;
using LegendsOfTheDungeon.StaticData.Abstractions;
using LegendsOfTheDungeon.StaticData.Core;
using LegendsOfTheDungeon.StaticData.Json;
using LegendsOfTheDungeon.StaticData.Sql;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("LOTD_TEST_POSTGRES")
    ?? throw new InvalidOperationException("LOTD_TEST_POSTGRES is required.");
var factory = new DatabaseConnectionFactory(NpgsqlFactory.Instance, connectionString);
var loader = new PostgreSqlStaticCatalogLoader(factory.OpenAsync);
CatalogReloadCoordinator? successfulRegistry = null;
await InitializeAsync();

await CheckAsync("postgresql-json-same-content-same-sha256", EquivalenceAsync);
await CheckAsync("postgresql-version-guard-is-fail-closed", VersionGuardAsync);
await CheckAsync("postgresql-invalid-fields-identify-source", FieldFailureAsync);
await CheckAsync("postgresql-mismatched-record-id-is-rejected", RecordIdFailureAsync);
await CheckAsync("postgresql-overlimit-payload-does-not-publish", PayloadLimitAsync);
await CheckAsync("postgresql-row-count-hard-bound", RecordLimitAsync);
await CheckAsync("cancelled-database-read-releases-connection", CancellationAsync);
Console.WriteLine("P15_STATICDATA_POSTGRES_PASS");
await CheckAsync("p16-real-sql-bundle-atomic-reload", PackageReloadAsync);
await CheckAsync("p16-real-sql-bundle-invalid-reference-keeps-old", PackageInvalidReferenceAsync);
Console.WriteLine("P16_CATALOG_RELOAD_POSTGRES_PASS");

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

async Task AddAsync(string recordId, string payload, int dataVersion = 7)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO public.lotd_static_catalog_rows
          (catalog_name, schema_version, data_version, record_id, payload_json)
        VALUES (@catalog, 3, @data_version, @id, @payload::jsonb)
        """;
    command.Parameters.AddWithValue("@catalog", "fixture_catalog");
    command.Parameters.AddWithValue("@data_version", dataVersion);
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

static StaticCatalogSchema RelationSchema() => new("relations", 1,
[
    new StaticFieldSchema("target", StaticFieldType.Text, true, 64),
    new StaticFieldSchema("label", StaticFieldType.Text, true, 80)
]);

static CatalogReferenceRule RelationRule() =>
    new("relations", "target", "fixture_catalog");

// 仅重置测试目录的版本和行，不触及正式业务数据。
async Task SetRelationAsync(int version, string target)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using (var clear = connection.CreateCommand())
    {
        clear.Transaction = tx;
        clear.CommandText = """
            DELETE FROM public.lotd_static_catalog_rows WHERE catalog_name = 'relations';
            DELETE FROM public.lotd_static_catalog_versions WHERE catalog_name = 'relations';
            """;
        await clear.ExecuteNonQueryAsync();
    }

    await using (var versionCommand = connection.CreateCommand())
    {
        versionCommand.Transaction = tx;
        versionCommand.CommandText = """
            INSERT INTO public.lotd_static_catalog_versions (catalog_name, schema_version, data_version)
            VALUES ('relations', 1, @version)
            """;
        versionCommand.Parameters.AddWithValue("@version", version);
        await versionCommand.ExecuteNonQueryAsync();
    }

    await using (var insert = connection.CreateCommand())
    {
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO public.lotd_static_catalog_rows
              (catalog_name, schema_version, data_version, record_id, payload_json)
            VALUES ('relations', 1, @version, 'edge',
              jsonb_build_object('id','edge','target',@target,'label','relation'))
            """;
        insert.Parameters.AddWithValue("@version", version);
        insert.Parameters.AddWithValue("@target", target);
        await insert.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
}

async Task<CatalogPackageSnapshot> LoadPackageFromDatabaseAsync(
    int version, CancellationToken cancellationToken = default)
{
    var definitions = await loader.LoadAsync(
        Schema(), version, "postgres_fixture", cancellationToken);
    var relations = await loader.LoadAsync(
        RelationSchema(), version, "postgres_relations", cancellationToken);
    return CatalogPackageBuilder.Build(
        version, [definitions, relations], [RelationRule()]);
}

async Task PackageReloadAsync()
{
    await ResetAsync(dataVersion: 7);
    await AddAsync("a", """{"id":"a","name":"first","amount":1}""");
    await SetRelationAsync(7, "a");
    var first = await LoadPackageFromDatabaseAsync(7);
    Check(first.Catalogs.Count == 2 &&
        first.Find("relations")?.Find("edge")?.Fields["target"].CanonicalText == "a",
        "P16 cross-catalog relations must resolve in actual SQL data");

    var registry = new CatalogReloadCoordinator(first, maximumHistory: 3);
    successfulRegistry = registry;
    using var pinned = registry.RegisterActiveDefinition(
        Guid.NewGuid(), "fixture_catalog", "a", ActiveDefinitionPolicy.PinOldVersion);

    await ResetAsync(dataVersion: 8);
    await AddAsync("b", """{"id":"b","name":"second","amount":2}""", 8);
    await SetRelationAsync(8, "b");
    var ticket = await registry.PrepareAsync(
        ct => LoadPackageFromDatabaseAsync(8, ct));
    Check(ReferenceEquals(registry.Current, first),
        "successfully prepared SQL bundle must not be prematurely published");

    using var tick = registry.CaptureTick(1);
    var sameTick = registry.CaptureTick(1);
    Check(ReferenceEquals(tick.Package, sameTick.Package),
        "same tick must observe exactly the same catalog package");
    var blocked = false;
    try
    {
        registry.CommitAtBoundary(1, ticket);
    }
    catch (CatalogReloadException ex) when (ex.Fault == CatalogReloadFault.TickActive)
    {
        blocked = true;
    }

    Check(blocked, "active tick should reject SQL bundle commit");
    sameTick.Dispose();
    tick.Dispose();
    registry.CommitAtBoundary(1, ticket);
    Check(registry.Current.Version == 8 &&
        registry.Current.Find("fixture_catalog")?.Find("b") is not null &&
        registry.Current.Find("relations")?.Find("edge")?.Fields["target"].CanonicalText == "b",
        "SQL bundle must switch all related catalogs atomically");
    Check(registry.FindRetained(7, first.ContentHash) is not null,
        "old frozen bundle must remain available for replay while pinned");
    using var newTick = registry.CaptureTick(2);
    Check(ReferenceEquals(newTick.Package, registry.Current),
        "next logical tick must see all of updated SQL bundle");
    Check(ReferenceEquals(tick.Package, first),
        "old completed tick must not switch to new catalog");
}

async Task PackageInvalidReferenceAsync()
{
    var registry = successfulRegistry ??
        throw new InvalidOperationException("required SQL reload fixture was not initialized");
    var current = registry.Current;
    await ResetAsync(dataVersion: 9);
    await AddAsync("c", """{"id":"c","name":"third","amount":3}""", 9);
    await SetRelationAsync(9, "absent");
    var rejected = false;
    try
    {
        _ = await registry.PrepareAsync(ct => LoadPackageFromDatabaseAsync(9, ct));
    }
    catch (CatalogPackageException ex)
        when (ex.Fault == CatalogPackageFault.MissingReference &&
              ex.Catalog == "relations" && ex.RowIndex == 1 && ex.Field == "target")
    {
        Check(!ex.Message.Contains("absent", StringComparison.Ordinal),
            "invalid SQL payload must not escape into diagnostics");
        rejected = true;
    }

    Check(rejected, "cross-catalog missing SQL target must be rejected by P16.F");
    Check(ReferenceEquals(registry.Current, current) &&
        registry.Current.Version == 8 &&
        registry.Current.Find("fixture_catalog")?.Find("b") is not null,
        "failed SQL candidate must preserve current valid bundle");
}
