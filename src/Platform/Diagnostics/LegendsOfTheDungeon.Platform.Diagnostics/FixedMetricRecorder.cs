namespace LegendsOfTheDungeon.Platform.Diagnostics;

/// <summary>只含固定维度的聚合结果；数值与业务状态分离。</summary>
public sealed class MetricCell
{
    internal MetricCell(DiagnosticArea area, DiagnosticOutcome outcome,
        long count, long totalMilliseconds, long maximumMilliseconds,
        long thresholdExceeded)
    {
        Area = area;
        Outcome = outcome;
        Count = count;
        TotalMilliseconds = totalMilliseconds;
        MaximumMilliseconds = maximumMilliseconds;
        ThresholdExceeded = thresholdExceeded;
    }

    /// <summary>固定的指标领域。</summary>
    public DiagnosticArea Area { get; }
    /// <summary>固定的结果分类。</summary>
    public DiagnosticOutcome Outcome { get; }
    /// <summary>采样次数，饱和到 Int64 上限。</summary>
    public long Count { get; }
    /// <summary>所有耗时的饱和和，单位毫秒。</summary>
    public long TotalMilliseconds { get; }
    /// <summary>最大已采样耗时，单位毫秒。</summary>
    public long MaximumMilliseconds { get; }
    /// <summary>达到慢操作阈值的采样数量。</summary>
    public long ThresholdExceeded { get; }
}

/// <summary>固定 5x4 个指标槽位，不接受动态标签或任意实体身份。</summary>
public sealed class FixedMetricRecorder
{
    private const int AreaCount = 5;
    private const int OutcomeCount = 4;
    private readonly object _gate = new();
    private readonly long[] _counts = new long[AreaCount * OutcomeCount];
    private readonly long[] _milliseconds = new long[AreaCount * OutcomeCount];
    private readonly long[] _maxima = new long[AreaCount * OutcomeCount];
    private readonly long[] _slowCounts = new long[AreaCount * OutcomeCount];
    private readonly long _threshold;

    /// <summary>定义慢操作阈值；聚合槽位数量不随事件增加。</summary>
    /// <param name="slowThresholdMilliseconds">正数毫秒阈值。</param>
    public FixedMetricRecorder(long slowThresholdMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slowThresholdMilliseconds);
        _threshold = slowThresholdMilliseconds;
    }

    /// <summary>固定维度的槽位总数，无论发生多少实体或任务。</summary>
    public int MaximumSeries => AreaCount * OutcomeCount;

    /// <summary>按固定类型记录技术操作；不使用自由文本或异常本体。</summary>
    /// <param name="area">Tick/Queue/DB/AI/Script。</param>
    /// <param name="outcome">成功/失败/取消/拒绝。</param>
    /// <param name="elapsedMilliseconds">非负毫秒数。</param>
    public void Record(
        DiagnosticArea area, DiagnosticOutcome outcome, long elapsedMilliseconds)
    {
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMilliseconds);
        var index = (int)area * OutcomeCount + (int)outcome;
        lock (_gate)
        {
            _counts[index] = SaturatingAdd(_counts[index], 1);
            _milliseconds[index] = SaturatingAdd(_milliseconds[index], elapsedMilliseconds);
            _maxima[index] = Math.Max(_maxima[index], elapsedMilliseconds);
            if (elapsedMilliseconds >= _threshold)
            {
                _slowCounts[index] = SaturatingAdd(_slowCounts[index], 1);
            }
        }
    }

    /// <summary>取得稳定只读的全部 20 个固定指标槽位，调用方不能写回聚合状态。</summary>
    public IReadOnlyList<MetricCell> Snapshot()
    {
        var result = new MetricCell[AreaCount * OutcomeCount];
        lock (_gate)
        {
            for (var area = 0; area < AreaCount; area++)
            {
                for (var outcome = 0; outcome < OutcomeCount; outcome++)
                {
                    var index = area * OutcomeCount + outcome;
                    result[index] = new MetricCell(
                        (DiagnosticArea)area, (DiagnosticOutcome)outcome,
                        _counts[index], _milliseconds[index],
                        _maxima[index], _slowCounts[index]);
                }
            }
        }

        return Array.AsReadOnly(result);
    }

    private static long SaturatingAdd(long existing, long additional) =>
        additional > long.MaxValue - existing ? long.MaxValue : existing + additional;
}
