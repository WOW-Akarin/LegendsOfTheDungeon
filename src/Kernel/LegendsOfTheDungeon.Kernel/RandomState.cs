namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示可持久化的确定性随机算法状态；default 状态未初始化。
/// </summary>
public readonly record struct RandomState
{
    private readonly string? _algorithmId;

    /// <summary>
    /// 创建带算法身份、版本和四个状态字的随机状态。
    /// </summary>
    /// <param name="algorithmId">稳定算法标识。</param>
    /// <param name="algorithmVersion">正整数算法版本。</param>
    /// <param name="state0">第一个 64-bit 状态字。</param>
    /// <param name="state1">第二个 64-bit 状态字。</param>
    /// <param name="state2">第三个 64-bit 状态字。</param>
    /// <param name="state3">第四个 64-bit 状态字。</param>
    /// <exception cref="ArgumentException">算法标识为空白时抛出。</exception>
    /// <exception cref="ArgumentOutOfRangeException">算法版本小于 1 时抛出。</exception>
    public RandomState(
        string algorithmId,
        int algorithmVersion,
        ulong state0,
        ulong state1,
        ulong state2,
        ulong state3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmId);

        if (algorithmVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(algorithmVersion),
                algorithmVersion,
                "随机算法版本必须大于 0。");
        }

        _algorithmId = algorithmId;
        AlgorithmVersion = algorithmVersion;
        State0 = state0;
        State1 = state1;
        State2 = state2;
        State3 = state3;
    }

    /// <summary>
    /// 获取状态是否由有效构造器初始化。
    /// </summary>
    public bool IsInitialized => _algorithmId is not null;

    /// <summary>
    /// 获取稳定算法标识。
    /// </summary>
    /// <exception cref="InvalidOperationException">状态为 default/未初始化时抛出。</exception>
    public string AlgorithmId =>
        _algorithmId ?? throw new InvalidOperationException("RandomState 未初始化。");

    /// <summary>
    /// 获取算法版本。
    /// </summary>
    public int AlgorithmVersion { get; }

    /// <summary>
    /// 获取第一个 64-bit 状态字。
    /// </summary>
    public ulong State0 { get; }

    /// <summary>
    /// 获取第二个 64-bit 状态字。
    /// </summary>
    public ulong State1 { get; }

    /// <summary>
    /// 获取第三个 64-bit 状态字。
    /// </summary>
    public ulong State2 { get; }

    /// <summary>
    /// 获取第四个 64-bit 状态字。
    /// </summary>
    public ulong State3 { get; }

    internal bool IsAllZero =>
        State0 == 0
        && State1 == 0
        && State2 == 0
        && State3 == 0;
}
