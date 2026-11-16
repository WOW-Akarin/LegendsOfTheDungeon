namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示消息契约版本；版本从 1 开始，default/0 为未初始化。
/// </summary>
public readonly record struct MessageVersion
{
    /// <summary>
    /// 创建正整数消息版本。
    /// </summary>
    /// <param name="value">版本号，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">版本为 0 时抛出。</exception>
    public MessageVersion(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    /// <summary>
    /// 获取版本号。
    /// </summary>
    public uint Value { get; }

    /// <summary>
    /// 获取当前值是否为 default/未初始化。
    /// </summary>
    public bool IsEmpty => Value == 0;
}

/// <summary>
/// 表示严格正整数领域事件序号；由 DeferredEventQueue 按插入顺序分配。
/// </summary>
public readonly record struct EventSequence
{
    /// <summary>
    /// 创建领域事件序号。
    /// </summary>
    /// <param name="value">序号，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">序号为 0 时抛出。</exception>
    public EventSequence(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    /// <summary>
    /// 获取事件序号。
    /// </summary>
    public ulong Value { get; }
}

/// <summary>
/// 标记返回 <typeparamref name="TResult"/> 的 Command 消息。
/// </summary>
/// <typeparam name="TResult">Command 成功结果类型。</typeparam>
public interface ICommand<TResult>
{
    /// <summary>
    /// 获取调用边界提供的稳定 Command 标识。
    /// </summary>
    CommandId CommandId { get; }

    /// <summary>
    /// 获取消息契约版本。
    /// </summary>
    MessageVersion Version { get; }
}

/// <summary>
/// 标记 Query 返回的只读快照；实现必须与可变领域状态断开。
/// </summary>
public interface IQuerySnapshot
{
}

/// <summary>
/// 标记返回只读快照的 Query 消息。
/// </summary>
/// <typeparam name="TResult">只读 Query Snapshot 类型。</typeparam>
public interface IQuery<TResult>
    where TResult : IQuerySnapshot
{
    /// <summary>
    /// 获取消息契约版本。
    /// </summary>
    MessageVersion Version { get; }
}

/// <summary>
/// 领域事件的不可变语义基类；具体事件应只携带已经发生的事实数据。
/// </summary>
public abstract record DomainEvent
{
    /// <summary>
    /// 创建指定契约版本的领域事件。
    /// </summary>
    /// <param name="version">事件契约版本，不能为 default。</param>
    /// <exception cref="ArgumentOutOfRangeException">版本为 default/0 时抛出。</exception>
    protected DomainEvent(MessageVersion version)
    {
        if (version.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "DomainEvent 版本不能为 default/0。");
        }

        Version = version;
    }

    /// <summary>
    /// 获取事件契约版本。
    /// </summary>
    public MessageVersion Version { get; }
}
