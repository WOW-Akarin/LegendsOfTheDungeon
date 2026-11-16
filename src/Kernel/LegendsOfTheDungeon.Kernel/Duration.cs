namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示非负整数毫秒持续时间；零时长合法。
/// </summary>
public readonly record struct Duration : IComparable<Duration>
{
    internal const long MaximumMilliseconds =
        TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// 创建非负、可精确转换为 TimeSpan 的整数毫秒持续时间。
    /// </summary>
    /// <param name="milliseconds">持续时间毫秒数。</param>
    /// <exception cref="ArgumentOutOfRangeException">毫秒数为负数或超过支持上限时抛出。</exception>
    public Duration(long milliseconds)
    {
        if (milliseconds < 0 || milliseconds > MaximumMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds),
                milliseconds,
                "Duration 必须是非负且可转换为 TimeSpan 的整数毫秒。");
        }

        Milliseconds = milliseconds;
    }

    /// <summary>
    /// 获取持续时间的整数毫秒数。
    /// </summary>
    public long Milliseconds { get; }

    /// <summary>
    /// 获取当前持续时间是否为零。
    /// </summary>
    public bool IsZero => Milliseconds == 0;

    /// <summary>
    /// 从非负整数秒创建持续时间。
    /// </summary>
    /// <param name="seconds">秒数。</param>
    /// <returns>等价持续时间。</returns>
    /// <exception cref="ArgumentOutOfRangeException">秒数为负数时抛出。</exception>
    /// <exception cref="OverflowException">换算后的毫秒数超过支持范围时抛出。</exception>
    public static Duration FromSeconds(long seconds)
    {
        if (seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "秒数不能为负数。");
        }

        if (seconds > MaximumMilliseconds / 1000)
        {
            throw new OverflowException("秒数换算为 Duration 后超出支持范围。");
        }

        return new Duration(seconds * 1000);
    }

    /// <summary>
    /// 返回两个持续时间的和。
    /// </summary>
    /// <param name="other">待相加持续时间。</param>
    /// <returns>相加后的持续时间。</returns>
    /// <exception cref="OverflowException">结果超过 Duration 支持范围时抛出。</exception>
    public Duration Add(Duration other)
    {
        if (other.Milliseconds > MaximumMilliseconds - Milliseconds)
        {
            throw new OverflowException("Duration 加法超出支持范围。");
        }

        return new Duration(Milliseconds + other.Milliseconds);
    }

    /// <summary>
    /// 按无符号整数因子放大持续时间。
    /// </summary>
    /// <param name="factor">非负整数倍数。</param>
    /// <returns>缩放后的持续时间。</returns>
    /// <exception cref="OverflowException">结果超过 Duration 支持范围时抛出。</exception>
    public Duration Multiply(ulong factor)
    {
        if (factor == 0 || Milliseconds == 0)
        {
            return default;
        }

        if (factor > (ulong)(MaximumMilliseconds / Milliseconds))
        {
            throw new OverflowException("Duration 乘法超出支持范围。");
        }

        return new Duration((long)((ulong)Milliseconds * factor));
    }

    /// <summary>
    /// 精确转换为 TimeSpan。
    /// </summary>
    /// <returns>与当前整数毫秒完全等价的 TimeSpan。</returns>
    public TimeSpan ToTimeSpan()
    {
        return TimeSpan.FromTicks(Milliseconds * TimeSpan.TicksPerMillisecond);
    }

    /// <summary>
    /// 按毫秒数比较两个持续时间。
    /// </summary>
    /// <param name="other">待比较持续时间。</param>
    /// <returns>小于、等于或大于零的比较结果。</returns>
    public int CompareTo(Duration other)
    {
        return Milliseconds.CompareTo(other.Milliseconds);
    }
}
