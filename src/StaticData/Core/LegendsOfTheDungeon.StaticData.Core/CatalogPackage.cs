using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using LegendsOfTheDungeon.StaticData.Abstractions;

namespace LegendsOfTheDungeon.StaticData.Core;

/// <summary>P16 整包校验的安全错误种类；不包含游戏业务定义。</summary>
public enum CatalogPackageFault
{
    /// <summary>整包中同名 Catalog 重复。</summary>
    DuplicateCatalog,
    /// <summary>Schema 关系指向缺失的 Catalog。</summary>
    MissingCatalog,
    /// <summary>版本不是单调、有效或整包一致。</summary>
    VersionMismatch,
    /// <summary>引用规则重复、字段缺失或字段不是文本。</summary>
    InvalidReferenceRule,
    /// <summary>来源行引用的目标记录不存在。</summary>
    MissingReference,
    /// <summary>跨 Catalog 或同 Catalog 引用出现环。</summary>
    ReferenceCycle,
    /// <summary>包中目录、规则或引用边超出有限预算。</summary>
    LimitExceeded
}

/// <summary>稳定的整包验证错误，避免携带实体 ID、SQL 参数、原始 JSON 或隐私数据。</summary>
public sealed class CatalogPackageException : Exception
{
    /// <summary>创建安全的错误；Catalog 与字段名称是已校验的技术标识。</summary>
    /// <param name="fault">预定义故障类型。</param>
    /// <param name="catalog">已校验的逻辑目录名称。</param>
    /// <param name="rowIndex">1-based 行号；0 为整包/目录级错误。</param>
    /// <param name="field">合法的技术字段名称。</param>
    public CatalogPackageException(
        CatalogPackageFault fault, string catalog, int rowIndex, string field)
        : base($"catalog_package.{fault}")
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault));
        }

        if (!StaticIdentifier.IsValid(catalog))
        {
            throw new ArgumentException("目录错误名称不合法。", nameof(catalog));
        }

        if (!StaticIdentifier.IsValid(field))
        {
            throw new ArgumentException("字段错误名称不合法。", nameof(field));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        Fault = fault;
        Catalog = catalog;
        RowIndex = rowIndex;
        Field = field;
    }

    /// <summary>有限故障枚举。</summary>
    public CatalogPackageFault Fault { get; }
    /// <summary>安全的目录名称。</summary>
    public string Catalog { get; }
    /// <summary>1-based 目录内行号；0 为目录级错误。</summary>
    public int RowIndex { get; }
    /// <summary>安全的字段名称。</summary>
    public string Field { get; }
}

/// <summary>由未来领域声明的通用跨目录引用规则，引用字段必须为 Text 标量。</summary>
public sealed class CatalogReferenceRule
{
    /// <summary>声明源目录/字段与目标目录的强引用；值是目标行 ID。</summary>
    /// <param name="sourceCatalog">源 Catalog 名称。</param>
    /// <param name="sourceField">源字段名。</param>
    /// <param name="targetCatalog">目标 Catalog 名称。</param>
    public CatalogReferenceRule(
        string sourceCatalog, string sourceField, string targetCatalog)
    {
        if (!StaticIdentifier.IsValid(sourceCatalog))
        {
            throw new ArgumentException("源目录名称非法。", nameof(sourceCatalog));
        }

        if (!StaticIdentifier.IsValid(sourceField))
        {
            throw new ArgumentException("引用字段名称非法。", nameof(sourceField));
        }

        if (!StaticIdentifier.IsValid(targetCatalog))
        {
            throw new ArgumentException("目标目录名称非法。", nameof(targetCatalog));
        }

        SourceCatalog = sourceCatalog;
        SourceField = sourceField;
        TargetCatalog = targetCatalog;
    }

    /// <summary>源目录。</summary>
    public string SourceCatalog { get; }
    /// <summary>源目录引用字段。</summary>
    public string SourceField { get; }
    /// <summary>目标目录。</summary>
    public string TargetCatalog { get; }
}

/// <summary>跨目录整包冻结快照，包含确定性内容 Hash 和不可写索引。</summary>
public sealed class CatalogPackageSnapshot
{
    private readonly FrozenDictionary<string, FrozenStaticCatalog> _index;
    private readonly IReadOnlyList<FrozenStaticCatalog> _catalogs;

