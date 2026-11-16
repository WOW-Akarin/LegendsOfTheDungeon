using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Entities.Core;

/// <summary>同一实体可被多个独立来源叠加的控制标志；仅允许单项标志作为授予单位。</summary>
[Flags]
public enum EntityControlEffects
{
    /// <summary>没有控制状态。</summary>
    None = 0,
    /// <summary>眩晕。</summary>
    Stunned = 1,
    /// <summary>定身。</summary>
    Rooted = 2,
    /// <summary>沉默。</summary>
    Silenced = 4,
    /// <summary>恐惧。</summary>
    Feared = 8
}

/// <summary>用于授予临时控制或标签的非零、不可变业务来源标识。</summary>
public readonly record struct EntityEffectSource
{
    /// <summary>拒绝空来源，不能把无来源效果当成可信永久状态。</summary>
    /// <param name="value">业务来源的非零无符号值。</param>
    public EntityEffectSource(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    /// <summary>来源的不可变编号；default(0) 不合法。</summary>
    public ulong Value { get; }

    /// <summary>是否未初始化。</summary>
    public bool IsEmpty => Value == 0;
}

/// <summary>控制与标签使用不同种类的句柄，避免同序号误撤销另一个能力的效果。</summary>
public enum EntityEffectKind
{
    /// <summary>控制状态来源。</summary>
    Control,
    /// <summary>临时标签来源。</summary>
    Tag
}

/// <summary>与完整实体四元组及递增代号绑定的临时效果撤销凭据。</summary>
public readonly record struct EntityEffectHandle
{
    /// <summary>创建真实的效果凭据；不允许空代号。</summary>
    /// <param name="identity">效果所属的世界/实体/代际。</param>
    /// <param name="sequence">此实体代际中的非零递增编号。</param>
    /// <param name="kind">绑定控制或标签种类。</param>
    public EntityEffectHandle(EntityIdentity identity, ulong sequence, EntityEffectKind kind)
    {
        if (identity.IsEmpty || sequence == 0 || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("Effect handle needs a complete identity and sequence.");
        }

        Identity = identity;
        Sequence = sequence;
        Kind = kind;
    }

    /// <summary>句柄绑定的唯一能力种类。</summary>
    public EntityEffectKind Kind { get; }

    /// <summary>不可变实体身份，包括 Generation。</summary>
    public EntityIdentity Identity { get; }

    /// <summary>不复用的递增效果编号。</summary>
    public ulong Sequence { get; }
}

/// <summary>可预测的临时控制/标签新增结果。</summary>
public enum EntityEffectGrantResult
{
    /// <summary>新授予。</summary>
    Granted,
    /// <summary>同一 Source 与同一 Flag/Tag 已授予，返回原句柄。</summary>
    AlreadyExists,
    /// <summary>达到每实体上限。</summary>
    CapacityExceeded
}

/// <summary>用于确定性状态回放的控制来源不可变快照。</summary>
/// <param name="Sequence">此代际顺序号。</param>
/// <param name="Source">非零业务来源。</param>
/// <param name="Flag">单项控制标志。</param>
public readonly record struct EntityControlGrantSnapshot(
    ulong Sequence, EntityEffectSource Source, EntityControlEffects Flag);

/// <summary>用于确定性状态回放的临时标签来源不可变快照。</summary>
/// <param name="Sequence">此代际顺序号。</param>
/// <param name="Source">非零业务来源。</param>
/// <param name="Tag">规范化标签。</param>
public readonly record struct EntityTagGrantSnapshot(
    ulong Sequence, EntityEffectSource Source, string Tag);

/// <summary>控制来源集合；每种控制按 Source 引用计数，不把一个 Stun 当成全部来源。</summary>
public sealed class EntityControlCapability
{
    private const int Limit = 64;
    private readonly EntityIdentity _identity;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, (EntityEffectSource Source, EntityControlEffects Flag)> _grants = [];
    private ulong _sequence;

    internal EntityControlCapability(EntityIdentity identity) => _identity = identity;

    /// <summary>当前有效的控制标志按位或，按值读取。</summary>
    public EntityControlEffects Effective
    {
        get
        {
            lock (_gate)
            {
                var flags = EntityControlEffects.None;
                foreach (var grant in _grants.Values)
                {
                    flags |= grant.Flag;
                }

                return flags;
            }
        }
    }

    /// <summary>当前已使用的最大控制序号；即使撤销全部来源仍保留以防重用。</summary>
    public ulong LastSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    /// <summary>按序号升序的来源快照，记录后续撤销的确定性状态。</summary>
    public IReadOnlyList<EntityControlGrantSnapshot> ActiveGrants
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_grants.OrderBy(item => item.Key)
                    .Select(item => new EntityControlGrantSnapshot(item.Key,
                        item.Value.Source, item.Value.Flag)).ToArray());
            }
        }
    }

    /// <summary>活动临时授予数，最多 64，不是控制标志数。</summary>
    public int GrantCount
    {
        get
        {
            lock (_gate)
            {
                return _grants.Count;
            }
        }
    }

    /// <summary>同步租约中授予一项来源控制，重复来源/标志只返回旧句柄。</summary>
    /// <param name="context">实体原有四元组对应的 P18 同步租约。</param>
    /// <param name="source">非零来源。</param>
    /// <param name="flag">仅一位支持的控制标志。</param>
    /// <param name="handle">新或原有的带代际撤销凭据。</param>
    /// <returns>授予、幂等重复或配额不足。</returns>
    public EntityEffectGrantResult Grant(IWorldWriteContext context, EntityEffectSource source,
        EntityControlEffects flag, out EntityEffectHandle handle)
    {
        _identity.Demand(context);
        if (source.IsEmpty || flag is not (EntityControlEffects.Stunned or EntityControlEffects.Rooted
            or EntityControlEffects.Silenced or EntityControlEffects.Feared))
        {
            throw new ArgumentException("Control grant requires a supported single flag and source.");
        }

        lock (_gate)
        {
            foreach (var (sequence, entry) in _grants)
            {
                if (entry.Source == source && entry.Flag == flag)
                {
                    handle = new EntityEffectHandle(_identity, sequence, EntityEffectKind.Control);
                    return EntityEffectGrantResult.AlreadyExists;
                }
            }

            if (_grants.Count >= Limit)
            {
                handle = default;
                return EntityEffectGrantResult.CapacityExceeded;
            }

            _sequence = checked(_sequence + 1);
            _grants.Add(_sequence, (source, flag));
            handle = new EntityEffectHandle(_identity, _sequence, EntityEffectKind.Control);
            return EntityEffectGrantResult.Granted;
        }
    }

    /// <summary>只撤销精确匹配此代际及序号的一次来源效果。</summary>
    /// <param name="context">实体有效同步租约。</param>
    /// <param name="handle">来自该 ControlCapability 的句柄。</param>
    /// <returns>是否实际移除一个活跃来源。</returns>
    public bool Revoke(IWorldWriteContext context, EntityEffectHandle handle)
    {
        _identity.Demand(context);
        if (handle.Identity != _identity || handle.Kind != EntityEffectKind.Control)
        {
            return false;
        }

        lock (_gate)
        {
            return _grants.Remove(handle.Sequence);
        }
    }

    /// <summary>在实体退休时清除该代际全部控制来源。</summary>
    /// <param name="context">同步写租约。</param>
    public void Clear(IWorldWriteContext context)
    {
        _identity.Demand(context);
        lock (_gate)
        {
            _grants.Clear();
        }
    }
}

