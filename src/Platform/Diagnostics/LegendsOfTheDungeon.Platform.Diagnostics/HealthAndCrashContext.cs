namespace LegendsOfTheDungeon.Platform.Diagnostics;

/// <summary>固定的技术健康组件，不接受来自网络或实体 ID 的动态标签。</summary>
public enum HealthComponent
{
    /// <summary>世界运行时，由未来 Host 显式更新。</summary>
    Runtime,
    /// <summary>持久化技术组件。</summary>
    Persistence,
    /// <summary>网络技术组件。</summary>
    Networking,
    /// <summary>后台并发组件。</summary>
    Background,
    /// <summary>诊断组件自身。</summary>
    Diagnostics
}

/// <summary>健康状态；Unknown 不等于 Ready。</summary>
public enum HealthCondition
{
    /// <summary>没有真实探针或尚未收到健康事实。</summary>
    Unknown,
    /// <summary>真实探针报告可用。</summary>
    Healthy,
    /// <summary>仍部分可用但需关注。</summary>
    Degraded,
    /// <summary>明确不可用。</summary>
    Unavailable
}

/// <summary>某固定组件的不可变健康快照。</summary>
public sealed class HealthItem
{
    internal HealthItem(HealthComponent component, HealthCondition condition)
    {
        Component = component;
        Condition = condition;
    }

    /// <summary>固定组件类别。</summary>
    public HealthComponent Component { get; }
    /// <summary>无自由文本的状态。</summary>
    public HealthCondition Condition { get; }
}

/// <summary>固定五个组件的技术健康状态记录器，默认 Unknown/未就绪。</summary>
public sealed class FixedHealthRegistry
{
    private readonly object _gate = new();
    private readonly HealthCondition[] _conditions = new HealthCondition[5];

    /// <summary>上报某组件的真实探针状态；只限受信任调用方。</summary>
    /// <param name="component">固定组件类别。</param>
    /// <param name="condition">预定义健康状态。</param>
    public void Report(HealthComponent component, HealthCondition condition)
    {
        if (!Enum.IsDefined(component))
        {
            throw new ArgumentOutOfRangeException(nameof(component));
        }

        if (!Enum.IsDefined(condition))
        {
            throw new ArgumentOutOfRangeException(nameof(condition));
        }

        lock (_gate)
        {
            _conditions[(int)component] = condition;
        }
    }

    /// <summary>返回固定五个组件的不可变状态快照，不改变世界生命周期。</summary>
    public IReadOnlyList<HealthItem> Snapshot()
    {
        var result = new HealthItem[_conditions.Length];
        lock (_gate)
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new HealthItem((HealthComponent)i, _conditions[i]);
            }
        }

        return Array.AsReadOnly(result);
    }

    /// <summary>只有五个组件都被真实探针明确报告 Healthy 时才视为全部就绪。</summary>
    public bool AllHealthy
    {
        get
        {
            lock (_gate)
            {
                return _conditions.All(static x => x == HealthCondition.Healthy);
            }
        }
    }
}

/// <summary>崩溃原因白名单，不包含原始 Exception 或凭证。</summary>
public enum CrashCause
{
    /// <summary>不预期的技术异常。</summary>
    UnexpectedException,
    /// <summary>生命周期/数据不变量故障。</summary>
    InvariantViolation,
    /// <summary>外部依赖不可用。</summary>
    DependencyUnavailable,
    /// <summary>未能在期限内完成关停。</summary>
    ShutdownTimeout
}

/// <summary>用于人工定位的不可变有界 CrashContext 快照，不直接抛出或改变业务状态。</summary>
public sealed class CrashContextSnapshot
{
    internal CrashContextSnapshot(
        CrashCause cause, Guid correlation, IReadOnlyList<DiagnosticTraceEntry> traces,
        IReadOnlyList<SlowOperationRecord> slowOperations,
        IReadOnlyList<MetricCell> metrics, IReadOnlyList<HealthItem> health)
    {
        Cause = cause;
        Correlation = correlation;
        Traces = traces;
        SlowOperations = slowOperations;
        Metrics = metrics;
        Health = health;
    }

    /// <summary>固定原因代码，不含异常原文。</summary>
    public CrashCause Cause { get; }
    /// <summary>非空关联 GUID。</summary>
    public Guid Correlation { get; }
    /// <summary>最新的有限 Trace 快照。</summary>
    public IReadOnlyList<DiagnosticTraceEntry> Traces { get; }
    /// <summary>最新有限慢操作快照。</summary>
    public IReadOnlyList<SlowOperationRecord> SlowOperations { get; }
    /// <summary>固定 20 个 Metric 槽位。</summary>
    public IReadOnlyList<MetricCell> Metrics { get; }
    /// <summary>固定 5 个 Health 组件。</summary>
    public IReadOnlyList<HealthItem> Health { get; }
}

/// <summary>仅组合现有只读诊断快照的 CrashContext 构造者。</summary>
public sealed class CrashContextCapture
{
    private readonly FixedMetricRecorder _metrics;
    private readonly FixedHealthRegistry _health;
    private readonly DiagnosticTraceBuffer _trace;
    private readonly SlowOperationDetector _slow;
    private readonly int _maximumEntries;

    /// <summary>采用固定来源生成安全快照，无运行时引用和业务写入 API。</summary>
    /// <param name="metrics">已有限聚合的固定指标。</param>
    /// <param name="health">固定五个组件的状态。</param>
    /// <param name="trace">有限关联事件。</param>
    /// <param name="slow">有限慢操作事实。</param>
    /// <param name="maximumEntries">每种历史最多 1–128 条。</param>
    public CrashContextCapture(
        FixedMetricRecorder metrics, FixedHealthRegistry health,
        DiagnosticTraceBuffer trace, SlowOperationDetector slow,
        int maximumEntries)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _slow = slow ?? throw new ArgumentNullException(nameof(slow));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntries, DiagnosticBudget.MaximumCrashEntries);
        _maximumEntries = maximumEntries;
    }

    /// <summary>从当前快照捕获最近 N 条结构化事实；不读取原始异常、Token、SQL 参数或玩家状态。</summary>
    /// <param name="cause">固定故障类别。</param>
    /// <param name="correlation">受信任、非空的关联 GUID。</param>
    /// <returns>可安全传递的有限快照。</returns>
    public CrashContextSnapshot Capture(CrashCause cause, Guid correlation)
    {
        if (!Enum.IsDefined(cause))
        {
            throw new ArgumentOutOfRangeException(nameof(cause));
        }

        if (correlation == Guid.Empty)
        {
            throw new ArgumentException("崩溃关联标识不能为空。", nameof(correlation));
        }

        return new CrashContextSnapshot(
            cause, correlation,
            Array.AsReadOnly(_trace.Snapshot().TakeLast(_maximumEntries).ToArray()),
            Array.AsReadOnly(_slow.Snapshot().TakeLast(_maximumEntries).ToArray()),
            _metrics.Snapshot(), _health.Snapshot());
    }
}