    internal CatalogPackageSnapshot(int version, string hash, FrozenStaticCatalog[] catalogs)
    {
        Version = version;
        ContentHash = hash;
        _catalogs = Array.AsReadOnly(catalogs);
        _index = catalogs.ToFrozenDictionary(static c => c.Schema.CatalogName, StringComparer.Ordinal);
    }

    /// <summary>单一整包版本，所有 Catalog 的 DataVersion 必须相等。</summary>
    public int Version { get; }
    /// <summary>独立于输入顺序的整包 SHA-256，包含每目录 Hash 和引用规则。</summary>
    public string ContentHash { get; }
    /// <summary>按目录名 Ordinal 排序的有限只读目录快照。</summary>
    public IReadOnlyList<FrozenStaticCatalog> Catalogs => _catalogs;

    /// <summary>按确定性目录名获取冻结 Catalog，不返回可写对象。</summary>
    /// <param name="catalog">逻辑目录名称。</param>
    /// <returns>存在则返回完整的冻结目录，缺失时 null。</returns>
    public FrozenStaticCatalog? Find(string catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return _index.GetValueOrDefault(catalog);
    }
}

/// <summary>跨目录 P16.F 校验与冻结；包含数量预算和无递归循环检测。</summary>
public static class CatalogPackageBuilder
{
    /// <summary>最多 16 个跨域 Catalog；领域应选择有意义的分包。</summary>
    public const int MaximumCatalogs = 16;
    /// <summary>最多 64 条预声明引用规则。</summary>
    public const int MaximumReferenceRules = 64;
    /// <summary>最多 131072 条真实引用边；拒绝超额再构建。</summary>
    public const int MaximumReferenceEdges = 131072;

    /// <summary>验证整个跨目录版本包，所有错误路径都不会发布半成品。</summary>
    /// <param name="version">必须为正数且与每个 Catalog 的 DataVersion 一致。</param>
    /// <param name="catalogs">P15 已冻结的每域目录集合。</param>
    /// <param name="referenceRules">由可信领域提供的固定引用规则。</param>
    /// <returns>全部校验通过才返回的不可变整包与规范 Hash。</returns>
    public static CatalogPackageSnapshot Build(
        int version, IEnumerable<FrozenStaticCatalog> catalogs,
        IEnumerable<CatalogReferenceRule> referenceRules)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(referenceRules);
        if (version <= 0)
        {
            throw Error(CatalogPackageFault.VersionMismatch, "package", 0, "version");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<FrozenStaticCatalog>();
        foreach (var catalog in catalogs)
        {
            if (list.Count >= MaximumCatalogs)
            {
                throw Error(CatalogPackageFault.LimitExceeded, "package", 0, "catalogs");
            }

            ArgumentNullException.ThrowIfNull(catalog);
            var name = catalog.Schema.CatalogName;
            if (!seen.Add(name))
            {
                throw Error(CatalogPackageFault.DuplicateCatalog, name, 0, "catalog");
            }

            if (catalog.DataVersion != version)
            {
                throw Error(CatalogPackageFault.VersionMismatch, name, 0, "version");
            }

            list.Add(catalog);
        }

        if (list.Count == 0)
        {
            throw Error(CatalogPackageFault.MissingCatalog, "package", 0, "catalogs");
        }

        var sorted = list.OrderBy(static c => c.Schema.CatalogName, StringComparer.Ordinal).ToArray();
        var index = sorted.ToDictionary(static c => c.Schema.CatalogName, StringComparer.Ordinal);
        var rules = new List<CatalogReferenceRule>();
        var ruleKeys = new HashSet<(string, string, string)>();
        foreach (var rule in referenceRules)
        {
            if (rules.Count >= MaximumReferenceRules)
            {
                throw Error(CatalogPackageFault.LimitExceeded, "package", 0, "rules");
            }

            ArgumentNullException.ThrowIfNull(rule);
            if (!ruleKeys.Add((rule.SourceCatalog, rule.SourceField, rule.TargetCatalog)))
            {
                throw Error(CatalogPackageFault.InvalidReferenceRule,
                    rule.SourceCatalog, 0, rule.SourceField);
            }

            if (!index.TryGetValue(rule.SourceCatalog, out var source))
            {
                throw Error(CatalogPackageFault.MissingCatalog,
                    rule.SourceCatalog, 0, "catalog");
            }

            if (!index.ContainsKey(rule.TargetCatalog))
            {
                throw Error(CatalogPackageFault.MissingCatalog,
                    rule.TargetCatalog, 0, "catalog");
            }

            var field = source.Schema.Fields.FirstOrDefault(
                f => f.Name == rule.SourceField);
            if (field is null || field.Type != StaticFieldType.Text)
            {
                throw Error(CatalogPackageFault.InvalidReferenceRule,
                    rule.SourceCatalog, 0, rule.SourceField);
            }

            rules.Add(rule);
        }

        var orderedRules = rules
            .OrderBy(static r => r.SourceCatalog, StringComparer.Ordinal)
            .ThenBy(static r => r.SourceField, StringComparer.Ordinal)
            .ThenBy(static r => r.TargetCatalog, StringComparer.Ordinal)
            .ToArray();
        ValidateReferences(sorted, index, orderedRules);
        return new CatalogPackageSnapshot(version, Hash(version, sorted, orderedRules), sorted);
    }

