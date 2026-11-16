using System.Text;
using System.Text.Json;
using LegendsOfTheDungeon.StaticData.Abstractions;
using LegendsOfTheDungeon.StaticData.Core;

namespace LegendsOfTheDungeon.StaticData.Json;

/// <summary>严格 JSON 来源 P15.J，仅解析中立数据；领域自己拥有 Schema/定义。</summary>
public static class JsonStaticCatalogLoader
{
    /// <summary>解析受限 JSON 文档，严格核验元数据/字段并构建冻结 Hash Catalog。</summary>
    /// <param name="schema">受信任领域提供的通用 Schema。</param>
    /// <param name="expectedDataVersion">预期的正整数内容版本，禁止静默采用不匹配版本。</param>
    /// <param name="utf8Json">最多 4MiB 的 UTF-8 文档，不进行任意来源 I/O。</param>
    /// <param name="sourceName">固定来源标识，必须是 ASCII 小写标识。</param>
    /// <returns>完全校验后才发布的冻结快照。</returns>
    public static FrozenStaticCatalog Load(
        StaticCatalogSchema schema, int expectedDataVersion,
        ReadOnlyMemory<byte> utf8Json, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ValidateSource(sourceName);
        if (expectedDataVersion <= 0)
        {
            throw new StaticCatalogException(
                StaticCatalogFault.VersionMismatch, sourceName, 0, "data_version");
        }

        if (utf8Json.Length > StaticCatalogLimits.MaximumDocumentBytes)
        {
            throw new StaticCatalogException(
                StaticCatalogFault.LimitExceeded, sourceName, 0, "document");
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 12
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Fault(StaticCatalogFault.InvalidDocument, sourceName, 0, "document");
            }

            return ParseDocument(schema, expectedDataVersion, document.RootElement, sourceName);
        }
        catch (JsonException)
        {
            // 文本输入可含敏感内容；只返回稳定错误码及安全来源。
            throw Fault(StaticCatalogFault.InvalidDocument, sourceName, 0, "document");
        }
    }

    /// <summary>解析 SQL JSONB record payload；使用与 JSON 来源相同的字段规范化。</summary>
    /// <param name="schema">调用方明确指定的领域 Schema。</param>
    /// <param name="utf8Json">非空的一条 JSON 对象的 UTF-8。</param>
    /// <param name="sourceName">受检来源名称。</param>
    /// <param name="rowIndex">1-based 来源行号，用于定位字段错误。</param>
    /// <returns>中立数据行，交由通用 CatalogBuilder 冻结。</returns>
    public static StaticDataRow ParseRecord(
        StaticCatalogSchema schema, ReadOnlyMemory<byte> utf8Json,
        string sourceName, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ValidateSource(sourceName);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowIndex, 1);
        if (utf8Json.Length > StaticCatalogLimits.MaximumDocumentBytes)
        {
            throw Fault(StaticCatalogFault.LimitExceeded, sourceName, rowIndex, "record");
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                MaxDepth = 6,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            return ParseObject(schema, document.RootElement, sourceName, rowIndex);
        }
        catch (JsonException)
        {
            throw Fault(StaticCatalogFault.InvalidDocument, sourceName, rowIndex, "record");
        }
    }

    private static FrozenStaticCatalog ParseDocument(
        StaticCatalogSchema schema, int expectedDataVersion,
        JsonElement root, string sourceName)
    {
        string? catalogName = null;
        int? schemaVersion = null;
        int? dataVersion = null;
        JsonElement records = default;
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.Add(property.Name))
            {
                throw Fault(StaticCatalogFault.Duplicate, sourceName, 0, "document");
            }

            switch (property.Name)
            {
                case "catalog":
                    catalogName = RequireString(property.Value, sourceName, 0, "catalog");
                    break;
                case "schemaVersion":
                    schemaVersion = RequireInt32(property.Value, sourceName, "schema_version");
                    break;
                case "dataVersion":
                    dataVersion = RequireInt32(property.Value, sourceName, "data_version");
                    break;
                case "records":
                    records = property.Value;
                    break;
                default:
                    throw Fault(StaticCatalogFault.UnknownField, sourceName, 0, "document");
            }
        }

        if (catalogName != schema.CatalogName)
        {
            throw Fault(StaticCatalogFault.VersionMismatch, sourceName, 0, "catalog");
        }

        if (schemaVersion != schema.SchemaVersion)
        {
            throw Fault(StaticCatalogFault.VersionMismatch, sourceName, 0, "schema_version");
        }

        if (dataVersion != expectedDataVersion)
        {
            throw Fault(StaticCatalogFault.VersionMismatch, sourceName, 0, "data_version");
        }

        if (records.ValueKind != JsonValueKind.Array)
        {
            throw Fault(StaticCatalogFault.MissingField, sourceName, 0, "records");
        }

        var rows = new List<StaticDataRow>();
        foreach (var record in records.EnumerateArray())
        {
            var position = rows.Count + 1;
            if (position > StaticCatalogLimits.MaximumRecords)
            {
                throw Fault(StaticCatalogFault.LimitExceeded, sourceName, position, "records");
            }

            rows.Add(ParseObject(schema, record, sourceName, position));
        }

        return StaticCatalogBuilder.Build(schema, expectedDataVersion, rows, sourceName);
    }

    private static StaticDataRow ParseObject(
        StaticCatalogSchema schema, JsonElement record, string sourceName, int position)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            throw Fault(StaticCatalogFault.InvalidDocument, sourceName, position, "record");
        }

        var fieldSchemas = schema.Fields.ToDictionary(static x => x.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<KeyValuePair<string, StaticScalar>>();
        string? id = null;

        foreach (var property in record.EnumerateObject())
        {
            var key = property.Name;
            var safeKey = StaticIdentifier.IsValid(key) ? key : "field";
            if (!seen.Add(key))
            {
                throw Fault(StaticCatalogFault.Duplicate, sourceName, position, safeKey);
            }

            if (key == "id")
            {
                id = RequireString(property.Value, sourceName, position, "id");
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
                {
                    throw Fault(StaticCatalogFault.InvalidValue, sourceName, position, "id");
                }

                continue;
            }

            if (!fieldSchemas.TryGetValue(key, out var specification))
            {
                throw Fault(StaticCatalogFault.UnknownField, sourceName, position, safeKey);
            }

            var scalar = ParseScalar(property.Value, specification, sourceName, position);
            fields.Add(new KeyValuePair<string, StaticScalar>(key, scalar));
        }

        if (id is null)
        {
            throw Fault(StaticCatalogFault.MissingField, sourceName, position, "id");
        }

        foreach (var definition in schema.Fields)
        {
            if (definition.Required && !seen.Contains(definition.Name))
            {
                throw Fault(StaticCatalogFault.MissingField, sourceName, position, definition.Name);
            }
        }

        // 所有字段先经过类型/范围/重复校验；本处不可能持有外部可变引用。
        return new StaticDataRow(id, fields);
    }

    private static StaticScalar ParseScalar(
        JsonElement value, StaticFieldSchema field, string sourceName, int position)
    {
        switch (field.Type)
        {
            case StaticFieldType.Text when value.ValueKind == JsonValueKind.String:
            {
                var text = value.GetString()!;
                var scalar = StaticScalar.FromText(text);
                if (scalar.CanonicalText.Length > field.MaxLength)
                {
                    throw Fault(StaticCatalogFault.InvalidValue, sourceName, position, field.Name);
                }

                return scalar;
            }
            case StaticFieldType.WholeNumber when value.ValueKind == JsonValueKind.Number:
                if (value.TryGetInt64(out var number))
                {
                    return StaticScalar.FromInt64(number);
                }

                break;
            case StaticFieldType.FixedPoint when value.ValueKind == JsonValueKind.Number:
                if (value.TryGetDecimal(out var amount))
                {
                    return StaticScalar.FromDecimal(amount);
                }

                break;
            case StaticFieldType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return StaticScalar.FromBoolean(value.GetBoolean());
        }

        // 显式 JSON null 与未知/错误类型都会拒绝；不进行隐式字符串转换。
        throw Fault(StaticCatalogFault.InvalidValue, sourceName, position, field.Name);
    }

    private static string RequireString(
        JsonElement element, string sourceName, int rowIndex, string field)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Fault(StaticCatalogFault.InvalidValue, sourceName, rowIndex, field);
        }

        return element.GetString()!;
    }

    private static int RequireInt32(JsonElement element, string sourceName, string field)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Fault(StaticCatalogFault.InvalidValue, sourceName, 0, field);
        }

        return value;
    }

    private static StaticCatalogException Fault(
        StaticCatalogFault fault, string sourceName, int rowIndex, string fieldName) =>
        new(fault, sourceName, rowIndex, fieldName);

    private static void ValidateSource(string sourceName)
    {
        if (!StaticIdentifier.IsValid(sourceName))
        {
            throw new ArgumentException("静态数据来源名称无效。", nameof(sourceName));
        }
    }
}
