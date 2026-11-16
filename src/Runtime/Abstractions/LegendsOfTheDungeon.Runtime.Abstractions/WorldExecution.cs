using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Runtime.Abstractions;

/// <summary>由世界执行器签发的只读异步回执票据；调用方不能自行构造真实执行授权。</summary>
public interface IWorldReplyTicket
{
    /// <summary>拥有此请求的世界。</summary>
    WorldId World { get; }
    /// <summary>签发请求时的世界 Epoch。</summary>
    WorldEpoch Epoch { get; }
    /// <summary>签发请求时的实体标识。</summary>
    EntityId Entity { get; }
    /// <summary>签发请求时的实体 Generation。</summary>
    EntityGeneration Generation { get; }
    /// <summary>每世界递增的请求序号，较新请求会使旧请求过期。</summary>
    long RequestSequence { get; }
}

/// <summary>同步 World 写入契约；不提供异步生命周期，所有方法都要求当前真实执行租约。</summary>
public interface IWorldWriteContext
{
    /// <summary>当前执行器唯一拥有的世界。</summary>
    WorldId World { get; }
    /// <summary>当前世界生命周期 Epoch。</summary>
    WorldEpoch Epoch { get; }

    /// <summary>对实体写入前必须显式验证领域实体绑定的 WorldId、Epoch、真实租约和 Generation。</summary>
    /// <param name="world">领域实体原本拥有的 WorldId，禁止直接从当前执行器推导填充。</param>
    /// <param name="epoch">领域实体原本绑定的 WorldEpoch，不是外部消息无条件提供的值。</param>
    /// <param name="entity">待写实体 ID。</param>
    /// <param name="generation">当下真实实体代际。</param>
    void Demand(WorldId world, WorldEpoch epoch, EntityId entity, EntityGeneration generation);

    /// <summary>登记一条技术实体代际，仅允许在同步执行器上下文调用。</summary>
    /// <param name="entity">实体 ID。</param>
    /// <param name="generation">必须严格高于已经退休的同 ID 代际。</param>
    void BindEntity(EntityId entity, EntityGeneration generation);

    /// <summary>终结实体当前技术代际，并立即使此前的异步回执全部过期。</summary>
    /// <param name="entity">实体 ID。</param>
    /// <param name="generation">必须与当前活跃代际一致。</param>
    void RetireEntity(EntityId entity, EntityGeneration generation);

    /// <summary>只有有效同步世界上下文才能签发一次性的外部异步请求回执票据。</summary>
    /// <param name="entity">需要验证的活跃实体。</param>
    /// <param name="generation">真实代际。</param>
    /// <returns>可信外部 Worker 返回时必须原样持有的 opaque 票据。</returns>
    IWorldReplyTicket IssueReplyTicket(EntityId entity, EntityGeneration generation);
}

/// <summary>世界唯一执行者接纳的同步不可变命令；不允许 await 后继续持有写入上下文。</summary>
public interface IWorldAction
{
    /// <summary>在有限时间、世界独占的同步租约内提交领域状态变化。</summary>
    /// <param name="context">每次调用独立的有效世界写入租约。</param>
    void Execute(IWorldWriteContext context);
}