/// <summary>有限的稳定标签集合；永久标签与按来源授予的临时标签同时存在。</summary>
public sealed class EntityTagCapability
{
    private const int Limit = 64;
    private readonly EntityIdentity _identity;
    private readonly object _gate = new();
    private readonly HashSet<string> _permanent = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, (EntityEffectSource Source, string Tag)> _grants = [];
    private ulong _sequence;

    internal EntityTagCapability(EntityIdentity identity) => _identity = identity;

    /// <summary>所有实际有效标签，Ordinal 排序、按值冻结，不共享集合。</summary>
    public IReadOnlyList<string> ActiveTags
    {
        get
        {
            lock (_gate)
            {
                var tags = new SortedSet<string>(_permanent, StringComparer.Ordinal);
                foreach (var (_, tag) in _grants.Values)
                {
                    tags.Add(tag);
                }

                return Array.AsReadOnly(tags.ToArray());
            }
        }
    }

    /// <summary>当前已使用的最大临时标签序号；撤销后仍单调。</summary>
    public ulong LastSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    /// <summary>只包含永久来源的按值、Ordinal 排序快照。</summary>
    public IReadOnlyList<string> PermanentTags
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_permanent.Order(StringComparer.Ordinal).ToArray());
            }
        }
    }

    /// <summary>稳定顺序的临时 Tag 来源快照，不共享内部字典。</summary>
    public IReadOnlyList<EntityTagGrantSnapshot> ActiveGrants
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_grants.OrderBy(item => item.Key)
                    .Select(item => new EntityTagGrantSnapshot(item.Key,
                        item.Value.Source, item.Value.Tag)).ToArray());
            }
        }
    }

    /// <summary>所有永久和临时标签来源的总数，不按名称去重。</summary>
    public int SourceCount
    {
        get
        {
            lock (_gate)
            {
                return _permanent.Count + _grants.Count;
            }
        }
    }

    /// <summary>此名称是否拥有永久来源。</summary>
    /// <param name="tag">已规范化标签。</param>
    /// <returns>是否为永久标签。</returns>
    public bool HasPermanent(string tag)
    {
        ValidateTag(tag);
        lock (_gate)
        {
            return _permanent.Contains(tag);
        }
    }

    /// <summary>是否含有精确区分大小写的规范化标签。</summary>
    /// <param name="tag">ASCII 小写标签。</param>
    /// <returns>永久或临时存在该标签。</returns>
    public bool Contains(string tag)
    {
        ValidateTag(tag);
        lock (_gate)
        {
            return _permanent.Contains(tag) || _grants.Values.Any(item => item.Tag == tag);
        }
    }

    /// <summary>同步设置永久标签；临时效果撤销不会移除永久同名标签。</summary>
    /// <param name="context">实体有效同步租约。</param>
    /// <param name="tag">非空 ASCII 小写规范化标签。</param>
    /// <param name="enabled">启用或关闭永久来源。</param>
    /// <returns>永久标签集合是否真正改变。</returns>
    public bool SetPermanent(IWorldWriteContext context, string tag, bool enabled)
    {
        _identity.Demand(context);
        ValidateTag(tag);
        lock (_gate)
        {
            if (enabled && _permanent.Count + _grants.Count >= Limit && !_permanent.Contains(tag))
            {
                throw new InvalidOperationException("Entity tag source limit exceeded.");
            }

            return enabled ? _permanent.Add(tag) : _permanent.Remove(tag);
        }
    }

    /// <summary>同来源/标签重复授予保持同一个句柄，可与其它来源及永久标签叠加。</summary>
    /// <param name="context">实体有效同步租约。</param>
    /// <param name="source">临时效果的非零来源。</param>
    /// <param name="tag">规范化标签。</param>
    /// <param name="handle">授予句柄或同来源已存在句柄。</param>
    /// <returns>授予结果。</returns>
    public EntityEffectGrantResult Grant(IWorldWriteContext context, EntityEffectSource source,
        string tag, out EntityEffectHandle handle)
    {
        _identity.Demand(context);
        ValidateTag(tag);
        if (source.IsEmpty)
        {
            throw new ArgumentException("Temporary tag requires non-empty source.", nameof(source));
        }

        lock (_gate)
        {
            foreach (var (sequence, entry) in _grants)
            {
                if (entry.Source == source && StringComparer.Ordinal.Equals(entry.Tag, tag))
                {
                    handle = new EntityEffectHandle(_identity, sequence, EntityEffectKind.Tag);
                    return EntityEffectGrantResult.AlreadyExists;
                }
            }

            if (_permanent.Count + _grants.Count >= Limit)
            {
                handle = default;
                return EntityEffectGrantResult.CapacityExceeded;
            }

            _sequence = checked(_sequence + 1);
            _grants.Add(_sequence, (source, tag));
            handle = new EntityEffectHandle(_identity, _sequence, EntityEffectKind.Tag);
            return EntityEffectGrantResult.Granted;
        }
    }

    /// <summary>撤销精确临时来源，不影响其它来源或永久标签。</summary>
    /// <param name="context">实体有效同步租约。</param>
    /// <param name="handle">同实体同代际的标签句柄。</param>
    /// <returns>是否撤销了活动来源。</returns>
    public bool Revoke(IWorldWriteContext context, EntityEffectHandle handle)
    {
        _identity.Demand(context);
        if (handle.Identity != _identity || handle.Kind != EntityEffectKind.Tag)
        {
            return false;
        }

        lock (_gate)
        {
            return _grants.Remove(handle.Sequence);
        }
    }

    /// <summary>退休实体时清除全部永久与临时标签。</summary>
    /// <param name="context">有效同步租约。</param>
    public void Clear(IWorldWriteContext context)
    {
        _identity.Demand(context);
        lock (_gate)
        {
            _permanent.Clear();
            _grants.Clear();
        }
    }

    /// <summary>约束 P21 未来标签匹配为 ASCII lowercase、最多 48 字符。</summary>
    /// <param name="tag">待验证的标签。</param>
    public static void ValidateTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > 48 || tag[0] is < 'a' or > 'z' ||
            tag.Any(static ch => ch is not (>= 'a' and <= 'z' or >= '0' and <= '9'
                or '.' or '_' or '-' or ':')))
        {
            throw new ArgumentException("Tag must be 1–48 ASCII lowercase [a-z][a-z0-9._:-]*.", nameof(tag));
        }
    }
}

