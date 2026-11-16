namespace LegendsOfTheDungeon.Platform.Concurrency;

/// <summary>普通输入或需要独立预留容量的关键回执。</summary>
public enum MailboxLane
{
    /// <summary>普通命令及可延后输入。</summary>
    Normal,
    /// <summary>DB 回执及有恢复意义的控制通知。</summary>
    Critical
}

/// <summary>将消息接纳进有界 Mailbox 的结果；接纳不代表业务执行或提交。</summary>
public enum MailboxOfferResult
{
    /// <summary>已进入相应车道。</summary>
    Accepted,
    /// <summary>该车道容量已满，消息没有被接纳。</summary>
    Busy,
    /// <summary>Mailbox 停止接纳新消息。</summary>
    Closed
}

/// <summary>从有界 Mailbox 按稳定顺序交付的一条消息。</summary>
/// <typeparam name="T">消息负载。</typeparam>
public sealed class MailboxDelivery<T>
{
    internal MailboxDelivery(MailboxLane lane, T value)
    {
        Lane = lane;
        Value = value;
    }

    /// <summary>获取原始车道。</summary>
    public MailboxLane Lane { get; }

    /// <summary>获取被接纳的消息；真正的所有权和授权留给消费者验证。</summary>
    public T Value { get; }
}

/// <summary>线程安全的双车道有界队列；关键回执容量不被普通流量使用。</summary>
/// <typeparam name="T">跨线程传递的不可变请求或结果类型。</typeparam>
public sealed class BoundedMailbox<T>
{
    private readonly object _gate = new();
    private readonly Queue<T> _normal = new();
    private readonly Queue<T> _critical = new();
    private readonly int _normalCapacity;
    private readonly int _criticalCapacity;
    private readonly int _criticalBurst;
    private int _consecutiveCritical;
    private bool _closed;

    /// <summary>创建具有独立容量和抗饥饿配额的技术 Mailbox。</summary>
    /// <param name="normalCapacity">普通队列最大数量。</param>
    /// <param name="criticalCapacity">关键回执独立预留数量。</param>
    /// <param name="maximumCriticalBurst">两车道均非空时，连续优先处理关键回执的最大次数。</param>
    public BoundedMailbox(int normalCapacity, int criticalCapacity, int maximumCriticalBurst)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(criticalCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCriticalBurst);
        _normalCapacity = normalCapacity;
        _criticalCapacity = criticalCapacity;
        _criticalBurst = maximumCriticalBurst;
    }

    /// <summary>当前两车道待处理总数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _normal.Count + _critical.Count;
            }
        }
    }

    /// <summary>仅接纳未关闭且相应车道有剩余容量的消息。</summary>
    /// <param name="value">已验证且不可变的请求快照或回执。</param>
    /// <param name="lane">独立容量的车道。</param>
    /// <returns>接纳、满队列或已关闭。</returns>
    public MailboxOfferResult Offer(T value, MailboxLane lane)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(lane))
        {
            throw new ArgumentOutOfRangeException(nameof(lane));
        }

        lock (_gate)
        {
            if (_closed)
            {
                return MailboxOfferResult.Closed;
            }

            var queue = lane == MailboxLane.Critical ? _critical : _normal;
            var capacity = lane == MailboxLane.Critical ? _criticalCapacity : _normalCapacity;
            if (queue.Count >= capacity)
            {
                return MailboxOfferResult.Busy;
            }

            queue.Enqueue(value);
            return MailboxOfferResult.Accepted;
        }
    }

    /// <summary>每次最多取出指定配额；关键优先且保证普通输入不会持续饥饿。</summary>
    /// <param name="maximumItems">本轮最多派发数。</param>
    /// <returns>只读、按消费顺序排列的消息快照。</returns>
    public IReadOnlyList<MailboxDelivery<T>> Drain(int maximumItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        var delivered = new List<MailboxDelivery<T>>();
        lock (_gate)
        {
            while (delivered.Count < maximumItems && (_normal.Count != 0 || _critical.Count != 0))
            {
                var critical = _critical.Count != 0
                    && (_normal.Count == 0 || _consecutiveCritical < _criticalBurst);
                if (critical)
                {
                    delivered.Add(new MailboxDelivery<T>(MailboxLane.Critical, _critical.Dequeue()));
                    _consecutiveCritical++;
                }
                else
                {
                    delivered.Add(new MailboxDelivery<T>(MailboxLane.Normal, _normal.Dequeue()));
                    _consecutiveCritical = 0;
                }
            }
        }

        return delivered.AsReadOnly();
    }

    /// <summary>停止接纳新消息，但允许消费已经接纳的普通消息与关键回执。</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }
}
