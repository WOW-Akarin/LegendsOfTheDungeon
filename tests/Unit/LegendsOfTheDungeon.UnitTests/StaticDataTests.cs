using System.Text;
using LegendsOfTheDungeon.StaticData.Abstractions;
using LegendsOfTheDungeon.StaticData.Core;
using LegendsOfTheDungeon.StaticData.Json;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class StaticDataTests
{
    private static StaticCatalogSchema Schema() => new("test_catalog", 2,
    [
        new StaticFieldSchema("label", StaticFieldType.Text, true, 60),
        new StaticFieldSchema("quantity", StaticFieldType.WholeNumber, true),
        new StaticFieldSchema("cost", StaticFieldType.FixedPoint, false),
        new StaticFieldSchema("enabled", StaticFieldType.Boolean, false)
    ]);

    private static FrozenStaticCatalog Load(string json, int version = 7) =>
        JsonStaticCatalogLoader.Load(Schema(), version,
            Encoding.UTF8.GetBytes(json), "json_fixture");

    private static string Envelope(string records, int version = 7) =>
        $$"""{"catalog":"test_catalog","schemaVersion":2,"dataVersion":{{version}},"records":[{{records}}]}""";

    [Fact]
    public void JsonRecordAndPropertyOrderCannotAlterCanonicalHash()
    {
        var a = Load(Envelope("""
            {"id":"b","label":"Beta","quantity":2,"cost":1.00,"enabled":true},
            {"id":"a","label":"Alpha","quantity":1,"cost":2.500}
            """));
        var b = Load("""
            {"records":[
                {"cost":2.5,"quantity":1,"label":"Alpha","id":"a"},
                {"enabled":true,"cost":1e0,"label":"Beta","quantity":2,"id":"b"}],
            "dataVersion":7,"schemaVersion":2,"catalog":"test_catalog"}
            """);

        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.Equal(64, a.ContentHash.Length);
        Assert.Equal(["a", "b"], a.Records.Select(static row => row.Id));
        Assert.Equal("1", a.Find("b")!.Fields["cost"].CanonicalText);
        Assert.Equal("2.5", a.Find("a")!.Fields["cost"].CanonicalText);
        Assert.Null(a.Find("missing"));
        Assert.Equal("Alpha", a.Find("a")!.Fields["label"].CanonicalText);
    }

    [Fact]
    public void UnicodeNormalizationProducesIdenticalCatalogHash()
    {
        var composed = Load(Envelope("{\"id\":\"é\",\"label\":\"Café\",\"quantity\":4}"));
        var decomposed = Load(Envelope("{\"id\":\"e\\u0301\",\"label\":\"Cafe\\u0301\",\"quantity\":4}"));
        Assert.Equal(composed.ContentHash, decomposed.ContentHash);
        Assert.NotNull(composed.Find("e\u0301"));
        Assert.Equal("Café", decomposed.Find("é")!.Fields["label"].CanonicalText);
    }

    [Fact]
    public void DataSchemaAndOptionalContentChangesAlterHash()
    {
        var noOptional = Load(Envelope("""{"id":"a","label":"a","quantity":1}"""));
        var withOptional = Load(Envelope("""{"id":"a","label":"a","quantity":1,"enabled":false}"""));
        var newer = Load(Envelope("""{"id":"a","label":"a","quantity":1}""", 8), 8);
        Assert.NotEqual(noOptional.ContentHash, withOptional.ContentHash);
        Assert.NotEqual(noOptional.ContentHash, newer.ContentHash);
        var differentSchema = new StaticCatalogSchema("test_catalog", 3, Schema().Fields);
        var differentJson = """{"catalog":"test_catalog","schemaVersion":3,"dataVersion":7,"records":[{"id":"a","label":"a","quantity":1}]}""";
        var updated = JsonStaticCatalogLoader.Load(
            differentSchema, 7, Encoding.UTF8.GetBytes(differentJson), "json_fixture");
        Assert.NotEqual(noOptional.ContentHash, updated.ContentHash);
    }

    [Fact]
    public void FrozenRecordsCannotBeModifiedAfterPublication()
    {
        var rows = new List<KeyValuePair<string, StaticScalar>>
        {
            new("label", StaticScalar.FromText("original")),
            new("quantity", StaticScalar.FromInt64(1))
        };
        var item = new StaticDataRow("x", rows);
        rows[0] = new KeyValuePair<string, StaticScalar>("label", StaticScalar.FromText("mutated"));
        var records = new List<StaticDataRow> { item };
        var snapshot = StaticCatalogBuilder.Build(Schema(), 7, records, "memory_fixture");
        records.Clear();
        Assert.Equal("original", snapshot.Find("x")!.Fields["label"].CanonicalText);
        Assert.Single(snapshot.Records);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<StaticDataRow>)snapshot.Records).Add(item));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, StaticScalar>)item.Fields)
                .Add("enabled", StaticScalar.FromBoolean(true)));
    }

    [Theory]
    [InlineData("""{"id":"a","label":"x","quantity":1},{"id":"a","label":"y","quantity":2}""",
        StaticCatalogFault.Duplicate, 2, "id")]
    [InlineData("""{"id":"a","label":"x","quantity":"1"}""",
        StaticCatalogFault.InvalidValue, 1, "quantity")]
    [InlineData("""{"id":"a","label":"x"}""",
        StaticCatalogFault.MissingField, 1, "quantity")]
    [InlineData("""{"id":"a","label":"x","quantity":1,"unexpected":8}""",
        StaticCatalogFault.UnknownField, 1, "unexpected")]
    [InlineData("""{"id":"a","label":"x","quantity":1,"quantity":2}""",
        StaticCatalogFault.Duplicate, 1, "quantity")]
    [InlineData("""{"id":"a","label":null,"quantity":1}""",
        StaticCatalogFault.InvalidValue, 1, "label")]
    [InlineData("""{"id":"a","label":"x","quantity":1.5}""",
        StaticCatalogFault.InvalidValue, 1, "quantity")]
    [InlineData("""{"id":"bad\u0000","label":"x","quantity":1}""",
        StaticCatalogFault.InvalidValue, 1, "id")]
    public void InvalidRecordIsRejectedWithSourceRowAndField(
        string records, StaticCatalogFault expected, int row, string field)
    {
        var error = Assert.Throws<StaticCatalogException>(() =>
            Load(Envelope(records)));
        Assert.Equal(expected, error.Fault);
        Assert.Equal("json_fixture", error.SourceName);
        Assert.Equal(row, error.RowIndex);
        Assert.Equal(field, error.FieldName);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"catalog":"test_catalog","schemaVersion":99,"dataVersion":7,"records":[]}""",
        StaticCatalogFault.VersionMismatch, "schema_version")]
    [InlineData("""{"catalog":"test_catalog","schemaVersion":2,"dataVersion":6,"records":[]}""",
        StaticCatalogFault.VersionMismatch, "data_version")]
    [InlineData("""{"catalog":"other","schemaVersion":2,"dataVersion":7,"records":[]}""",
        StaticCatalogFault.VersionMismatch, "catalog")]
    [InlineData("""{"catalog":"test_catalog","schemaVersion":2,"dataVersion":7,"records":[],"records":[]}""",
        StaticCatalogFault.Duplicate, "document")]
    [InlineData("""{"catalog":"test_catalog","schemaVersion":2,"dataVersion":7}""",
        StaticCatalogFault.MissingField, "records")]
    [InlineData("""{"catalog":"test_catalog","schemaVersion":2,"dataVersion":7,"records":[}""",
        StaticCatalogFault.InvalidDocument, "document")]
    public void InvalidRootMetadataAndSyntaxReportSafeSource(
        string json, StaticCatalogFault fault, string field)
    {
        var ex = Assert.Throws<StaticCatalogException>(() => Load(json));
        Assert.Equal(fault, ex.Fault);
        Assert.Equal(0, ex.RowIndex);
        Assert.Equal(field, ex.FieldName);
        Assert.Equal("json_fixture", ex.SourceName);
        Assert.DoesNotContain("test_catalog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonBudgetAndRecordLimitAreHardFailures()
    {
        var tooLarge = new byte[StaticCatalogLimits.MaximumDocumentBytes + 1];
        Assert.Equal(StaticCatalogFault.LimitExceeded,
            Assert.Throws<StaticCatalogException>(() =>
                JsonStaticCatalogLoader.Load(Schema(), 7, tooLarge, "json_fixture")).Fault);
        var objects = string.Join(",", Enumerable.Range(1, StaticCatalogLimits.MaximumRecords + 1)
            .Select(static i => $$"""{"id":"{{i}}","label":"a","quantity":1}"""));
        var error = Assert.Throws<StaticCatalogException>(() => Load(Envelope(objects)));
        Assert.Equal(StaticCatalogFault.LimitExceeded, error.Fault);
        Assert.Equal(StaticCatalogLimits.MaximumRecords + 1, error.RowIndex);
    }

    [Fact]
    public void EmptyCatalogProducesStableHashAndMissingRecord()
    {
        var empty = Load(Envelope(""));
        Assert.Empty(empty.Records);
        Assert.Equal(64, empty.ContentHash.Length);
        Assert.Equal(empty.ContentHash, Load(Envelope("")).ContentHash);
    }

    [Fact]
    public void SchemaConfigurationChecksReservedAndDuplicateFields()
    {
        Assert.Throws<ArgumentException>(() => new StaticFieldSchema("id", StaticFieldType.Text, true));
        Assert.Throws<ArgumentException>(() => new StaticFieldSchema("A B", StaticFieldType.Text, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StaticFieldSchema(
            "price", (StaticFieldType)999, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StaticCatalogSchema(
            "valid", 0, Array.Empty<StaticFieldSchema>()));
        Assert.Throws<ArgumentException>(() => new StaticCatalogSchema("valid", 1,
        [
            new StaticFieldSchema("name", StaticFieldType.Text, true),
            new StaticFieldSchema("name", StaticFieldType.Text, false)
        ]));
    }

    [Fact]
    public void DistinctInputEnumerationBuildsSameCanonicalResultUnderConcurrency()
    {
        var source = Load(Envelope("""
            {"id":"a","label":"A","quantity":1},{"id":"b","label":"B","quantity":2},
            {"id":"c","label":"C","quantity":3}
            """));
        var results = new string[500];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = JsonStaticCatalogLoader.Load(
                Schema(), 7, Encoding.UTF8.GetBytes(
                    i % 2 == 0
                        ? Envelope("""
                            {"id":"c","label":"C","quantity":3},
                            {"id":"a","label":"A","quantity":1},
                            {"id":"b","label":"B","quantity":2}
                            """)
                        : Envelope("""
                            {"id":"b","label":"B","quantity":2},
                            {"id":"c","label":"C","quantity":3},
                            {"id":"a","label":"A","quantity":1}
                            """)),
                "parallel_fixture").ContentHash;
        });
        Assert.All(results, hash => Assert.Equal(source.ContentHash, hash));
    }
}
