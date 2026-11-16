namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示从 0 开始的无符号整数逻辑 Tick；0 是合法初始 Tick。
/// </summary>
public readonly record struct Tick : IComparable<Tick>
{
    /// <summary>
    /// 创建逻辑 Tick。
    /// </summary>
    /// <param name="value">无符号逻辑 Tick 序号。</param>
    public Tick(ulong value)
    {
        Value = value;
    }

    /// <summary>
    /// 获取逻辑 Tick 序号。
    /// </summary>
    public ulong Value { get; }

    /// <summary>
    /// 推进指定数量的 Tick。
    /// </summary>
    /// <param name="count">要推进的 Tick 数。</param>
    /// <returns>推进后的 Tick。</returns>
    /// <exception cref="OverflowException">结果超过 UInt64 范围时抛出。</exception>
    public Tick Add(ulong count)
    {
        if (count > ulong.MaxValue - Value)
        {
            throw new OverflowException("逻辑 Tick 推进发生溢出。");
        }

        return new Tick(Value + count);
    }

    /// <summary>
    /// 按序号比较两个逻辑 Tick。
    /// </summary>
    /// <param name="other">待比较 Tick。</param>
    /// <returns>小于、等于或大于零的比较结果。</returns>
    public int CompareTo(Tick other)
    {
        return Value.CompareTo(other.Value);
    }
}
