using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Entities.Core;

/// <summary>不可变实体归属身份；World、Epoch、Id、Generation 必须一起匹配，不能只比较 EntityId。</summary>
public readonly record struct EntityIdentity
{
    /// <summary>创建具有完整非空归属版本的实体身份。</summary>
    /// <param name="world">实体所属世界。</param>
    /// <param name="epoch">实体所属世界当前生命周期版本。</param>
    /// <param name="id">实体标识。</param>
    /// <param name="generation">该标识的当前代际。</param>
    public EntityIdentity(WorldId world, WorldEpoch epoch, EntityId id, EntityGeneration generation)
    {
        if (world.IsEmpty || epoch.IsEmpty || id.IsEmpty || generation.IsEmpty)
        {
            throw new ArgumentException("EntityIdentity requires non-empty world, epoch, id and generation.");
        }

        World = world;
        Epoch = epoch;
        Id = id;
        Generation = generation;
    }

    /// <summary>实体所属世界。</summary>
    public WorldId World { get; }

    /// <summary>所属世界生命周期版本。</summary>
    public WorldEpoch Epoch { get; }

    /// <summary>实体运行期 ID。</summary>
    public EntityId Id { get; }

    /// <summary>实体 ID 的单调代际。</summary>
    public EntityGeneration Generation { get; }

    /// <summary>是否为 CLR default 或不完整身份；不能用于活动实体。</summary>
    public bool IsEmpty => World.IsEmpty || Epoch.IsEmpty || Id.IsEmpty || Generation.IsEmpty;

    internal void Demand(IWorldWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Demand(World, Epoch, Id, Generation);
    }
}

/// <summary>只公开不可变身份的能力；不允许在实体存活期间重新指向另一世界或代际。</summary>
public sealed class IdentityCapability
{
    internal IdentityCapability(EntityIdentity identity) => Value = identity;

    /// <summary>固定的归属信息。</summary>
    public EntityIdentity Value { get; }
}

/// <summary>唯一持有实体位置及朝向的能力；所有写入先验证同步世界写租约。</summary>
public sealed class TransformCapability
{
    private readonly EntityIdentity _identity;
    private readonly object _gate = new();
    private Transform _value;

    internal TransformCapability(EntityIdentity identity, Transform initial)
    {
        _identity = identity;
        _value = initial;
    }

    /// <summary>返回按值复制的不可变位置快照，不返回可变内部引用。</summary>
    public Transform Value
    {
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }
    }

    /// <summary>仅在实体原本所属世界的有效同步租约中更新 Transform。</summary>
    /// <param name="context">P18 WorldExecutor 签发的同步写上下文。</param>
    /// <param name="next">新的有限世界位置和朝向。</param>
    public void Relocate(IWorldWriteContext context, Transform next)
    {
        _identity.Demand(context);
        lock (_gate)
        {
            _value = next;
        }
    }
}

/// <summary>最小生命标记；血量联动、死亡事件及完整战斗清理归 P32/P34。</summary>
public enum LifeState
{
    /// <summary>存活。</summary>
    Alive,
    /// <summary>已标记死亡，不可选择新的目标。</summary>
    Dead
}

/// <summary>基础生命能力只暴露读取；状态切换必须经过薄实体组合以同时清理目标。</summary>
public sealed class LifeCapability
{
    internal LifeCapability() { }

    /// <summary>当前生命标记。</summary>
    public LifeState State { get; internal set; } = LifeState.Alive;
}

/// <summary>目标的不可变身份快照；不提供目标实体引用或跨世界修改能力。</summary>
public readonly record struct TargetState
{
    /// <summary>创建目标快照，null 表示无目标。</summary>
    /// <param name="selected">目标的完整身份快照。</param>
    public TargetState(EntityIdentity? selected) => Selected = selected;

    /// <summary>当前目标的完整归属版本，可能为空。</summary>
    public EntityIdentity? Selected { get; }
}

/// <summary>可选目标能力；不保留其他实体引用，P17 负责实际注册/代际存在性检查。</summary>
public sealed class TargetCapability
{
    private readonly EntityIdentity _identity;
    private readonly LifeCapability _life;
    private readonly object _gate = new();
    private TargetState _state;

