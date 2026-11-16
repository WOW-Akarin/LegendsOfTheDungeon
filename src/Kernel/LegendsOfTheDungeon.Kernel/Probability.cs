namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示量化到 2^-32 固定精度的概率值，范围为 [0,1]。
/// </summary>
public readonly record struct Probability
{
    /// <summary>
    /// 单位区间被划分的固定单位数。
    /// </summary>
    public const ulong Resolution = 1UL << 32;

    private readonly ulong _units;

    /// <summary>
    /// 从 decimal 概率创建值；按 nearest-even 量化到 2^-32 精度。
    /// </summary>
    /// <param name="value">必须位于 [0,1] 的概率。</param>
    /// <exception cref="ArgumentOutOfRangeException">输入小于 0 或大于 1 时抛出。</exception>
    public Probability(decimal value)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Probability 必须位于 [0,1]。");
        }

        var scaled = value * Resolution;
        _units = (ulong)decimal.Round(
            scaled,
            0,
            MidpointRounding.ToEven);
    }

    /// <summary>
    /// 获取量化后的固定单位数，范围为 [0, 2^32]。
    /// </summary>
    public ulong Units => _units;

    /// <summary>
    /// 获取量化后的 decimal 概率值。
    /// </summary>
    public decimal Value => (decimal)_units / Resolution;

    /// <summary>
    /// 获取概率是否恒为 0。
    /// </summary>
    public bool IsImpossible => _units == 0;

    /// <summary>
    /// 获取概率是否恒为 1。
    /// </summary>
    public bool IsCertain => _units == Resolution;

    /// <summary>
    /// 使用注入的随机源进行一次概率判定；0/1 边界不会消耗随机状态。
    /// </summary>
    /// <param name="random">随机源。</param>
    /// <returns>本次事件是否发生。</returns>
    /// <exception cref="ArgumentNullException">随机源为 null 时抛出。</exception>
    public bool Roll(IRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (IsImpossible)
        {
            return false;
        }

        if (IsCertain)
        {
            return true;
        }

        return random.NextUInt64(Resolution) < _units;
    }
}
