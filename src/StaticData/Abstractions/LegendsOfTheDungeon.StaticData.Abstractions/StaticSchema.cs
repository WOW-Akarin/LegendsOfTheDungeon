using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace LegendsOfTheDungeon.StaticData.Abstractions;

/// <summary>支持确定性跨 JSON/SQL 规范化的基础标量类型，不包含业务定义。</summary>
public enum StaticFieldType
{
    /// <summary>UTF-8 文本，Unicode NFC 规范化。</summary>
    Text,
    /// <summary>有符号 64 位整数。</summary>
    WholeNumber,
    /// <summary>高精度 Decimal，无平台浮点误差。</summary>
    FixedPoint,
    /// <summary>逻辑布尔值。</summary>
    Boolean
}

/// <summary>静态数据的固定输入预算，避免外部源无限消耗内存。</summary>
public static class StaticCatalogLimits
{
    /// <summary>任何 JSON 文档或 SQL 记录载荷流的累计最大字节数。</summary>
    public const int MaximumDocumentBytes = 4 * 1024 * 1024;
    /// <summary>Catalog 最大记录数。</summary>
    public const int MaximumRecords = 10_000;
    /// <summary>Schema 最多字段数（不含固有 id）。</summary>
    public const int MaximumFields = 64;
    /// <summary>最大单个文本字段 UTF-16 字符数。</summary>
    public const int MaximumTextLength = 4096;
}

/// <summary>只允许 ASCII 小写稳定字段/来源标识的公用验证。</summary>
public static class StaticIdentifier
{
    /// <summary>验证小写 ASCII 标识，禁止任意路径/SQL/敏感内容混入诊断。</summary>
    /// <param name="value">候选值。</param>
    /// <returns>是否为 1–64 字符的规范标识。</returns>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value[0] is not (>= 'a' and <= 'z'))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>通用字段契约由将来各领域提供，StaticData 基础库不包含具体玩法类型。</summary>
public sealed class StaticFieldSchema
{
    /// <summary>创建受检的字段定义；id 是隐含必填字段，不得再声明。</summary>
    /// <param name="name">受检字段名称。</param>
    /// <param name="type">确定的标量类型。</param>
    /// <param name="required">该字段是否必须出现在每行。</param>
    /// <param name="maxLength">文本最大 UTF-16 长度，1–4096；数字和布尔忽略此值。</param>
    public StaticFieldSchema(
        string name, StaticFieldType type, bool required, int maxLength = 1024)
    {
        if (!StaticIdentifier.IsValid(name) || name == "id")
        {
            throw new ArgumentException("通用字段标识非法或与 id 重名。", nameof(name));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLength, StaticCatalogLimits.MaximumTextLength);
        Name = name;
        Type = type;
        Required = required;
        MaxLength = maxLength;
    }

    /// <summary>稳定的小写字段名。</summary>
    public string Name { get; }
    /// <summary>唯一允许的标量类型。</summary>
    public StaticFieldType Type { get; }
    /// <summary>缺少该字段是否失败；显式 JSON null 总是拒绝。</summary>
    public bool Required { get; }
    /// <summary>文本字段最大长度。</summary>
    public int MaxLength { get; }
}

/// <summary>领域拥有的通用 Schema 描述，版本必须是正整数。</summary>
public sealed class StaticCatalogSchema
{
    private readonly IReadOnlyList<StaticFieldSchema> _fields;

    /// <summary>创建冻结的 Schema 字段列表；Schema Version 与数据版本分开。</summary>
    /// <param name="catalogName">受检逻辑 Catalog 名称。</param>
    /// <param name="schemaVersion">必须为正整数的结构版本。</param>
    /// <param name="fields">不包含 id 的字段集合，最多 64 项。</param>
    public StaticCatalogSchema(
        string catalogName, int schemaVersion, IEnumerable<StaticFieldSchema> fields)
    {
        if (!StaticIdentifier.IsValid(catalogName))
        {
            throw new ArgumentException("Catalog 名称无效。", nameof(catalogName));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentNullException.ThrowIfNull(fields);
        var values = new List<StaticFieldSchema>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (!seen.Add(field.Name))
            {
                throw new ArgumentException("Schema 字段名不能重复。", nameof(fields));
            }

            if (values.Count >= StaticCatalogLimits.MaximumFields)
            {
                throw new ArgumentException("Schema 字段数超过硬上限。", nameof(fields));
            }

            values.Add(field);
        }

        CatalogName = catalogName;
        SchemaVersion = schemaVersion;
        _fields = Array.AsReadOnly(values.OrderBy(static x => x.Name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>逻辑 Catalog 名称。</summary>
    public string CatalogName { get; }
    /// <summary>Schema Version。</summary>
    public int SchemaVersion { get; }
    /// <summary>按字段名序排列的不可修改 Schema 快照。</summary>
    public IReadOnlyList<StaticFieldSchema> Fields => _fields;
}

/// <summary>来源/数据错误的稳定类别，不回显原始 SQL/JSON 内容。</summary>
public enum StaticCatalogFault
{
    /// <summary>输入格式或 JSON 语法错误。</summary>
    InvalidDocument,
    /// <summary>Catalog 名称、Schema Version 或 Data Version 不吻合。</summary>
    VersionMismatch,
    /// <summary>未知字段。</summary>
    UnknownField,
    /// <summary>重复字段名或重复记录 ID。</summary>
    Duplicate,
    /// <summary>缺少必填字段。</summary>
    MissingField,
    /// <summary>字段标量类型或允许范围不合法。</summary>
    InvalidValue,
    /// <summary>输入记录数或总字节数过多。</summary>
    LimitExceeded,
    /// <summary>SQL 来源或外部数据读取失败。</summary>
    SourceUnavailable
}

/// <summary>可追踪来源、行序号、字段名的安全错误；不包含原始值/数据库异常。</summary>
public sealed class StaticCatalogException : Exception
{
    /// <summary>创建稳定错误而不附带外部源的异常正文。</summary>
    /// <param name="fault">错误类型。</param>
    /// <param name="sourceName">安全固定来源标识。</param>
    /// <param name="rowIndex">1-based 行序号；0 表示文档级问题。</param>
    /// <param name="fieldName">受检字段名或元数据名。</param>
    public StaticCatalogException(
        StaticCatalogFault fault, string sourceName, int rowIndex, string fieldName)
        : base($"static_data.{fault}")
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault));
        }

