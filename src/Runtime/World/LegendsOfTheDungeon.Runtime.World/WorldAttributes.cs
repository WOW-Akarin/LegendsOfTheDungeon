using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>P25 对 P17 canonical 实体的窄能力路由；不公开实体或第二份资源真相。</summary>
public sealed partial class WorldInstance
{
    /// <summary>按完整身份读取活动实体的冻结属性/资源及来源账本；Faulted 不返回可信活动查询。</summary>
    /// <param name="identity">包含代际的实体完整身份。</param>
    /// <param name="snapshot">只读属性及资源状态。</param>
    /// <returns>该代际是否处于活动世界。</returns>
    public bool TryGetAttributes(EntityIdentity identity, out EntityAttributeSnapshot? snapshot)
    {
        lock (_gate)
        {
            var result = FindActive(identity, out var entry);
            if (result != WorldEntityResult.Added)
            {
                snapshot = null;
                return false;
            }

            snapshot = entry!.Capabilities.Attributes.Capture();
            return true;
        }
    }

    /// <summary>设置当前活动实体基础属性，只通过 P18 世界作用域写入。</summary>
    /// <param name="context">当前同步世界执行租约。</param>
    /// <param name="identity">已注册实体的完整身份。</param>
    /// <param name="key">需要设置的属性键。</param>
    /// <param name="value">非负基础值。</param>
    /// <returns>明确结果，不将旧代际当成成功。</returns>
    public WorldEntityResult SetBaseAttribute(IWorldWriteContext context, EntityIdentity identity,
        AttributeKey key, decimal value)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            return entry!.Capabilities.Attributes.SetBase(context, key, value)
                ? WorldEntityResult.AttributeChanged : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>定义从单一基础属性推导的新属性；重复相同定义幂等。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">已注册实体完整身份。</param>
    /// <param name="key">派生键。</param>
    /// <param name="source">基础属性来源键。</param>
    /// <param name="factor">倍数。</param>
    /// <param name="offset">固定加数。</param>
    /// <returns>变更或重复状态。</returns>
    public WorldEntityResult DefineDerivedAttribute(IWorldWriteContext context, EntityIdentity identity,
        AttributeKey key, AttributeKey source, decimal factor, decimal offset)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            return entry!.Capabilities.Attributes.DefineDerived(context, key, source, factor, offset)
                ? WorldEntityResult.AttributeChanged : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>为活动实体添加可撤销的来源修饰。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">完整实体代际。</param>
    /// <param name="source">业务来源。</param>
    /// <param name="key">目标属性。</param>
    /// <param name="operation">绝对增减或百分比。</param>
    /// <param name="amount">变化量。</param>
    /// <param name="priority">优先级。</param>
    /// <param name="handle">成功时发出的精确撤销凭据。</param>
    /// <returns>成功或实体代际/生命周期拒绝。</returns>
    public WorldEntityResult GrantAttributeModifier(IWorldWriteContext context, EntityIdentity identity,
        AttributeModifierSource source, AttributeKey key, AttributeModifierOperation operation,
        decimal amount, int priority, out AttributeModifierHandle handle)
    {
        Demand(context);
        lock (_gate)
        {
            handle = default;
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            handle = entry!.Capabilities.Attributes.AddModifier(context, source, key, operation, amount, priority);
            return WorldEntityResult.ModifierAdded;
        }
    }

    /// <summary>撤销当前代际的精确 Modifier；重复旧撤销不会影响其它来源。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">实体完整身份。</param>
    /// <param name="handle">精确凭据。</param>
    /// <returns>撤销、重复或过期结果。</returns>
    public WorldEntityResult RevokeAttributeModifier(IWorldWriteContext context,
        EntityIdentity identity, AttributeModifierHandle handle)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            if (handle.Identity != identity) return WorldEntityResult.Stale;
            return entry!.Capabilities.Attributes.RemoveModifier(context, handle)
                ? WorldEntityResult.ModifierRemoved : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>只移除一个业务来源的全部活跃修饰，不影响其它来源。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">完整实体身份。</param>
    /// <param name="source">业务来源。</param>
    /// <param name="removed">本次实际撤销的个数。</param>
    /// <returns>撤销或幂等结果。</returns>
    public WorldEntityResult RevokeAttributeSource(IWorldWriteContext context,
        EntityIdentity identity, AttributeModifierSource source, out int removed)
    {
        Demand(context);
        lock (_gate)
        {
            removed = 0;
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            removed = entry!.Capabilities.Attributes.RemoveSource(context, source);
            return removed > 0 ? WorldEntityResult.ModifierRemoved : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>创建资源并绑定已有 Stat 上限；初始值不自动回满。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">完整实体身份。</param>
    /// <param name="key">资源键。</param>
    /// <param name="maximumStat">最大值属性键。</param>
    /// <param name="initialCurrent">初始资源。</param>
    /// <returns>资源新建或重复。</returns>
    public WorldEntityResult DefineEntityResource(IWorldWriteContext context, EntityIdentity identity,
        AttributeKey key, AttributeKey maximumStat, decimal initialCurrent)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            return entry!.Capabilities.Attributes.DefineResource(context, key, maximumStat, initialCurrent)
                ? WorldEntityResult.AttributeChanged : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>以差值增加或消费资源，拒绝死亡和越界。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">完整实体身份。</param>
    /// <param name="key">资源键。</param>
    /// <param name="delta">有符号变化量。</param>
    /// <returns>资源改变、越界或其它拒绝。</returns>
    public WorldEntityResult ChangeEntityResource(IWorldWriteContext context, EntityIdentity identity,
        AttributeKey key, decimal delta)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            return MapResourceChange(entry!.Capabilities.Attributes.ChangeCurrent(context, key, delta));
        }
    }

    /// <summary>设置资源绝对当前值，拒绝死亡和越界。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">完整实体身份。</param>
    /// <param name="key">资源键。</param>
    /// <param name="amount">新的当前值。</param>
    /// <returns>变更或拒绝。</returns>
    public WorldEntityResult SetEntityResource(IWorldWriteContext context, EntityIdentity identity,
        AttributeKey key, decimal amount)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            return MapResourceChange(entry!.Capabilities.Attributes.SetCurrent(context, key, amount));
        }
    }

    private static WorldEntityResult MapResourceChange(ResourceChangeResult result) => result switch
    {
        ResourceChangeResult.Changed => WorldEntityResult.ResourceChanged,
        ResourceChangeResult.Unchanged => WorldEntityResult.AlreadyCompleted,
        ResourceChangeResult.NotFound => WorldEntityResult.NotFound,
        ResourceChangeResult.OutOfRange => WorldEntityResult.ResourceOutOfRange,
        ResourceChangeResult.InvalidState => WorldEntityResult.InvalidState,
        _ => throw new InvalidOperationException("Unexpected resource change result.")
    };
}
