namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示基于逻辑 Tick 的一次性定时器值；不创建线程或系统 Timer。
/// </summary>
public readonly record struct LogicalTimer
{
    private const byte ActiveState = 1;
    private const byte CancelledState = 2;

    private readonly byte _state;
    private readonly Tick _dueTick;

    private LogicalTimer(Tick dueTick, byte state)
    {
        _dueTick = dueTick;
        _state = state;
    }

    /// <summary>
    /// 获取到期 Tick；未初始化 Timer 不允许读取。
    /// </summary>
    /// <exception cref="InvalidOperationException">Timer 为 default/未初始化时抛出。</exception>
    public Tick DueTick
    {
        get
        {
            EnsureInitialized();
            return _dueTick;
        }
    }

    /// <summary>
    /// 获取 Timer 是否已取消；未初始化 Timer 不允许读取。
    /// </summary>
    /// <exception cref="InvalidOperationException">Timer 为 default/未初始化时抛出。</exception>
    public bool IsCancelled
    {
        get
        {
            EnsureInitialized();
            return _state == CancelledState;
        }
    }

    /// <summary>
    /// 从时钟当前 Tick 启动一次性逻辑 Timer。
    /// </summary>
    /// <param name="clock">提供当前 Tick 与固定 TickDuration 的逻辑时钟。</param>
    /// <param name="timeout">非负超时时长；0 在当前 Tick 到期，正时长向上取整到 Tick 边界。</param>
    /// <returns>活动的逻辑 Timer。</returns>
    /// <exception cref="ArgumentNullException">时钟为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">时钟报告 0 TickDuration 时抛出。</exception>
    /// <exception cref="OverflowException">计算到期 Tick 发生溢出时抛出。</exception>
    public static LogicalTimer Start(IClock clock, Duration timeout)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var tickMilliseconds = clock.TickDuration.Milliseconds;
        if (tickMilliseconds == 0)
        {
            throw new InvalidOperationException("IClock.TickDuration 必须大于 0。");
        }

        var fullTicks = timeout.Milliseconds / tickMilliseconds;
        var hasRemainder = timeout.Milliseconds % tickMilliseconds != 0;
        var ticksToWait = (ulong)fullTicks + (hasRemainder ? 1UL : 0UL);
        var dueTick = clock.CurrentTick.Add(ticksToWait);

        return new LogicalTimer(dueTick, ActiveState);
    }

    /// <summary>
    /// 判断 Timer 在当前逻辑 Tick 是否已经到期。
    /// </summary>
    /// <param name="clock">提供当前 Tick 的逻辑时钟。</param>
    /// <returns>活动 Timer 当前 Tick 大于等于 DueTick 时返回 true；已取消时始终返回 false。</returns>
    /// <exception cref="ArgumentNullException">时钟为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">Timer 为 default/未初始化时抛出。</exception>
    public bool IsDue(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureInitialized();

        return _state == ActiveState
            && clock.CurrentTick.Value >= _dueTick.Value;
    }

    /// <summary>
    /// 返回取消后的 Timer；重复取消保持取消状态。
    /// </summary>
    /// <returns>取消状态的逻辑 Timer。</returns>
    /// <exception cref="InvalidOperationException">Timer 为 default/未初始化时抛出。</exception>
    public LogicalTimer Cancel()
    {
        EnsureInitialized();

        return _state == CancelledState
            ? this
            : new LogicalTimer(_dueTick, CancelledState);
    }

    private void EnsureInitialized()
    {
        if (_state == 0)
        {
            throw new InvalidOperationException("LogicalTimer 未初始化。");
        }
    }
}