    private static void ValidateReferences(
        FrozenStaticCatalog[] catalogs,
        Dictionary<string, FrozenStaticCatalog> index,
        CatalogReferenceRule[] rules)
    {
        var nodes = new Dictionary<(string Catalog, string Id), int>();
        var nodeCatalogs = new List<string>();
        var nodeRows = new List<int>();
        foreach (var catalog in catalogs)
        {
            for (var rowIndex = 0; rowIndex < catalog.Records.Count; rowIndex++)
            {
                nodes.Add((catalog.Schema.CatalogName, catalog.Records[rowIndex].Id), nodes.Count);
                nodeCatalogs.Add(catalog.Schema.CatalogName);
                nodeRows.Add(rowIndex + 1);
            }
        }

        var adjacency = new List<int>[nodes.Count];
        for (var n = 0; n < adjacency.Length; n++)
        {
            adjacency[n] = [];
        }

        var edges = 0;
        foreach (var rule in rules)
        {
            var source = index[rule.SourceCatalog];
            foreach (var row in source.Records)
            {
                if (!row.Fields.TryGetValue(rule.SourceField, out var referenced))
                {
                    continue;
                }

                var position = nodes[(rule.SourceCatalog, row.Id)];
                if (!nodes.TryGetValue((rule.TargetCatalog, referenced.CanonicalText), out var target))
                {
                    throw Error(CatalogPackageFault.MissingReference,
                        rule.SourceCatalog, nodeRows[position], rule.SourceField);
                }

                if (edges >= MaximumReferenceEdges)
                {
                    throw Error(CatalogPackageFault.LimitExceeded,
                        rule.SourceCatalog, 0, "references");
                }

                adjacency[position].Add(target);
                edges++;
            }
        }

        // 迭代 DFS 不随 10k 链深消耗系统调用栈；所有路径按固定目录/行/rule 顺序。
        var state = new byte[adjacency.Length];
        for (var start = 0; start < state.Length; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            state[start] = 1;
            var stack = new Stack<(int Node, int Cursor)>();
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (node, cursor) = stack.Pop();
                if (cursor == adjacency[node].Count)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, cursor + 1));
                var next = adjacency[node][cursor];
                if (state[next] == 1)
                {
                    throw Error(CatalogPackageFault.ReferenceCycle,
                        nodeCatalogs[node], nodeRows[node], "references");
                }

                if (state[next] == 0)
                {
                    state[next] = 1;
                    stack.Push((next, 0));
                }
            }
        }
    }

    private static string Hash(
        int version, FrozenStaticCatalog[] catalogs, CatalogReferenceRule[] rules)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Write(hash, "lotd.catalog_package.v1");
        WriteInt(hash, version);
        WriteInt(hash, catalogs.Length);
        foreach (var catalog in catalogs)
        {
            Write(hash, catalog.Schema.CatalogName);
            Write(hash, catalog.ContentHash);
        }

        WriteInt(hash, rules.Length);
        foreach (var rule in rules)
        {
            Write(hash, rule.SourceCatalog);
            Write(hash, rule.SourceField);
            Write(hash, rule.TargetCatalog);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Write(IncrementalHash hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteInt(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void WriteInt(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static CatalogPackageException Error(
        CatalogPackageFault fault, string catalog, int row, string field) =>
        new(fault, catalog, row, field);
}