        if (!StaticIdentifier.IsValid(sourceName))
        {
            throw new ArgumentException("来源标识无效。", nameof(sourceName));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

        if (!StaticIdentifier.IsValid(fieldName))
        {
            throw new ArgumentException("错误字段标识无效。", nameof(fieldName));
        }

        Fault = fault;
        SourceName = sourceName;
        RowIndex = rowIndex;
        FieldName = fieldName;
    }

    /// <summary>可枚举错误种类。</summary>
    public StaticCatalogFault Fault { get; }
    /// <summary>由调用者配置的安全来源名。</summary>
    public string SourceName { get; }
    /// <summary>1-based 记录序号，0 表示文档本身。</summary>
    public int RowIndex { get; }
    /// <summary>字段名称（不包含原始值）。</summary>
    public string FieldName { get; }
}

/// <summary>跨 JSON/SQL 严格规范的标量值，内部只保存不依赖区域设置的文本表示。</summary>
public sealed class StaticScalar
{
    private StaticScalar(StaticFieldType type, string canonicalText)
    {
        Type = type;
        CanonicalText = canonicalText;
    }

    /// <summary>标量类型。</summary>
    public StaticFieldType Type { get; }
    /// <summary>跨平台固定的文本形式，可由领域按 Schema 类型解释。</summary>
    public string CanonicalText { get; }

    /// <summary>文本仅执行 Unicode NFC 规范化，不折叠大小写和空白。</summary>
    public static StaticScalar FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new StaticScalar(StaticFieldType.Text, value.Normalize(NormalizationForm.FormC));
    }

    /// <summary>整数使用文化无关的十进制文本。</summary>
    public static StaticScalar FromInt64(long value) =>
        new(StaticFieldType.WholeNumber, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Decimal 统一去除无意义的尾部零，避免 JSON 1.00 与 SQL 1.0 Hash 不一致。</summary>
    public static StaticScalar FromDecimal(decimal value) =>
        new(StaticFieldType.FixedPoint, value.ToString("G29", CultureInfo.InvariantCulture));

    /// <summary>布尔值固定为小写 true/false。</summary>
    public static StaticScalar FromBoolean(bool value) =>
        new(StaticFieldType.Boolean, value ? "true" : "false");
}

/// <summary>Adapter 生成的中立行；构造时防御性复制所有字段，不持有 JSON 文档引用。</summary>
public sealed class StaticDataRow
{
    private readonly IReadOnlyDictionary<string, StaticScalar> _values;

    /// <summary>创建中立行；重复字段被拒绝。</summary>
    /// <param name="id">非空、最多 64 UTF-16 字符的记录 ID。</param>
    /// <param name="values">已经类型化的通用字段值。</param>
    public StaticDataRow(string id, IEnumerable<KeyValuePair<string, StaticScalar>> values)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(values);
        var normalizedId = id.Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(normalizedId) || normalizedId.Length > 64)
        {
            throw new ArgumentException("记录 ID 超出允许范围。", nameof(id));
        }

        var fields = new Dictionary<string, StaticScalar>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (!StaticIdentifier.IsValid(pair.Key) || pair.Key == "id")
            {
                throw new ArgumentException("字段名非法。", nameof(values));
            }

            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!fields.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException("重复字段。", nameof(values));
            }
        }

        Id = normalizedId;
        _values = new ReadOnlyDictionary<string, StaticScalar>(fields);
    }

    /// <summary>Unicode NFC 的稳定记录 ID。</summary>
    public string Id { get; }
    /// <summary>防御性复制且只读的字段字典。</summary>
    public IReadOnlyDictionary<string, StaticScalar> Fields => _values;
}
