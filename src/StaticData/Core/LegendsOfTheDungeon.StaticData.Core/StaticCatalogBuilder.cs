using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using LegendsOfTheDungeon.StaticData.Abstractions;

namespace LegendsOfTheDungeon.StaticData.Core;

/// <summary>构建后完全冻结的静态数据快照；索引不提供写路径。</summary>
public sealed class FrozenStaticCatalog
{
    private readonly FrozenDictionary<string, StaticDataRow> _index;
    private readonly IReadOnlyList<StaticDataRow> _rows;

    internal FrozenStaticCatalog(
        StaticCatalogSchema schema, int dataVersion,
        string contentHash, StaticDataRow[] rows)
    {
        Schema = schema;
        DataVersion = dataVersion;
        ContentHash = contentHash;
        _rows = Array.AsReadOnly(rows);
        _index = rows.ToFrozenDictionary(static x => x.Id, StringComparer.Ordinal);
    }

    /// <summary>构建此目录时的结构定义与 SchemaVersion。</summary>
    public StaticCatalogSchema Schema { get; }
    /// <summary>显式内容版本，不由文件时间或数据库行顺序推断。</summary>
    public int DataVersion { get; }
    /// <summary>同结构、同版本、同规范化行的 SHA-256 小写 hex。</summary>
    public string ContentHash { get; }
    /// <summary>始终按记录 ID Ordinal 排序的只读集合。</summary>
    public IReadOnlyList<StaticDataRow> Records => _rows;

    /// <summary>按 NFC 规范化 ID 做 O(1) 冻结查找；返回对象不可变。</summary>
    /// <param name="id">记录 ID。</param>
    /// <returns>存在则返回冻结行，否则为 null。</returns>
    public StaticDataRow? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _index.GetValueOrDefault(id.Normalize(NormalizationForm.FormC));
    }
}

/// <summary>JSON 和 SQL Source 共同使用的校验、排序、冻结和内容 Hash 唯一路径。</summary>
public static class StaticCatalogBuilder
{
    /// <summary>校验中立行，保持失败原子性，生成冻结目录与稳定 Hash。</summary>
    /// <param name="schema">由领域拥有的通用 Schema。</param>
    /// <param name="dataVersion">正整数数据版本。</param>
    /// <param name="rows">有界源记录。</param>
    /// <param name="sourceName">受检来源标识。</param>
    /// <returns>排序且无可变写入口的 Catalog。</returns>
    public static FrozenStaticCatalog Build(
        StaticCatalogSchema schema, int dataVersion,
        IEnumerable<StaticDataRow> rows, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);
        if (!StaticIdentifier.IsValid(sourceName))
        {
            throw new ArgumentException("来源名称非法。", nameof(sourceName));
        }

        if (dataVersion <= 0)
        {
            throw new StaticCatalogException(
                StaticCatalogFault.VersionMismatch, sourceName, 0, "data_version");
        }

        var definitions = schema.Fields.ToDictionary(static x => x.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<StaticDataRow>();
        foreach (var record in rows)
        {
            var index = items.Count + 1;
            if (index > StaticCatalogLimits.MaximumRecords)
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.LimitExceeded, sourceName, index, "records");
            }

            if (record is null)
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.InvalidDocument, sourceName, index, "records");
            }

            if (!seen.Add(record.Id))
            {
                throw new StaticCatalogException(
                    StaticCatalogFault.Duplicate, sourceName, index, "id");
            }

            foreach (var pair in record.Fields)
            {
                if (!definitions.TryGetValue(pair.Key, out var field))
                {
                    throw new StaticCatalogException(
                        StaticCatalogFault.UnknownField, sourceName, index, pair.Key);
                }

                if (pair.Value.Type != field.Type ||
                    field.Type == StaticFieldType.Text &&
                    pair.Value.CanonicalText.Length > field.MaxLength)
                {
                    throw new StaticCatalogException(
                        StaticCatalogFault.InvalidValue, sourceName, index, pair.Key);
                }
            }

            foreach (var field in schema.Fields)
            {
                if (field.Required && !record.Fields.ContainsKey(field.Name))
                {
                    throw new StaticCatalogException(
                        StaticCatalogFault.MissingField, sourceName, index, field.Name);
                }
            }

            items.Add(record);
        }

        var sorted = items.OrderBy(static x => x.Id, StringComparer.Ordinal).ToArray();
        var hash = ComputeHash(schema, dataVersion, sorted);
        return new FrozenStaticCatalog(schema, dataVersion, hash, sorted);
    }

    private static string ComputeHash(StaticCatalogSchema schema, int dataVersion,
        IReadOnlyList<StaticDataRow> rows)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hasher, "lotd.static_catalog.v1");
        AppendText(hasher, schema.CatalogName);
        AppendInt32(hasher, schema.SchemaVersion);
        AppendInt32(hasher, dataVersion);
        AppendInt32(hasher, schema.Fields.Count);
        foreach (var field in schema.Fields)
        {
            AppendText(hasher, field.Name);
            AppendInt32(hasher, (int)field.Type);
            AppendInt32(hasher, field.Required ? 1 : 0);
            AppendInt32(hasher, field.MaxLength);
        }

        AppendInt32(hasher, rows.Count);
        foreach (var row in rows)
        {
            AppendText(hasher, row.Id);
            foreach (var field in schema.Fields)
            {
                if (row.Fields.TryGetValue(field.Name, out var scalar))
                {
                    AppendInt32(hasher, 1);
                    AppendText(hasher, scalar.CanonicalText);
                }
                else
                {
                    AppendInt32(hasher, 0);
                }
            }
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private static void AppendInt32(IncrementalHash hasher, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hasher.AppendData(bytes);
    }

    private static void AppendText(IncrementalHash hasher, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hasher, bytes.Length);
        hasher.AppendData(bytes);
    }
}