    internal TargetCapability(EntityIdentity identity, LifeCapability life)
    {
        _identity = identity;
        _life = life;
    }

    /// <summary>复制目标快照；不暴露可变集合或目标对象。</summary>
    public TargetState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>在有效同步写租约中选择或清除目标；跨世界、旧 Epoch 或自身目标被拒绝。</summary>
    /// <param name="context">当前实体的世界同步写上下文。</param>
    /// <param name="target">完整目标身份；null 清空目标。</param>
    public void Select(IWorldWriteContext context, EntityIdentity? target)
    {
        _identity.Demand(context);
        if (target is { } candidate)
        {
            if (candidate.IsEmpty || candidate.World != _identity.World ||
                candidate.Epoch != _identity.Epoch || candidate.Id == _identity.Id)
            {
                throw new ArgumentException("Target must be a different entity in the same world and epoch.", nameof(target));
            }

            if (_life.State != LifeState.Alive)
            {
                throw new InvalidOperationException("Dead entities cannot select a target.");
            }
        }

        lock (_gate)
        {
            _state = new TargetState(target);
        }
    }

    internal void ClearOnDeath()
    {
        lock (_gate)
        {
            _state = default;
        }
    }
}

/// <summary>显式、固定的薄实体能力组合，不提供 Get&lt;T&gt;、服务定位或可变能力字典。</summary>
public sealed class EntityCapabilities
{
    /// <summary>创建尚未附加至世界注册表的实体能力；绑定与唯一归属由 P17 执行。</summary>
    /// <param name="identity">实体预定世界、Epoch、ID 与代际。</param>
    /// <param name="initialTransform">合法初始坐标和朝向。</param>
    public EntityCapabilities(EntityIdentity identity, Transform initialTransform)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("EntityIdentity must be complete.", nameof(identity));
        }

        Identity = new IdentityCapability(identity);
        Transform = new TransformCapability(identity, initialTransform);
        Life = new LifeCapability();
        Target = new TargetCapability(identity, Life);
        Control = new EntityControlCapability(identity);
        Tags = new EntityTagCapability(identity);
        Relationships = new EntityRelationshipCapability(identity);
    }

    /// <summary>不可变归属能力。</summary>
    public IdentityCapability Identity { get; }

    /// <summary>唯一位置真相的能力。</summary>
    public TransformCapability Transform { get; }

    /// <summary>只读生命能力。</summary>
    public LifeCapability Life { get; }

    /// <summary>不持有实体引用的窄目标能力。</summary>
    public TargetCapability Target { get; }

    /// <summary>按来源叠加并精确撤销的控制能力。</summary>
    public EntityControlCapability Control { get; }

    /// <summary>永久及临时来源标签的唯一集合能力。</summary>
    public EntityTagCapability Tags { get; }

    /// <summary>Owner/Creator/Controller/Summoner 四类独立的实体身份关系。</summary>
    public EntityRelationshipCapability Relationships { get; }

    /// <summary>在同一同步世界租约内完成死亡标记和目标清理；重复死亡无副作用。</summary>
    /// <param name="context">有效的 P18 世界写入租约。</param>
    /// <returns>首次从 Alive 转为 Dead 时为 true。</returns>
    public bool MarkDead(IWorldWriteContext context)
    {
        Identity.Value.Demand(context);
        if (Life.State == LifeState.Dead)
        {
            return false;
        }

        Life.State = LifeState.Dead;
        Target.ClearOnDeath();
        return true;
    }

    /// <summary>显式复活最小生命标记；生命值和重生规则由后续阶段定义。</summary>
    /// <param name="context">有效的 P18 世界写入租约。</param>
    /// <returns>本次是否从 Dead 转为 Alive。</returns>
    public bool Revive(IWorldWriteContext context)
    {
        Identity.Value.Demand(context);
        if (Life.State == LifeState.Alive)
        {
            return false;
        }

        Life.State = LifeState.Alive;
        return true;
    }
}
