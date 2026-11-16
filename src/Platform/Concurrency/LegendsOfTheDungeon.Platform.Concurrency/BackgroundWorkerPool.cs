namespace LegendsOfTheDungeon.Platform.Concurrency;

/// <summary>Worker 级失败摘要；不会输出原始异常或用户任务负载。</summary>
public sealed class WorkerFailure
{
    internal WorkerFailure(long id)
    {
        Id = id;
        Code = "platform.worker.unhandled_exception";
    }

    /// <summary>失败任务 ID，供调用方在自己的安全范围内关联。</summary>
    public long Id { get; }

    /// <summary>固定错误代码；不得含异常、连接串及负载原文。</summary>
    public string Code { get; }
}

/// <summary>优雅关闭的稳定结果；未排空时必须处理 Outstanding 清单。</summary>
public sealed class WorkerShutdownReport
{
    internal WorkerShutdownReport(
        bool drained, IReadOnlyList<OutstandingWork> outstanding,
        long succeeded, long failed, long cancelled, IReadOnlyList<WorkerFailure> failures)
    {
        Drained = drained;
        Outstanding = outstanding;
        Succeeded = succeeded;
        Failed = failed;
        Cancelled = cancelled;
        RecentFailures = failures;
    }

    /// <summary>全部已接纳任务是否已确认结束。</summary>
    public bool Drained { get; }

    /// <summary>截至此刻仍在队列或执行中的任务，关闭超时不得忽略。</summary>
    public IReadOnlyList<OutstandingWork> Outstanding { get; }

    /// <summary>成功处理数量。</summary>
    public long Succeeded { get; }

    /// <summary>由异常导致的失败数量。</summary>
    public long Failed { get; }

    /// <summary>取消任务的数量。</summary>
    public long Cancelled { get; }

    /// <summary>最多保留有限条的脱敏失败记录。</summary>
    public IReadOnlyList<WorkerFailure> RecentFailures { get; }
}

/// <summary>有限并行度、逐项错误隔离的后台 Worker 池，不授予任何 World 写入权限。</summary>
public sealed class BackgroundWorkerPool : IAsyncDisposable
{
    private readonly object _failureGate = new();
    private readonly List<WorkerFailure> _failures = [];
    private readonly BoundedWorkChannel _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _workers;
    private readonly int _maximumFailureRecords;
    private long _succeeded;
    private long _failed;
    private long _cancelled;
    private int _disposed;

    /// <summary>立即启动固定数量的后台消费者。</summary>
    /// <param name="queue">共享有界任务队列，调用方拥有接纳权。</param>
    /// <param name="workerCount">正数且固定的并行数。</param>
    /// <param name="maximumFailureRecords">保留的最多脱敏失败记录数。</param>
    public BackgroundWorkerPool(BoundedWorkChannel queue, int workerCount, int maximumFailureRecords = 16)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFailureRecords);
        _maximumFailureRecords = maximumFailureRecords;
        _workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(ProcessAsync))
            .ToArray();
    }

    /// <summary>停止新任务接纳，按关闭期限排空；超时取消 Worker 并返回未完成任务快照。</summary>
    /// <param name="timeout">允许正常排空的非负墙钟期限（仅用于技术关停）。</param>
    /// <param name="cancellationToken">调用者停止等待的信号。</param>
    /// <returns>明确区分完成和超时的关闭结果。</returns>
    public async Task<WorkerShutdownReport> ShutdownAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);

        _queue.Complete();
        var drained = false;
        try
        {
            await Task.WhenAll(_workers).WaitAsync(timeout, cancellationToken);
            drained = true;
        }
        catch (TimeoutException)
        {
            _stop.Cancel();
        }

        IReadOnlyList<WorkerFailure> failures;
        lock (_failureGate)
        {
            failures = _failures.ToArray();
        }

        var outstanding = _queue.SnapshotOutstanding();
        return new WorkerShutdownReport(
            drained && outstanding.Count == 0, outstanding,
            Interlocked.Read(ref _succeeded),
            Interlocked.Read(ref _failed),
            Interlocked.Read(ref _cancelled),
            failures);
    }

    /// <summary>等待已发出的任务和 Worker 退出后释放取消源；调用方应先用限时 ShutdownAsync 停止接纳并取得未完成清单。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.Complete();
        _stop.Cancel();
        try
        {
            await Task.WhenAll(_workers);
        }
        finally
        {
            // 在所有 Worker 退出后释放尚未执行任务的闭包，不删除供恢复的 ID。
            _queue.ReleaseQueuedCallbacksAfterWorkersExit();
            _stop.Dispose();
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var item in _queue.ReadAllAsync(_stop.Token))
            {
                // Channel 在取消与已缓冲数据竞态时仍可能交付一项；超时后不再启动新工作。
                if (_stop.IsCancellationRequested)
                {
                    _queue.MarkAbandoned(item.Id);
                    break;
                }

                _queue.MarkRunning(item.Id);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _stop.Token, item.CancellationToken);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    await item.InvokeAsync(linked.Token);
                    Interlocked.Increment(ref _succeeded);
                }
                catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _cancelled);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _failed);
                    lock (_failureGate)
                    {
                        if (_failures.Count == _maximumFailureRecords)
                        {
                            _failures.RemoveAt(0);
                        }

                        _failures.Add(new WorkerFailure(item.Id));
                    }
                }
                finally
                {
                    _queue.MarkCompleted(item.Id);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // 关停期限已到；尚未领取的工作仍保留在有界队列和 Outstanding 快照。
        }
    }
}
