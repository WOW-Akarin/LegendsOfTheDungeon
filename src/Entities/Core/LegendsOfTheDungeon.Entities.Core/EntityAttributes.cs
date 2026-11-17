using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Entities.Core;

/// <summary>资源与属性使用的稳定 ASCII 小写键，避免业务名字和实体 ID 混用。</summary>
public readonly record struct AttributeKey
{
    /// <summary>构造 1–64 字符的规范化键：a-z、0-9、点、下划线、连字符。</summary>
    /// <param name="value">业务定义的不可变键。</param>
    public AttributeKey(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value.Any(c => !((c >= 'a' && c <= 'z') ||
                             (c >= '0' && c <= '9') ||
                             c is '.' or '_' or '-')))
        {
            throw new ArgumentException("Attribute key must be lowercase ASCII (1–64 characters).", nameof(value));
        }

        Value = value;
    }

    /// <summary>规范化的键；default 的 Value 为空，不能用于定义。</summary>
    public string Value { get; }

    /// <summary>是否未初始化。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);
}

/// <summary>相互独立的临时修饰业务来源；与撤销句柄不同。</summary>
public readonly record struct AttributeModifierSource
{
    /// <summary>构造非零来源 ID。</summary>
    /// <param name="value">同实体内稳定的业务来源编号。</param>
    public AttributeModifierSource(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    /// <summary>来源编号。</summary>
    public ulong Value { get; }
}

/// <summary>带完整实体代际及不可复用递增编号的修饰撤销凭据。</summary>
/// <param name="Identity">归属实体的 World/Epoch/Id/Generation。</param>
/// <param name="Sequence">从 1 开始的句柄编号。</param>
public readonly record struct AttributeModifierHandle(EntityIdentity Identity, ulong Sequence);

/// <summary>Flat 为绝对数值，Percent 为百分比（100 表示翻倍）。</summary>
public enum AttributeModifierOperation
{
    /// <summary>直接增加或减少数值。</summary>
    Flat,
    /// <summary>将当前结果乘以 1 + Amount / 100。</summary>
    Percent
}

/// <summary>资源设置或增减的明确结果，拒绝时不修改当前值。</summary>
public enum ResourceChangeResult
{
    /// <summary>资源实际改变。</summary>
    Changed,
    /// <summary>数值与原有相同。</summary>
    Unchanged,
    /// <summary>该资源尚未定义。</summary>
    NotFound,
    /// <summary>结果超出零到当前最大值。</summary>
    OutOfRange,
    /// <summary>实体已经死亡，禁止消费或回复资源。</summary>
    InvalidState
}

/// <summary>属性的已重算只读值，区分基础定义和一层派生定义。</summary>
/// <param name="Key">属性键。</param>
/// <param name="BaseValue">未附加当前属性修饰的输入值。</param>
/// <param name="EffectiveValue">所有修饰完成后的最终值。</param>
/// <param name="IsDerived">该属性是否来自另一基础属性的有效值。</param>
public readonly record struct AttributeStatSnapshot(
    AttributeKey Key, decimal BaseValue, decimal EffectiveValue, bool IsDerived);

/// <summary>受控的一层派生定义；Source 必须是基础属性，禁止隐含递归依赖。</summary>
/// <param name="Key">目标派生属性。</param>
/// <param name="Source">作为来源的基础属性。</param>
/// <param name="Factor">基础有效值的倍数。</param>
/// <param name="Offset">固定非负加数。</param>
public readonly record struct DerivedAttributeDefinition(
    AttributeKey Key, AttributeKey Source, decimal Factor, decimal Offset);

/// <summary>唯一序号与业务来源不同；用于精准撤销和稳定重放。</summary>
/// <param name="Handle">精确撤销凭据。</param>
/// <param name="Source">可关联多个独立修饰的业务来源。</param>
/// <param name="Key">受影响的属性。</param>
/// <param name="Operation">固定运算种类。</param>
/// <param name="Amount">绝对数值或百分比。</param>
/// <param name="Priority">先按优先级升序，再按句柄序号升序执行。</param>
public readonly record struct AttributeModifierSnapshot(
    AttributeModifierHandle Handle, AttributeModifierSource Source, AttributeKey Key,
    AttributeModifierOperation Operation, decimal Amount, int Priority);

/// <summary>资源的绝对当前值和绑定属性上限，最大值下降时不按比例回复。</summary>
/// <param name="Key">资源键。</param>
/// <param name="MaximumStat">提供最大资源量的属性键。</param>
/// <param name="Current">当前资源值。</param>
/// <param name="Maximum">当前最大资源量。</param>
public readonly record struct AttributeResourceSnapshot(
    AttributeKey Key, AttributeKey MaximumStat, decimal Current, decimal Maximum);

/// <summary>稳定排序、不可原地修改的属性/资源/来源账本快照。</summary>
public sealed class EntityAttributeSnapshot : IEquatable<EntityAttributeSnapshot>
{
    internal EntityAttributeSnapshot(
        IReadOnlyList<AttributeStatSnapshot> stats,
        IReadOnlyList<DerivedAttributeDefinition> derived,
        IReadOnlyList<AttributeModifierSnapshot> modifiers,
        IReadOnlyList<AttributeResourceSnapshot> resources, ulong lastModifierSequence)
    {
        Stats = stats;
        Derived = derived;
        Modifiers = modifiers;
        Resources = resources;
        LastModifierSequence = lastModifierSequence;
    }

    /// <summary>键升序的属性有效值。</summary>
    public IReadOnlyList<AttributeStatSnapshot> Stats { get; }
    /// <summary>键升序的不可变派生公式。</summary>
    public IReadOnlyList<DerivedAttributeDefinition> Derived { get; }
    /// <summary>序号升序的活动 Modifier 来源。</summary>
    public IReadOnlyList<AttributeModifierSnapshot> Modifiers { get; }
    /// <summary>键升序的资源状态。</summary>
    public IReadOnlyList<AttributeResourceSnapshot> Resources { get; }
    /// <summary>最后发出的 Modifier 序号；撤销后也不能回退。</summary>
    public ulong LastModifierSequence { get; }

    /// <summary>以所有值和来源账本进行结构比较，而非比较冻结数组引用。</summary>
    /// <param name="other">另一份领域状态快照。</param>
    /// <returns>状态与历史序号均相同则相等。</returns>
    public bool Equals(EntityAttributeSnapshot? other) =>
        other is not null &&
        LastModifierSequence == other.LastModifierSequence &&
        Stats.SequenceEqual(other.Stats) &&
        Derived.SequenceEqual(other.Derived) &&
        Modifiers.SequenceEqual(other.Modifiers) &&
        Resources.SequenceEqual(other.Resources);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EntityAttributeSnapshot);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LastModifierSequence);
        foreach (var value in Stats) hash.Add(value);
        foreach (var value in Derived) hash.Add(value);
        foreach (var value in Modifiers) hash.Add(value);
        foreach (var value in Resources) hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>
