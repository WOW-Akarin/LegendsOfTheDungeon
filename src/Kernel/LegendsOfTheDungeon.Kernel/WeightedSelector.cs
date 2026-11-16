namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示具有严格正整数权重的候选项。
/// </summary>
/// <typeparam name="T">候选值类型。</typeparam>
public readonly record struct WeightedOption<T>
{
    /// <summary>
    /// 创建加权候选项。
    /// </summary>
    /// <param name="item">候选值。</param>
    /// <param name="weight">严格大于 0 的整数权重。</param>
    /// <exception cref="ArgumentOutOfRangeException">权重为 0 时抛出。</exception>
    public WeightedOption(T item, ulong weight)
    {
        if (weight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                weight,
                "WeightedOption 权重必须大于 0。");
        }

        Item = item;
        Weight = weight;
    }

    /// <summary>
    /// 获取候选值。
    /// </summary>
    public T Item { get; }

    /// <summary>
    /// 获取严格正整数权重。
    /// </summary>
    public ulong Weight { get; }
}

/// <summary>
/// 提供基于正整数权重的确定性选择算法。
/// </summary>
public static class WeightedSelector
{
    /// <summary>
    /// 按权重从候选项中选择一个值。
    /// </summary>
    /// <typeparam name="T">候选值类型。</typeparam>
    /// <param name="random">注入的随机源。</param>
    /// <param name="options">非空候选集合；每项权重必须大于 0。</param>
    /// <returns>被选中的候选值。</returns>
    /// <exception cref="ArgumentNullException">随机源或候选集合为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">候选集合为空或包含 default/零权重项时抛出。</exception>
    /// <exception cref="OverflowException">总权重超过 UInt64 范围时抛出。</exception>
    public static T Select<T>(
        IRandom random,
        IReadOnlyList<WeightedOption<T>> options)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Count == 0)
        {
            throw new ArgumentException("WeightedSelector 候选集合不能为空。", nameof(options));
        }

        var snapshot = options.ToArray();
        ulong totalWeight = 0;

        foreach (var option in snapshot)
        {
            if (option.Weight == 0)
            {
                throw new ArgumentException(
                    "WeightedSelector 不接受 default/零权重候选项。",
                    nameof(options));
            }

            if (option.Weight > ulong.MaxValue - totalWeight)
            {
                throw new OverflowException("WeightedSelector 总权重发生 UInt64 溢出。");
            }

            totalWeight += option.Weight;
        }

        var roll = random.NextUInt64(totalWeight);
        ulong cumulative = 0;

        foreach (var option in snapshot)
        {
            cumulative += option.Weight;
            if (roll < cumulative)
            {
                return option.Item;
            }
        }

        throw new InvalidOperationException("WeightedSelector 内部累计权重与随机上界不一致。");
    }
}
