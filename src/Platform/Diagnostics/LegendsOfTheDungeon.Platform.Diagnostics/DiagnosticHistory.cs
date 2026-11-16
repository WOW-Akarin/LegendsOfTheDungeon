namespace LegendsOfTheDungeon.Platform.Diagnostics;

/// <summary>可追踪的固定事件阶段，禁用任意标签/负载。</summary>
public enum DiagnosticTraceStage
{
    /// <summary>操作已接纳。</summary>
    Accepted,
    /// <summary>操作开始执行。</summary>
    Started,
    /// <summary>操作完成。</summary>
    Completed,
    /// <summary>操作失败或取消。</summary>
    Faulted
}

/// <summary>单条受限关联 Trace，仅记录固定操作类别、阶段、关联 GUID。</summary>
public sealed class DiagnosticTraceEntry
{
    internal DiagnosticTraceEntry(long sequence, DiagnosticArea area,
        DiagnosticTraceStage stage, Guid correlation)
    {
        Sequence = sequence;
        Area = area;
        Stage = stage;
        Correlation = correlation;
    }

    /// <summary>本地单调事件序号。</summary>
    public long Sequence { get; }
    /// <summary>固定的操作类别。</summary>
    public DiagnosticArea Area { get; }
    /// <summary>固定阶段。</summary>
    public DiagnosticTraceStage Stage { get; }
    /// <summary>请求关联 GUID；不会作为 Metric 标签。</summary>
    public Guid Correlation { get; }
}

/// <summary>只保留最后 N 条关联 Trace 的有界 FIFO。</summary>
public sealed class DiagnosticTraceBuffer
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosticTraceEntry> _entries = new();
    private readonly int _capacity;
    private long _sequence;

    /// <summary>创建固定容量 Trace 缓冲。</summary>
    /// <param name="capacity">1–4096。</param>
    public DiagnosticTraceBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, DiagnosticBudget.MaximumTraceEntries);
        _capacity = capacity;
    }

    /// <summary>记录一个已校验阶段；不接收任意字符串、异常或秘密。</summary>
    /// <param name="area">固定技术领域。</param>
    /// <param name="stage">操作阶段。</param>
    /// <param name="correlation">非空关联 GUID。</param>
    public void Record(DiagnosticArea area, DiagnosticTraceStage stage, Guid correlation)
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        if (correlation == Guid.Empty)
        {
            throw new ArgumentException("Trace 关联标识不能为空。", nameof(correlation));
        }

        lock (_gate)
        {
            if (_entries.Count == _capacity)
            {
                _entries.Dequeue();
            }

            if (_sequence < long.MaxValue)
            {
                _sequence++;
            }

            _entries.Enqueue(new DiagnosticTraceEntry(_sequence, area, stage, correlation));
        }
    }

    /// <summary>按原始接纳顺序返回只读 Trace 快照。</summary>
    public IReadOnlyList<DiagnosticTraceEntry> Snapshot()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_entries.ToArray());
        }
    }
}

/// <summary>慢操作技术事实；只包含固定技术领域和关联 GUID。</summary>
public sealed class SlowOperationRecord
{
    internal SlowOperationRecord(DiagnosticArea area, Guid correlation,
        long elapsedMilliseconds)
    {
        Area = area;
        Correlation = correlation;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>慢操作所属固定领域。</summary>
    public DiagnosticArea Area { get; }
    /// <summary>可在 Trace 中查找的关联 GUID。</summary>
    public Guid Correlation { get; }
    /// <summary>操作耗时毫秒。</summary>
    public long ElapsedMilliseconds { get; }
}

/// <summary>单纯观测慢任务，阈值可确定复现且不调用业务 Handler。</summary>
public sealed class SlowOperationDetector
{
    private readonly object _gate = new();
    private readonly Queue<SlowOperationRecord> _recent = new();
    private readonly int _capacity;
    private readonly long _threshold;
    private long _observedTotal;

    /// <summary>创建有限慢操作历史，不使用每个任务 Timer。</summary>
    /// <param name="capacity">1–1024。</param>
    /// <param name="thresholdMilliseconds">正数毫秒阈值。</param>
    public SlowOperationDetector(int capacity, long thresholdMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, DiagnosticBudget.MaximumSlowEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdMilliseconds);
        _capacity = capacity;
        _threshold = thresholdMilliseconds;
    }

    /// <summary>从被信任的技术计时器记录耗时，未达到阈值不会增长历史。</summary>
    /// <param name="area">固定领域。</param>
    /// <param name="correlation">不可为空的关联 GUID。</param>
    /// <param name="elapsedMilliseconds">非负毫秒。</param>
    /// <returns>此次操作是否达到阈值。</returns>
    public bool Observe(DiagnosticArea area, Guid correlation, long elapsedMilliseconds)
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        if (correlation == Guid.Empty)
        {
            throw new ArgumentException("关联标识不能为空。", nameof(correlation));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMilliseconds);
        if (elapsedMilliseconds < _threshold)
        {
            return false;
        }

        lock (_gate)
        {
            if (_recent.Count == _capacity)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(new SlowOperationRecord(area, correlation, elapsedMilliseconds));
            if (_observedTotal < long.MaxValue)
            {
                _observedTotal++;
            }
        }

        return true;
    }

    /// <summary>观测到的慢操作总数，按 Int64 饱和累积。</summary>
    public long TotalSlowOperations
    {
        get
        {
            lock (_gate)
            {
                return _observedTotal;
            }
        }
    }

    /// <summary>有限的最近慢操作列表；不含异常、主体、任意资源字符串。</summary>
    public IReadOnlyList<SlowOperationRecord> Snapshot()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_recent.ToArray());
        }
    }
}
