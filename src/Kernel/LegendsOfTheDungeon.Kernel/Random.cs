using System.Numerics;

namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 提供确定性游戏模拟所需的原始随机抽样接口。
/// </summary>
public interface IRandom
{
    /// <summary>
    /// 返回下一个完整 64-bit 无符号随机值。
    /// </summary>
    /// <returns>下一个随机值。</returns>
    ulong NextUInt64();

    /// <summary>
    /// 返回 [0, exclusiveUpperBound) 内的无偏随机值。
    /// </summary>
    /// <param name="exclusiveUpperBound">严格大于 0 的排他上界。</param>
    /// <returns>小于上界的随机值。</returns>
    /// <exception cref="ArgumentOutOfRangeException">上界为 0 时抛出。</exception>
    ulong NextUInt64(ulong exclusiveUpperBound);
}

/// <summary>
/// 使用固定 xoshiro256** v1 算法的可保存状态确定性随机源；不提供密码学安全保证。
/// </summary>
public sealed class DeterministicRandom : IRandom
{
    /// <summary>
    /// 稳定算法标识；相同标识与版本不得改变序列语义。
    /// </summary>
    public const string AlgorithmId = "xoshiro256starstar";

    /// <summary>
    /// 当前固定算法版本。
    /// </summary>
    public const int AlgorithmVersion = 1;

    private ulong _state0;
    private ulong _state1;
    private ulong _state2;
    private ulong _state3;

    /// <summary>
    /// 使用 64-bit Seed 创建确定性随机源。
    /// </summary>
    /// <param name="seed">固定 Seed；0 也是合法 Seed。</param>
    public DeterministicRandom(ulong seed)
    {
        var splitMix = new SplitMix64(seed);
        _state0 = splitMix.Next();
        _state1 = splitMix.Next();
        _state2 = splitMix.Next();
        _state3 = splitMix.Next();
    }

    private DeterministicRandom(
        ulong state0,
        ulong state1,
        ulong state2,
        ulong state3)
    {
        _state0 = state0;
        _state1 = state1;
        _state2 = state2;
        _state3 = state3;
    }

    /// <summary>
    /// 从兼容的导出状态恢复随机源。
    /// </summary>
    /// <param name="state">此前导出的状态。</param>
    /// <returns>从该状态继续生成序列的随机源。</returns>
    /// <exception cref="ArgumentException">状态未初始化、算法/版本不兼容或四状态字全为 0 时抛出。</exception>
    public static DeterministicRandom FromState(RandomState state)
    {
        if (!state.IsInitialized)
        {
            throw new ArgumentException("RandomState 未初始化。", nameof(state));
        }

        if (!string.Equals(state.AlgorithmId, AlgorithmId, StringComparison.Ordinal)
            || state.AlgorithmVersion != AlgorithmVersion)
        {
            throw new ArgumentException("RandomState 的算法或版本与当前实现不兼容。", nameof(state));
        }

        if (state.IsAllZero)
        {
            throw new ArgumentException("xoshiro256** 的四状态字不能全部为 0。", nameof(state));
        }

        return new DeterministicRandom(
            state.State0,
            state.State1,
            state.State2,
            state.State3);
    }

    /// <summary>
    /// 导出可持久化并可精确恢复的当前随机状态。
    /// </summary>
    /// <returns>包含算法 ID、版本和四个状态字的快照。</returns>
    public RandomState ExportState()
    {
        return new RandomState(
            AlgorithmId,
            AlgorithmVersion,
            _state0,
            _state1,
            _state2,
            _state3);
    }

    /// <inheritdoc />
    public ulong NextUInt64()
    {
        unchecked
        {
            var result =
                BitOperations.RotateLeft(_state1 * 5, 7) * 9;
            var shifted = _state1 << 17;

            _state2 ^= _state0;
            _state3 ^= _state1;
            _state1 ^= _state2;
            _state0 ^= _state3;
            _state2 ^= shifted;
            _state3 = BitOperations.RotateLeft(_state3, 45);

            return result;
        }
    }

    /// <inheritdoc />
    public ulong NextUInt64(ulong exclusiveUpperBound)
    {
        if (exclusiveUpperBound == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveUpperBound),
                exclusiveUpperBound,
                "随机上界必须大于 0。");
        }

        var threshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;

        while (true)
        {
            var candidate = NextUInt64();
            if (candidate >= threshold)
            {
                return candidate % exclusiveUpperBound;
            }
        }
    }

    private struct SplitMix64
    {
        private ulong _state;

        internal SplitMix64(ulong seed)
        {
            _state = seed;
        }

        internal ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var value = _state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}
