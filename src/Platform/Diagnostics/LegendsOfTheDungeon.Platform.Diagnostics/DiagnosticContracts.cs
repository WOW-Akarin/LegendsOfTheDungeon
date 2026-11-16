namespace LegendsOfTheDungeon.Platform.Diagnostics;

/// <summary>固定低基数技术领域，不得使用 EntityId、ActorId 或动态字符串作为指标标签。</summary>
public enum DiagnosticArea
{
    /// <summary>世界 Tick 处理时间；未来 P19 接入。</summary>
    Tick,
    /// <summary>技术队列等待与任务运行；可由 P12 Worker 使用。</summary>
    Queue,
    /// <summary>数据库技术操作；未来生产适配器接入。</summary>
    Database,
    /// <summary>AI 计算；未来 AI Runtime 接入。</summary>
    ArtificialIntelligence,
    /// <summary>脚本调用；未来 Script Runtime 接入。</summary>
    Script
}

/// <summary>统一的操作结果维度，不接受任意业务结果文本。</summary>
public enum DiagnosticOutcome
{
    /// <summary>成功完成。</summary>
    Succeeded,
    /// <summary>可识别异常或失败。</summary>
    Failed,
    /// <summary>取消或被关停中断。</summary>
    Cancelled,
    /// <summary>负载拒绝或超配额。</summary>
    Rejected
}

/// <summary>受检的诊断资源上限；不可配置成无界缓存。</summary>
public sealed class DiagnosticBudget
{
    /// <summary>硬性最大关联 Trace 数量。</summary>
    public const int MaximumTraceEntries = 4096;
    /// <summary>硬性最大慢操作记录数量。</summary>
    public const int MaximumSlowEntries = 1024;
    /// <summary>硬性最大 CrashContext 快照事件数量。</summary>
    public const int MaximumCrashEntries = 128;

    /// <summary>显式设置各有界缓存与慢操作阈值。</summary>
    /// <param name="traceEntries">1–4096 条关联 Trace。</param>
    /// <param name="slowEntries">1–1024 条慢操作。</param>
    /// <param name="crashEntries">1–128 条崩溃上下文记录。</param>
    /// <param name="slowThresholdMilliseconds">超过或等于此非负毫秒阈值视为慢操作，必须为正数。</param>
    public DiagnosticBudget(
        int traceEntries, int slowEntries, int crashEntries,
        long slowThresholdMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(traceEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(traceEntries, MaximumTraceEntries);
        ArgumentOutOfRangeException.ThrowIfLessThan(slowEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slowEntries, MaximumSlowEntries);
        ArgumentOutOfRangeException.ThrowIfLessThan(crashEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(crashEntries, MaximumCrashEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slowThresholdMilliseconds);
        TraceEntries = traceEntries;
        SlowEntries = slowEntries;
        CrashEntries = crashEntries;
        SlowThresholdMilliseconds = slowThresholdMilliseconds;
    }

    /// <summary>每个实例最多持有的 Trace 条数。</summary>
    public int TraceEntries { get; }
    /// <summary>每个实例最多持有的慢操作条数。</summary>
    public int SlowEntries { get; }
    /// <summary>一次 CrashContext 最多返回的最近 Trace 条数。</summary>
    public int CrashEntries { get; }
    /// <summary>固定慢操作阈值，毫秒。</summary>
    public long SlowThresholdMilliseconds { get; }
}
