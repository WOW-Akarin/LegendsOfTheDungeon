namespace LegendsOfTheDungeon.Platform.Concurrency;

/// <summary>逻辑调度的接纳结果。</summary>
public enum ScheduleOfferStatus
{
    /// <summary>已分配稳定调度编号。</summary>
    Accepted,
    /// <summary>队列容量已满。</summary>
    Busy,
    /// <summary>已经停止接纳调度。</summary>
    Closed
}

/// <summary>调度接纳回执，未接纳时编号为零。</summary>
public sealed class ScheduleOffer
{
    internal ScheduleOffer(ScheduleOfferStatus status, long id)
    {
        Status = status;
        Id = id;
    }

    /// <summary>调度接纳状态。</summary>
    public ScheduleOfferStatus Status { get; }

    /// <summary>成功接纳后唯一、单调递增的编号。</summary>
    public long Id { get; }
}

/// <summary>已到期且按逻辑 Tick 与接纳顺序稳定排列的信封。</summary>
/// <typeparam name="T">不可变任务描述；调度器不执行它。</typeparam>
public sealed class ScheduledDelivery<T>
{
    internal ScheduledDelivery(long id, long dueTick, T payload)
    {
        Id = id;
        DueTick = dueTick;
        Payload = payload;
    }

    /// <summary>接纳编号，用于取消与回放。</summary>
    public long Id { get; }

    /// <summary>到期逻辑 Tick，而非系统墙钟时间。</summary>
    public long DueTick { get; }

    /// <summary>任务描述；是否允许修改世界状态由 WorldExecutor 决定。</summary>
    public T Payload { get; }
}

/// <summary>只存信封、不启动系统 Timer 的有界逻辑调度器。</summary>
/// <typeparam name="T">可在 Tick 边界消费的不可变描述。</typeparam>
public sealed class LogicalScheduler<T>
{
    private readonly object _gate = new();
    private readonly SortedDictionary<(long Tick, long Id), ScheduledDelivery<T>> _entries = new();
    private readonly Dictionary<long, (long Tick, long Id)> _index = new();
    private readonly int _capacity;
    private long _nextId;
    private bool _closed;

    /// <summary>设置所有 Tick 共同使用的最大发送积压容量。</summary>
    /// <param name="capacity">正数容量。</param>
    public LogicalScheduler(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>当前尚未取消或派发的到期任务数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>按逻辑 Tick 接纳一次性事件；不会在背景线程运行任务。</summary>
    /// <param name="dueTick">非负逻辑 Tick。</param>
    /// <param name="payload">不可变描述。</param>
    /// <returns>接纳状态与单调编号。</returns>
    public ScheduleOffer Schedule(long dueTick, T payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dueTick);
        ArgumentNullException.ThrowIfNull(payload);
        lock (_gate)
        {
            if (_closed)
            {
                return new ScheduleOffer(ScheduleOfferStatus.Closed, 0);
            }

            if (_entries.Count >= _capacity)
            {
                return new ScheduleOffer(ScheduleOfferStatus.Busy, 0);
            }

            var id = checked(++_nextId);
            var key = (dueTick, id);
            _entries.Add(key, new ScheduledDelivery<T>(id, dueTick, payload));
            _index.Add(id, key);
            return new ScheduleOffer(ScheduleOfferStatus.Accepted, id);
        }
    }

    /// <summary>撤销尚未发出的任务并立即释放容量。</summary>
    /// <param name="id">已接纳的调度编号。</param>
    /// <returns>是否成功取消。</returns>
    public bool Cancel(long id)
    {
        lock (_gate)
        {
            if (!_index.Remove(id, out var key))
            {
                return false;
            }

            return _entries.Remove(key);
        }
    }

    /// <summary>只取到期且不超过单次预算的任务，保持 Tick/接纳编号排序。</summary>
    /// <param name="currentTick">当前非负逻辑 Tick。</param>
    /// <param name="maximumItems">本轮的派发上限。</param>
    /// <returns>只读稳定顺序的任务快照。</returns>
    public IReadOnlyList<ScheduledDelivery<T>> DrainDue(long currentTick, int maximumItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        var result = new List<ScheduledDelivery<T>>();
        lock (_gate)
        {
            while (result.Count < maximumItems && _entries.Count > 0)
            {
                var first = _entries.First();
                if (first.Key.Tick > currentTick)
                {
                    break;
                }

                _entries.Remove(first.Key);
                _index.Remove(first.Value.Id);
                result.Add(first.Value);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>停止新调度，保留已接纳事件供世界按逻辑 Tick 排空。</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }
}
