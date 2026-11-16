namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 标识必须保持独立序列的稳定游戏随机流；数值 ID 一经发布不可重排复用。
/// </summary>
public enum RandomStreamKind
{
    /// <summary>
    /// 战斗判定随机流。
    /// </summary>
    Combat = 1,

    /// <summary>
    /// 掉落与奖励随机流。
    /// </summary>
    Loot = 2,

    /// <summary>
    /// AI 决策随机流。
    /// </summary>
    AI = 3,
}

/// <summary>
/// 根据根 Seed 和稳定流标识创建相互隔离的确定性随机流。
/// </summary>
public static class RandomStreams
{
    /// <summary>
    /// 当前稳定流派生规则版本。
    /// </summary>
    public const int DerivationVersion = 1;

    private const ulong CombatSalt = 0x434F4D4241540001UL;
    private const ulong LootSalt = 0x4C4F4F5400000001UL;
    private const ulong AiSalt = 0x4149000000000001UL;

    /// <summary>
    /// 为指定稳定流创建独立确定性随机实例。
    /// </summary>
    /// <param name="rootSeed">世界、会话或业务显式提供的根 Seed。</param>
    /// <param name="stream">稳定流标识。</param>
    /// <returns>只受根 Seed 与指定流影响的独立随机实例。</returns>
    /// <exception cref="ArgumentOutOfRangeException">传入未知流标识时抛出。</exception>
    public static DeterministicRandom Create(
        ulong rootSeed,
        RandomStreamKind stream)
    {
        var salt = stream switch
        {
            RandomStreamKind.Combat => CombatSalt,
            RandomStreamKind.Loot => LootSalt,
            RandomStreamKind.AI => AiSalt,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stream),
                stream,
                "未知 RandomStreamKind。"),
        };

        return new DeterministicRandom(DeriveSeed(rootSeed, salt));
    }

    private static ulong DeriveSeed(ulong rootSeed, ulong salt)
    {
        unchecked
        {
            var value = (rootSeed ^ salt) + 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
