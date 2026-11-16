namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 提供核心逻辑计时所需的 Tick 和单调经过时间；不暴露墙上 UTC。
/// </summary>
public interface IClock
{
    /// <summary>
    /// 获取当前逻辑 Tick。
    /// </summary>
    Tick CurrentTick { get; }

    /// <summary>
    /// 获取固定 Tick 时长。
    /// </summary>
    Duration TickDuration { get; }

    /// <summary>
    /// 获取从时钟起点累计的单调经过时间。
    /// </summary>
    Duration MonotonicElapsed { get; }
}

/// <summary>
/// 提供审计、运维和离线规则使用的 UTC 墙上时间；不得用于核心 Timer 到期判断。
/// </summary>
public interface IAuditClock
{
    /// <summary>
    /// 获取规范化为 UTC 的当前墙上时间。
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// 提供完全由调用方推进的测试/模拟时钟，不读取任何真实系统时间。
/// </summary>
public sealed class FakeClock : IClock, IAuditClock
{
    /// <summary>
    /// 创建从 Tick 0、单调经过时间 0 开始的可控时钟。
    /// </summary>
    /// <param name="tickDuration">固定 Tick 时长，必须大于 0。</param>
    /// <param name="initialUtc">初始审计时间；会规范化为 UTC。</param>
    /// <exception cref="ArgumentOutOfRangeException">Tick 时长为 0 时抛出。</exception>
    public FakeClock(Duration tickDuration, DateTimeOffset initialUtc)
        : this(tickDuration, initialUtc, default, default)
    {
    }

    /// <summary>
    /// 创建具有显式初始 Tick 与单调时间的可控时钟。
    /// </summary>
    /// <param name="tickDuration">固定 Tick 时长，必须大于 0。</param>
    /// <param name="initialUtc">初始审计时间；会规范化为 UTC。</param>
    /// <param name="initialTick">初始逻辑 Tick。</param>
    /// <param name="initialMonotonicElapsed">初始单调经过时间。</param>
    /// <exception cref="ArgumentOutOfRangeException">Tick 时长为 0 时抛出。</exception>
    public FakeClock(
        Duration tickDuration,
        DateTimeOffset initialUtc,
        Tick initialTick,
        Duration initialMonotonicElapsed)
    {
        if (tickDuration.IsZero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tickDuration),
                "FakeClock 的 TickDuration 必须大于 0。");
        }

        TickDuration = tickDuration;
        CurrentTick = initialTick;
        MonotonicElapsed = initialMonotonicElapsed;
        UtcNow = initialUtc.ToUniversalTime();
    }

    /// <inheritdoc />
    public Tick CurrentTick { get; private set; }

    /// <inheritdoc />
    public Duration TickDuration { get; }

    /// <inheritdoc />
    public Duration MonotonicElapsed { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>
    /// 同步推进逻辑 Tick 和单调经过时间；操作在溢出时保持原状态不变。
    /// </summary>
    /// <param name="count">要推进的 Tick 数。</param>
    /// <exception cref="OverflowException">Tick 或单调经过时间推进溢出时抛出。</exception>
    public void AdvanceTicks(ulong count)
    {
        var nextTick = CurrentTick.Add(count);
        var elapsedDelta = TickDuration.Multiply(count);
        var nextElapsed = MonotonicElapsed.Add(elapsedDelta);

        CurrentTick = nextTick;
        MonotonicElapsed = nextElapsed;
    }

    /// <summary>
    /// 独立设置审计 UTC，可用于模拟墙钟向前或向后调整；不会改变 Tick 或单调时间。
    /// </summary>
    /// <param name="utc">新的墙上时间；任何 Offset 都会规范化为 UTC。</param>
    public void SetUtc(DateTimeOffset utc)
    {
        UtcNow = utc.ToUniversalTime();
    }
}