/// <summary>四种相互独立的实体指向关系；不得混为一个共享 Owner 字段。</summary>
public enum EntityRelationKind
{
    /// <summary>玩法归属者。</summary>
    Owner,
    /// <summary>最初创建者。</summary>
    Creator,
    /// <summary>当前控制者。</summary>
    Controller,
    /// <summary>当前召唤者。</summary>
    Summoner
}

/// <summary>四种不同关系的只读、按值快照，只有完整 EntityIdentity，不含可写引用。</summary>
/// <param name="Owner">归属实体。</param>
/// <param name="Creator">创建实体。</param>
/// <param name="Controller">控制实体。</param>
/// <param name="Summoner">召唤实体。</param>
public readonly record struct EntityRelations(EntityIdentity? Owner, EntityIdentity? Creator,
    EntityIdentity? Controller, EntityIdentity? Summoner)
{
    /// <summary>提取指定关系的身份；未知枚举拒绝。</summary>
    /// <param name="kind">关系类型。</param>
    /// <returns>指定关系对应的身份快照，或 null。</returns>
    public EntityIdentity? Get(EntityRelationKind kind) => kind switch
    {
        EntityRelationKind.Owner => Owner,
        EntityRelationKind.Creator => Creator,
        EntityRelationKind.Controller => Controller,
        EntityRelationKind.Summoner => Summoner,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal IEnumerable<EntityIdentity> Edges()
    {
        if (Owner is { } owner) yield return owner;
        if (Creator is { } creator) yield return creator;
        if (Controller is { } controller) yield return controller;
        if (Summoner is { } summoner) yield return summoner;
    }
}

/// <summary>四类实体关系的唯一领域持有者；世界注册表负责活动代际与图中环校验。</summary>
public sealed class EntityRelationshipCapability
{
    private readonly EntityIdentity _identity;
    private readonly object _gate = new();
    private EntityRelations _state;

    internal EntityRelationshipCapability(EntityIdentity identity) => _identity = identity;

    /// <summary>按值复制所有关系；不泄露目标实体对象。</summary>
    public EntityRelations State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>在真实同步租约里更新单一关系；活动状态和跨类型图无环由 WorldInstance 验证。</summary>
    /// <param name="context">实体有效同步租约。</param>
    /// <param name="kind">要改的唯一关系类型。</param>
    /// <param name="target">同 World/Epoch 的完整另一实体身份，或 null 清空。</param>
    /// <returns>该关系是否发生改变。</returns>
    public bool Set(IWorldWriteContext context, EntityRelationKind kind, EntityIdentity? target)
    {
        _identity.Demand(context);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (target is { } selected &&
            (selected.IsEmpty || selected.World != _identity.World ||
             selected.Epoch != _identity.Epoch || selected.Id == _identity.Id))
        {
            throw new ArgumentException("Relationship requires another entity in the same world and epoch.", nameof(target));
        }

        lock (_gate)
        {
            if (_state.Get(kind) == target)
            {
                return false;
            }

            _state = kind switch
            {
                EntityRelationKind.Owner => _state with { Owner = target },
                EntityRelationKind.Creator => _state with { Creator = target },
                EntityRelationKind.Controller => _state with { Controller = target },
                EntityRelationKind.Summoner => _state with { Summoner = target },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return true;
        }
    }

    /// <summary>删除退休代际全部入向关联，不需要关心引用者的关系角色。</summary>
    /// <param name="context">引用者自己的合法同步租约。</param>
    /// <param name="retired">已开始 Removing 的目标四元组。</param>
    public void ClearReference(IWorldWriteContext context, EntityIdentity retired)
    {
        _identity.Demand(context);
        lock (_gate)
        {
            _state = new EntityRelations(
                _state.Owner == retired ? null : _state.Owner,
                _state.Creator == retired ? null : _state.Creator,
                _state.Controller == retired ? null : _state.Controller,
                _state.Summoner == retired ? null : _state.Summoner);
        }
    }

    /// <summary>退休时清除自身所有出向关系。</summary>
    /// <param name="context">有效同步世界租约。</param>
    public void Clear(IWorldWriteContext context)
    {
        _identity.Demand(context);
        lock (_gate)
        {
            _state = default;
        }
    }
}