/// P25 Entity.Stats/Resources 的唯一可写账本；先构建候选并完成重算，再发布新状态。
/// 全部写入要求实体原四元组对应 P18 同步世界租约。无墙钟依赖。
/// </summary>
public sealed class EntityAttributeCapability
{
    private const int MaxKeys = 64;
    private const int MaxModifiers = 256;
    private const decimal MaxValue = 1_000_000_000m;
    private readonly EntityIdentity _identity;
    private readonly LifeCapability _life;
    private readonly object _gate = new();
    private Dictionary<AttributeKey, decimal> _bases = [];
    private Dictionary<AttributeKey, DerivedAttributeDefinition> _derived = [];
    private Dictionary<ulong, AttributeModifierSnapshot> _modifiers = [];
    private Dictionary<AttributeKey, AttributeStatSnapshot> _effective = [];
    private Dictionary<AttributeKey, AttributeResourceSnapshot> _resources = [];
    private ulong _sequence;

    internal EntityAttributeCapability(EntityIdentity identity, LifeCapability life)
    {
        _identity = identity;
        _life = life;
    }

    /// <summary>返回包含固定来源、定义与资源值的防御性快照。</summary>
    public EntityAttributeSnapshot Capture()
    {
        lock (_gate)
        {
            return new EntityAttributeSnapshot(
                Array.AsReadOnly(_effective.Values.OrderBy(v => v.Key.Value, StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(_derived.Values.OrderBy(v => v.Key.Value, StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(_modifiers.Values.OrderBy(v => v.Handle.Sequence).ToArray()),
                Array.AsReadOnly(_resources.Values.OrderBy(v => v.Key.Value, StringComparer.Ordinal).ToArray()),
                _sequence);
        }
    }

    /// <summary>设置或新增基础属性；数值 0–10 亿，最多 64 个属性。</summary>
    /// <param name="context">有效的同步世界租约。</param>
    /// <param name="key">基础属性键。</param>
    /// <param name="value">非负原始数值；按两位小数四舍五入（AwayFromZero）。</param>
    /// <returns>是否实际改变。</returns>
    public bool SetBase(IWorldWriteContext context, AttributeKey key, decimal value)
    {
        _identity.Demand(context);
        ValidateKey(key);
        ValidateNonNegative(value, nameof(value));
        var rounded = Round(value);
        lock (_gate)
        {
            if (_derived.ContainsKey(key)) throw new InvalidOperationException("Derived stat cannot be a base stat.");
            if (_bases.TryGetValue(key, out var previous) && previous == rounded) return false;
            if (!_bases.ContainsKey(key) && _bases.Count + _derived.Count >= MaxKeys)
                throw new InvalidOperationException("Stat limit exceeded.");

            var candidate = new Dictionary<AttributeKey, decimal>(_bases) { [key] = rounded };
            var computed = Recalculate(candidate, _derived, _modifiers);
            var resources = RecalculateResources(computed, _resources);
            _bases = candidate;
            _effective = computed;
            _resources = resources;
            return true;
        }
    }

    /// <summary>定义单层派生属性：Round(基础有效值 × factor + offset)，不可替换既有公式。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="key">新的派生属性键。</param>
    /// <param name="source">已存在的基础属性键。</param>
    /// <param name="factor">非负倍数，最大 100。</param>
    /// <param name="offset">非负固定加数，最大 10 亿。</param>
    /// <returns>新建时为 true；重复相同定义为 false。</returns>
    public bool DefineDerived(IWorldWriteContext context, AttributeKey key, AttributeKey source,
        decimal factor, decimal offset)
    {
        _identity.Demand(context);
        ValidateKey(key);
        ValidateKey(source);
        if (factor is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(factor));
        ValidateNonNegative(offset, nameof(offset));
        var definition = new DerivedAttributeDefinition(key, source, factor, Round(offset));
        lock (_gate)
        {
            if (!_bases.ContainsKey(source) || _bases.ContainsKey(key) || source == key)
                throw new InvalidOperationException("Derived stats can reference existing base stats only.");
            if (_derived.TryGetValue(key, out var old))
            {
                if (old == definition) return false;
                throw new InvalidOperationException("Conflicting derived stat definition.");
            }

            if (_bases.Count + _derived.Count >= MaxKeys)
                throw new InvalidOperationException("Stat limit exceeded.");
            var candidate = new Dictionary<AttributeKey, DerivedAttributeDefinition>(_derived) { [key] = definition };
            var computed = Recalculate(_bases, candidate, _modifiers);
            var resources = RecalculateResources(computed, _resources);
            _derived = candidate;
            _effective = computed;
            _resources = resources;
            return true;
        }
    }

    /// <summary>为来源添加修饰；精确的句柄在异常或计算失败时不被消耗。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="source">非零业务来源。</param>
    /// <param name="key">已有属性。</param>
    /// <param name="operation">Flat 或 Percent。</param>
    /// <param name="amount">Flat ±10 亿；Percent -100 至 +1000。</param>
    /// <param name="priority">-100 至 100；低优先级先执行，平局按句柄顺序。</param>
    /// <returns>绑定当前实体代际的唯一撤销凭据。</returns>
    public AttributeModifierHandle AddModifier(IWorldWriteContext context, AttributeModifierSource source,
        AttributeKey key, AttributeModifierOperation operation, decimal amount, int priority = 0)
    {
        _identity.Demand(context);
        ValidateKey(key);
        if (source.Value == 0 || priority is < -100 or > 100 || !Enum.IsDefined(operation))
            throw new ArgumentException("Invalid modifier source, operation or priority.");
        if (operation == AttributeModifierOperation.Flat && (amount < -MaxValue || amount > MaxValue) ||
            operation == AttributeModifierOperation.Percent && (amount < -100 || amount > 1000))
            throw new ArgumentOutOfRangeException(nameof(amount));

        lock (_gate)
        {
            if (!_effective.ContainsKey(key)) throw new KeyNotFoundException("Stat key is undefined.");
            if (_modifiers.Count >= MaxModifiers) throw new InvalidOperationException("Modifier limit exceeded.");
            var next = checked(_sequence + 1);
            var handle = new AttributeModifierHandle(_identity, next);
            var candidate = new Dictionary<ulong, AttributeModifierSnapshot>(_modifiers)
            {
                [next] = new AttributeModifierSnapshot(handle, source, key, operation, amount, priority)
            };
            var computed = Recalculate(_bases, _derived, candidate);
            var resources = RecalculateResources(computed, _resources);
            _modifiers = candidate;
            _effective = computed;
            _resources = resources;
            _sequence = next;
            return handle;
        }
    }

    /// <summary>撤销精确的实体代际/序号修饰，重复和异实体撤销不会改变状态。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="handle">先前授予的凭据。</param>
    /// <returns>是否实际撤销。</returns>
    public bool RemoveModifier(IWorldWriteContext context, AttributeModifierHandle handle)
    {
        _identity.Demand(context);
        if (handle.Identity != _identity) return false;
        lock (_gate)
        {
            if (!_modifiers.ContainsKey(handle.Sequence)) return false;
            var candidate = new Dictionary<ulong, AttributeModifierSnapshot>(_modifiers);
            candidate.Remove(handle.Sequence);
            var computed = Recalculate(_bases, _derived, candidate);
            var resources = RecalculateResources(computed, _resources);
            _modifiers = candidate;
            _effective = computed;
            _resources = resources;
            return true;
        }
    }

    /// <summary>精确移除此业务来源下的全部活跃修饰，不影响其它来源。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="source">非零业务来源。</param>
    /// <returns>实际移除的 Modifier 个数。</returns>
    public int RemoveSource(IWorldWriteContext context, AttributeModifierSource source)
    {
        _identity.Demand(context);
        if (source.Value == 0) throw new ArgumentException("Source is empty.", nameof(source));
        lock (_gate)
        {
            var candidate = new Dictionary<ulong, AttributeModifierSnapshot>(_modifiers);
            foreach (var item in _modifiers.Where(item => item.Value.Source == source))
                candidate.Remove(item.Key);
            var count = _modifiers.Count - candidate.Count;
            if (count == 0) return 0;
            var computed = Recalculate(_bases, _derived, candidate);
            var resources = RecalculateResources(computed, _resources);
            _modifiers = candidate;
            _effective = computed;
            _resources = resources;
            return count;
        }
    }

    /// <summary>绑定一种资源到已有最大属性，初值必须在合法闭区间。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="key">资源键。</param>
    /// <param name="maximumStat">决定资源上限的 Stat。</param>
    /// <param name="initialCurrent">初始绝对值，不默认自动回满。</param>
    /// <returns>定义成功为 true；完全相同定义为 false。</returns>
    public bool DefineResource(IWorldWriteContext context, AttributeKey key, AttributeKey maximumStat,
        decimal initialCurrent)
    {
        _identity.Demand(context);
        ValidateKey(key);
        ValidateKey(maximumStat);
        lock (_gate)
        {
            if (!_effective.TryGetValue(maximumStat, out var stat))
                throw new KeyNotFoundException("Maximum stat is not defined.");
            if (initialCurrent < 0 || initialCurrent > stat.EffectiveValue)
                throw new ArgumentOutOfRangeException(nameof(initialCurrent));
            var initial = Round(initialCurrent);
            if (_resources.TryGetValue(key, out var prior))
            {
                if (prior.MaximumStat == maximumStat && prior.Current == initial) return false;
                throw new InvalidOperationException("Resource definition already exists.");
            }

            if (_resources.Count >= MaxKeys) throw new InvalidOperationException("Resource limit exceeded.");
            var candidate = new Dictionary<AttributeKey, AttributeResourceSnapshot>(_resources)
            {
                [key] = new AttributeResourceSnapshot(key, maximumStat, initial, stat.EffectiveValue)
            };
            _resources = candidate;
            return true;
        }
    }

    /// <summary>设置资源当前绝对值；越界和死亡拒绝且无副作用。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="key">资源键。</param>
    /// <param name="amount">目标绝对值，单位与对应 Stat 一致。</param>
    /// <returns>已改变、未改变、未找到、越界或死亡。</returns>
    public ResourceChangeResult SetCurrent(IWorldWriteContext context, AttributeKey key, decimal amount)
    {
        _identity.Demand(context);
        ValidateKey(key);
        lock (_gate)
        {
            if (_life.State != LifeState.Alive) return ResourceChangeResult.InvalidState;
            if (!_resources.TryGetValue(key, out var current)) return ResourceChangeResult.NotFound;
            if (amount < 0 || amount > current.Maximum) return ResourceChangeResult.OutOfRange;
            var next = Round(amount);
            if (next == current.Current) return ResourceChangeResult.Unchanged;
            _resources[key] = current with { Current = next };
            return ResourceChangeResult.Changed;
        }
    }

    /// <summary>按绝对差值改变资源；溢出/超出 0..Max 均返回 OutOfRange 并保留旧状态。</summary>
    /// <param name="context">有效同步租约。</param>
    /// <param name="key">资源键。</param>
    /// <param name="delta">正值回复、负值消费。</param>
    /// <returns>更改状态，未定义或死亡时拒绝。</returns>
    public ResourceChangeResult ChangeCurrent(IWorldWriteContext context, AttributeKey key, decimal delta)
    {
        _identity.Demand(context);
        ValidateKey(key);
        lock (_gate)
        {
            if (_life.State != LifeState.Alive) return ResourceChangeResult.InvalidState;
            if (!_resources.TryGetValue(key, out var current)) return ResourceChangeResult.NotFound;
            decimal next;
            try { next = checked(current.Current + delta); }
            catch (OverflowException) { return ResourceChangeResult.OutOfRange; }
            if (next < 0 || next > current.Maximum) return ResourceChangeResult.OutOfRange;
            next = Round(next);
            if (next == current.Current) return ResourceChangeResult.Unchanged;
            _resources[key] = current with { Current = next };
            return ResourceChangeResult.Changed;
        }
    }

    private static void ValidateKey(AttributeKey key)
    {
        if (key.IsEmpty) throw new ArgumentException("Attribute key is empty.", nameof(key));
    }

    private static void ValidateNonNegative(decimal value, string name)
    {
        if (value < 0 || value > MaxValue) throw new ArgumentOutOfRangeException(name);
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static Dictionary<AttributeKey, AttributeStatSnapshot> Recalculate(
        Dictionary<AttributeKey, decimal> bases,
        Dictionary<AttributeKey, DerivedAttributeDefinition> derived,
        Dictionary<ulong, AttributeModifierSnapshot> modifiers)
    {
        var result = new Dictionary<AttributeKey, AttributeStatSnapshot>();
        foreach (var (key, value) in bases.OrderBy(item => item.Key.Value, StringComparer.Ordinal))
            result.Add(key, new AttributeStatSnapshot(key, value,
                Evaluate(key, value, modifiers), false));

        foreach (var rule in derived.Values.OrderBy(item => item.Key.Value, StringComparer.Ordinal))
        {
            var seed = checked(result[rule.Source].EffectiveValue * rule.Factor + rule.Offset);
            if (seed > MaxValue) throw new OverflowException("Derived stat exceeds configured limit.");
            seed = Round(seed);
            result.Add(rule.Key, new AttributeStatSnapshot(rule.Key, seed,
                Evaluate(rule.Key, seed, modifiers), true));
        }

        return result;
    }

    private static decimal Evaluate(AttributeKey key, decimal seed,
        Dictionary<ulong, AttributeModifierSnapshot> modifiers)
    {
        var current = seed;
        foreach (var mod in modifiers.Values.Where(item => item.Key == key)
                     .OrderBy(item => item.Priority).ThenBy(item => item.Handle.Sequence))
        {
            current = mod.Operation switch
            {
                AttributeModifierOperation.Flat => checked(current + mod.Amount),
                AttributeModifierOperation.Percent => checked(current * (1 + mod.Amount / 100m)),
                _ => throw new InvalidOperationException("Unknown modifier operation.")
            };
            // 使用 Decimal 固定两位小数，避免依赖线程、浮点平台或字典枚举顺序。
            current = Round(Math.Max(0m, current));
            if (current > MaxValue) throw new OverflowException("Effective stat exceeds configured limit.");
        }

        return current;
    }

    private static Dictionary<AttributeKey, AttributeResourceSnapshot> RecalculateResources(
        Dictionary<AttributeKey, AttributeStatSnapshot> stats,
        Dictionary<AttributeKey, AttributeResourceSnapshot> resources)
    {
        var result = new Dictionary<AttributeKey, AttributeResourceSnapshot>();
        foreach (var (key, resource) in resources)
        {
            var maximum = stats[resource.MaximumStat].EffectiveValue;
            // 最大值降低只裁剪当前绝对值；最大值回升不凭空回复资源。
            result.Add(key, resource with { Maximum = maximum, Current = Math.Min(resource.Current, maximum) });
        }

        return result;
    }
}
