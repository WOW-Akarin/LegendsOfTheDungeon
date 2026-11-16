using System.Threading.Channels;

namespace LegendsOfTheDungeon.Platform.Concurrency;

/// <summary>有界任务队列的接纳结果；接纳不意味着任务已经执行完毕。</summary>
public enum WorkOfferStatus
{
    /// <summary>已接纳，任务会在独立 Worker 上处理。</summary>
    Accepted,
    /// <summary>等待队列已满，调用方应执行背压策略。</summary>
    Busy,
    /// <summary>不再接纳任务。</summary>
    Closed,
    /// <summary>相同任务编号还在等待或执行。</summary>
    Duplicate
}

/// <summary>已经接纳但尚未完成的任务执行阶段。</summary>
public enum OutstandingWorkState
{
    /// <summary>仍在有界队列内。</summary>
    Queued,
    /// <summary>已被 Worker 领取且正在处理。</summary>
    Running
}

/// <summary>关闭时仍未完成的一项稳定任务标识和阶段。</summary>
public sealed class OutstandingWork
{
    internal OutstandingWork(long id, OutstandingWorkState state)
    {
        Id = id;
        State = state;
    }

    /// <summary>调用方分配的任务编号。</summary>
    public long Id { get; }

    /// <summary>任务正在等待或执行。</summary>
    public OutstandingWorkState State { get; }
}

/// <summary>独立于 World 状态的异步后台技术任务。</summary>
public sealed class BackgroundWorkItem
{
    private readonly Func<CancellationToken, ValueTask> _execute;

    /// <summary>创建有稳定 ID 的任务描述，不允许持有世界可变状态写权限。</summary>
    /// <param name="id">正数的调用方任务编号。</param>
    /// <param name="execute">后台异步操作；不得直接修改世界状态。</param>
    /// <param name="cancellationToken">本任务独立取消信号。</param>
    public BackgroundWorkItem(
        long id, Func<CancellationToken, ValueTask> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        Id = id;
        CancellationToken = cancellationToken;
    }

    /// <summary>稳定任务编号。</summary>
    public long Id { get; }

    /// <summary>本任务取消信号。</summary>
    public CancellationToken CancellationToken { get; }

    internal ValueTask InvokeAsync(CancellationToken token) => _execute(token);
}

/// <summary>线程安全有界任务队列，保留关闭时所有未确认完成的任务标识。</summary>
public sealed class BoundedWorkQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<long, OutstandingWorkState> _outstanding = new();
    private readonly Channel<BackgroundWorkItem> _channel;
    private bool _closed;

    /// <summary>设置队列等待容量；执行中的任务由固定 Worker 数额外限制。</summary>
    /// <param name="capacity">等待队列的正数上限。</param>
    public BoundedWorkQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _channel = Channel.CreateBounded<BackgroundWorkItem>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
    }

    /// <summary>尝试立即接纳任务，不等待容量、不静默丢弃。</summary>
    /// <param name="item">带唯一 ID 的任务。</param>
    /// <returns>接纳、忙、关闭或任务 ID 重复。</returns>
    public WorkOfferStatus Offer(BackgroundWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            if (_closed)
            {
                return WorkOfferStatus.Closed;
            }

            if (_outstanding.ContainsKey(item.Id))
            {
                return WorkOfferStatus.Duplicate;
            }

            if (!_channel.Writer.TryWrite(item))
            {
                return WorkOfferStatus.Busy;
            }

            _outstanding.Add(item.Id, OutstandingWorkState.Queued);
            return WorkOfferStatus.Accepted;
        }
    }

    /// <summary>停止接纳新任务，但允许后台消费者排空既有任务。</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _closed = true;
            _channel.Writer.TryComplete();
        }
    }

    /// <summary>按照稳定任务编号列出尚未完成的任务，供恢复与关闭报告使用。</summary>
    /// <returns>只读状态快照。</returns>
    public IReadOnlyList<OutstandingWork> SnapshotOutstanding()
    {
        lock (_gate)
        {
            return _outstanding
                .OrderBy(static item => item.Key)
                .Select(static item => new OutstandingWork(item.Key, item.Value))
                .ToArray();
        }
    }

    internal IAsyncEnumerable<BackgroundWorkItem> ReadAllAsync(CancellationToken token) =>
        _channel.Reader.ReadAllAsync(token);

    internal void MarkRunning(long id)
    {
        lock (_gate)
        {
            _outstanding[id] = OutstandingWorkState.Running;
        }
    }

    internal void MarkCompleted(long id)
    {
        lock (_gate)
        {
            _outstanding.Remove(id);
        }
    }
}
