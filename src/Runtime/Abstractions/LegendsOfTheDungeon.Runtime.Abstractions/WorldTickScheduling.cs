using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Runtime.Abstractions;

/// <summary>可信 Tick 调度器向 P18 普通世界信箱提交命令的精简结果。</summary>
public enum WorldTickAdmissionStatus
{
    /// <summary>命令被接纳，不代表已执行或状态已提交。</summary>
    Accepted,
    /// <summary>普通信箱已满，未接纳。</summary>
    Busy,
    /// <summary>世界已关闭或故障，未接纳。</summary>
    Closed
}

/// <summary>仅反映 P18 单轮同步消费状态，不返回可变世界引用。</summary>
public enum WorldTickBatchStatus
{
    /// <summary>本轮同步世界消息在预算内正常消费。</summary>
    Completed,
    /// <summary>该世界已有同步执行者，本轮未取得独占租约。</summary>
    Busy,
    /// <summary>世界信箱已关闭并排空。</summary>
    Closed,
    /// <summary>世界执行发生不可恢复异常，不得继续模拟。</summary>
    Faulted
}

/// <summary>
/// 只由可信组合根提供给 P19 的最小 P18 同步执行端口；不向任意网络代码开放。
/// Port 仅投递/消费消息，绝不向调度 Worker 签发 IWorldWriteContext。
/// </summary>
public interface IWorldTickDispatchPort
{
    /// <summary>此端口唯一对应的 WorldId。</summary>
    WorldId World { get; }

    /// <summary>此端口真实的 WorldEpoch。</summary>
    WorldEpoch Epoch { get; }

    /// <summary>不可恢复故障时为 true。</summary>
    bool IsFaulted { get; }

    /// <summary>将单个同步 Tick Action 加入有界普通世界信箱。</summary>
    /// <param name="action">同步世界命令；尚未获得写权限。</param>
    /// <returns>接纳、背压或关闭；Accepted 不等于 Commit。</returns>
    WorldTickAdmissionStatus OfferTick(IWorldAction action);

    /// <summary>由 P18 尝试独占消费有界数量的消息，并仅返回状态。</summary>
    /// <param name="maximumItems">最多消费的信箱消息数，P18 限制不超过 1024。</param>
    /// <returns>消费、竞争、关闭或故障状态。</returns>
    WorldTickBatchStatus RunTickBatch(int maximumItems);
}
