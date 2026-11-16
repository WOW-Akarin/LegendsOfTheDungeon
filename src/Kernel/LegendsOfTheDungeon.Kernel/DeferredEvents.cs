using System.Collections.ObjectModel;

namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示一次 Command 延迟发布的领域事件及其因果元数据。
/// </summary>
public sealed class DeferredEventEnvelope
{
    internal DeferredEventEnvelope(
        object queueToken,
        CommandId commandId,
        EventSequence sequence,
        EventSequence? parentSequence,
        int depth,
        DomainEvent domainEvent)
    {
        QueueToken = queueToken;
        CommandId = commandId;
        Sequence = sequence;
        ParentSequence = parentSequence;
        Depth = depth;
        DomainEvent = domainEvent;
    }

    internal object QueueToken { get; }

    /// <summary>
    /// 获取产生该事件的根 Command 标识。
    /// </summary>
    public CommandId CommandId { get; }

    /// <summary>
    /// 获取本 Command 内按插入顺序递增的事件序号。
    /// </summary>
    public EventSequence Sequence { get; }

    /// <summary>
    /// 获取直接父事件序号；根事件为 null。
    /// </summary>
    public EventSequence? ParentSequence { get; }

    /// <summary>
    /// 获取派生深度；根事件为 0。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// 获取领域事件事实。
    /// </summary>
    public DomainEvent DomainEvent { get; }
}

/// <summary>
/// 表示 Command 成功提交后可发布的延迟事件不可变快照。
/// </summary>
public sealed class DeferredEventBatch
{
    private readonly ReadOnlyCollection<DeferredEventEnvelope> _events;

    internal DeferredEventBatch(
        CommandId commandId,
        IReadOnlyList<DeferredEventEnvelope> events)
    {
        CommandId = commandId;
        _events = Array.AsReadOnly(events.ToArray());
    }

    /// <summary>
    /// 获取产生该批事件的 Command 标识。
    /// </summary>
    public CommandId CommandId { get; }

    /// <summary>
    /// 获取按 EventSequence 升序排列的只读事件集合。
    /// </summary>
    public IReadOnlyList<DeferredEventEnvelope> Events => _events;

    /// <summary>
    /// 获取事件批是否为空。
    /// </summary>
    public bool IsEmpty => _events.Count == 0;
}

/// <summary>
/// 收集单次 Command 提交后发布的领域事件，并强制数量、深度和因果边界；路线图中的 DeferredEventQueue 概念在 C# API 中命名为 DeferredEventBuffer。
/// </summary>
public sealed class DeferredEventBuffer
{
    private readonly object _queueToken = new();
    private readonly List<DeferredEventEnvelope> _events = [];
    private bool _completed;

    /// <summary>
    /// 创建绑定单个 Command 的延迟事件队列。
    /// </summary>
    /// <param name="commandId">当前 Command 的非零标识。</param>
    /// <param name="maximumEvents">整个派生树允许的最大事件数量，必须大于 0。</param>
    /// <param name="maximumDepth">允许的最大派生深度；根事件深度为 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">CommandId 为空、事件上限小于 1 或深度小于 0 时抛出。</exception>
    public DeferredEventBuffer(
        CommandId commandId,
        int maximumEvents,
        int maximumDepth)
    {
        if (commandId.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandId),
                "DeferredEventBuffer 的 CommandId 不能为 default/empty。");
        }

        if (maximumEvents < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEvents),
                maximumEvents,
                "maximumEvents 必须大于 0。");
        }

        if (maximumDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "maximumDepth 不能为负数。");
        }

        CommandId = commandId;
        MaximumEvents = maximumEvents;
        MaximumDepth = maximumDepth;
    }

    /// <summary>
    /// 获取当前队列绑定的 Command 标识。
    /// </summary>
    public CommandId CommandId { get; }

    /// <summary>
    /// 获取最大事件数量。
    /// </summary>
    public int MaximumEvents { get; }

    /// <summary>
    /// 获取最大派生深度。
    /// </summary>
    public int MaximumDepth { get; }

    /// <summary>
    /// 获取当前已排队事件数量。
    /// </summary>
    public int Count => _events.Count;

    /// <summary>
    /// 获取队列是否已经 Commit 或 Discard。
    /// </summary>
    public bool IsCompleted => _completed;

    /// <summary>
    /// 排入由当前 Command 直接产生的根领域事件。
    /// </summary>
    /// <param name="domainEvent">已经发生的领域事实。</param>
    /// <returns>带 Command 因果、序号和 depth=0 的 envelope。</returns>
    /// <exception cref="ArgumentNullException">事件为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">队列已封闭或数量达到上限时抛出。</exception>
    public DeferredEventEnvelope EnqueueRoot(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return Enqueue(domainEvent, null, 0);
    }

    /// <summary>
    /// 排入由当前队列中已有事件派生的新领域事件。
    /// </summary>
    /// <param name="parent">必须由当前队列真实产生的父 envelope。</param>
    /// <param name="domainEvent">派生的领域事实。</param>
    /// <returns>记录父序号和递增深度的新 envelope。</returns>
    /// <exception cref="ArgumentNullException">父 envelope 或事件为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">父 envelope 不属于当前队列时抛出。</exception>
    /// <exception cref="InvalidOperationException">队列已封闭、数量或深度达到上限时抛出。</exception>
    public DeferredEventEnvelope EnqueueDerived(
        DeferredEventEnvelope parent,
        DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (!ReferenceEquals(parent.QueueToken, _queueToken))
        {
            throw new ArgumentException(
                "派生事件 parent 必须由当前 DeferredEventBuffer 产生。",
                nameof(parent));
        }

        var depth = checked(parent.Depth + 1);
        return Enqueue(domainEvent, parent.Sequence, depth);
    }

    /// <summary>
    /// 将当前队列封闭并生成提交后可发布的不可变快照。
    /// </summary>
    /// <returns>按 EventSequence 排序的事件批。</returns>
    /// <exception cref="InvalidOperationException">队列已经 Commit 或 Discard 时抛出。</exception>
    public DeferredEventBatch Commit()
    {
        EnsureOpen();
        _completed = true;
        return new DeferredEventBatch(CommandId, _events);
    }

    /// <summary>
    /// 丢弃所有尚未发布的事件并封闭队列。
    /// </summary>
    /// <exception cref="InvalidOperationException">队列已经 Commit 或 Discard 时抛出。</exception>
    public void Discard()
    {
        EnsureOpen();
        _events.Clear();
        _completed = true;
    }

    private DeferredEventEnvelope Enqueue(
        DomainEvent domainEvent,
        EventSequence? parentSequence,
        int depth)
    {
        EnsureOpen();

        if (_events.Count >= MaximumEvents)
        {
            throw new InvalidOperationException(
                $"DeferredEventBuffer 已达到 maximumEvents={MaximumEvents}。");
        }

        if (depth > MaximumDepth)
        {
            throw new InvalidOperationException(
                $"DeferredEventBuffer 派生 depth={depth} 超过 maximumDepth={MaximumDepth}。");
        }

        var envelope = new DeferredEventEnvelope(
            _queueToken,
            CommandId,
            new EventSequence((ulong)_events.Count + 1),
            parentSequence,
            depth,
            domainEvent);

        _events.Add(envelope);
        return envelope;
    }

    private void EnsureOpen()
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "DeferredEventBuffer 已经 Commit 或 Discard，不能继续修改。");
        }
    }
}
