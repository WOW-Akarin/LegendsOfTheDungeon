using System.Diagnostics;

namespace LegendsOfTheDungeon.Platform.Diagnostics;

/// <summary>仅包含固定聚合字段的安全输出事实，不能包含高基数标签和异常原文。</summary>
public sealed class DiagnosticObservation
{
    /// <summary>建立来自受检类别和非负毫秒的不可变指标事实。</summary>
    /// <param name="area">固定技术领域。</param>
    /// <param name="outcome">受限结果类别。</param>
    /// <param name="elapsedMilliseconds">非负耗时毫秒。</param>
    public DiagnosticObservation(
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
        Area = area;
        Outcome = outcome;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>固定领域。</summary>
    public DiagnosticArea Area { get; }
    /// <summary>固定结果维度。</summary>
    public DiagnosticOutcome Outcome { get; }
    /// <summary>非负耗时。</summary>
    public long ElapsedMilliseconds { get; }
}

/// <summary>可失败的遥测外部输出端口，只允许输出受检字段。</summary>
public interface IDiagnosticObservationSink
{
    /// <summary>异步发送安全的固定标签事实，调用方负责失败隔离。</summary>
    /// <param name="observation">无任何用户身份/异常字符串的事实。</param>
    /// <param name="cancellationToken">导出取消信号。</param>
    /// <returns>发送完成。</returns>
    ValueTask WriteAsync(
        DiagnosticObservation observation, CancellationToken cancellationToken);
}

/// <summary>非阻塞、有限长度的遥测输出队列；外部 Sink 仅在显式 FlushAsync 调用。</summary>
public sealed class BoundedObservationOutbox
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosticObservation> _entries = new();
    private int _flushing;
    private readonly int _capacity;
    private long _dropped;
    private long _failed;

    /// <summary>固定最大积压，满队列直接丢失遥测而不阻塞世界/Worker。</summary>
    /// <param name="capacity">1–4096 条。</param>
    public BoundedObservationOutbox(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, DiagnosticBudget.MaximumTraceEntries);
        _capacity = capacity;
    }

    /// <summary>同步、短时、非阻塞地尝试接纳结构化事实；永不调用外部 Sink。</summary>
    /// <param name="observation">受限字段，不含秘密。</param>
    /// <returns>缓存满则 false，调用方可继续业务操作。</returns>
    public bool TryOffer(DiagnosticObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_gate)
        {
            if (_entries.Count == _capacity)
            {
                if (_dropped < long.MaxValue)
                {
                    _dropped++;
                }

                return false;
            }

            _entries.Enqueue(observation);
            return true;
        }
    }

    /// <summary>已拒绝的遥测数量，按 Int64 饱和。</summary>
    public long Dropped
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    /// <summary>外部 Sink 失败数量，按 Int64 饱和。</summary>
    public long FailedExports
    {
        get
        {
            lock (_gate)
            {
                return _failed;
            }
        }
    }

    /// <summary>队列内当前待输出记录数。</summary>
    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>从独立执行上下文导出至多 maxItems 条；Sink 异常只记录失败，不传入业务。</summary>
    /// <param name="sink">不可信遥测收集器。</param>
    /// <param name="maximumItems">单轮最多导出条数，必须为正。</param>
    /// <param name="cancellationToken">导出中断会保留尚未领取的数据。</param>
    /// <returns>本轮已领取并尝试发送的条数。</returns>
    public async Task<int> FlushAsync(
        IDiagnosticObservationSink sink, int maximumItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0)
        {
            // 不让并发导出者彼此等待；当前导出者负责有界批次。
            return 0;
        }

        try
        {
            var attempted = 0;
            while (attempted < maximumItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DiagnosticObservation next;
                lock (_gate)
                {
                    if (_entries.Count == 0)
                    {
                        break;
                    }

                    next = _entries.Dequeue();
                }

                attempted++;
                try
                {
                    await sink.WriteAsync(next, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 已领取的遥测允许丢失，不回滚业务；取消后不执行后续发送。
                    throw;
                }
                catch (Exception)
                {
                    lock (_gate)
                    {
                        if (_failed < long.MaxValue)
                        {
                            _failed++;
                        }
                    }
                }
            }

            return attempted;
        }
        finally
        {
            Volatile.Write(ref _flushing, 0);
        }
    }
}

/// <summary>技术操作计时包装，不改变原业务异常或返回值，遥测失败不得替代业务结果。</summary>
public static class SafeOperationTelemetry
{
    /// <summary>执行真实异步任务并用单调钟计时；诊断故障与实际任务结果彻底隔离。</summary>
    /// <param name="work">不含世界写授权的实际后台工作。</param>
    /// <param name="area">固定监控领域。</param>
    /// <param name="correlation">受信任的非空关联 GUID。</param>
    /// <param name="metrics">固定槽位计数器。</param>
    /// <param name="slow">有限慢任务检测器。</param>
    /// <param name="trace">关联 Trace 缓冲。</param>
    /// <param name="outbox">可选有限异步遥测 Outbox，不会在业务线程调用 Sink。</param>
    /// <param name="cancellationToken">工作取消令牌。</param>
    public static async ValueTask RunAsync(
        Func<CancellationToken, ValueTask> work,
        DiagnosticArea area, Guid correlation,
        FixedMetricRecorder metrics, SlowOperationDetector slow,
        DiagnosticTraceBuffer trace, BoundedObservationOutbox? outbox = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(slow);
        ArgumentNullException.ThrowIfNull(trace);
        if (!Enum.IsDefined(area))
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        if (correlation == Guid.Empty)
        {
            throw new ArgumentException("关联标识不能为空。", nameof(correlation));
        }

        TryTrace(trace, area, DiagnosticTraceStage.Started, correlation);
        var start = Stopwatch.GetTimestamp();
        var outcome = DiagnosticOutcome.Succeeded;
        try
        {
            await work(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = DiagnosticOutcome.Cancelled;
            throw;
        }
        catch (Exception)
        {
            outcome = DiagnosticOutcome.Failed;
            throw;
        }
        finally
        {
            var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            try
            {
                metrics.Record(area, outcome, elapsed);
                slow.Observe(area, correlation, elapsed);
                trace.Record(area,
                    outcome == DiagnosticOutcome.Succeeded
                        ? DiagnosticTraceStage.Completed
                        : DiagnosticTraceStage.Faulted, correlation);
                outbox?.TryOffer(new DiagnosticObservation(area, outcome, elapsed));
            }
            catch (Exception)
            {
                // 故障遥测永远不能覆盖真实业务成功/失败，且不允许访问世界可变状态。
            }
        }
    }

    private static void TryTrace(DiagnosticTraceBuffer trace, DiagnosticArea area,
        DiagnosticTraceStage stage, Guid correlation)
    {
        try
        {
            trace.Record(area, stage, correlation);
        }
        catch (Exception)
        {
            // 任何诊断失败仅丢失遥测；原业务动作仍会执行。
        }
    }
}
