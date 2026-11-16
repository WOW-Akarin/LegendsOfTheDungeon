namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 提供基于持久化 UTC 的离线经过时间计算；不会读取真实系统时钟。
/// </summary>
public static class OfflineTime
{
    /// <summary>
    /// 计算两个 UTC 时间点之间可用于业务补偿的离线经过时间。
    /// </summary>
    /// <param name="persistedUtc">持久化时的绝对时间；Offset 会规范化为 UTC。</param>
    /// <param name="currentUtc">由 Infrastructure/Adapter 提供的当前绝对时间；Offset 会规范化为 UTC。</param>
    /// <param name="maximum">调用业务允许的最大离线补偿时长。</param>
    /// <returns>非负完整毫秒经过时间，并限制在 <paramref name="maximum"/> 以内；墙钟回拨时返回 0。</returns>
    public static Duration CalculateElapsed(
        DateTimeOffset persistedUtc,
        DateTimeOffset currentUtc,
        Duration maximum)
    {
        var persistedTicks = persistedUtc.UtcDateTime.Ticks;
        var currentTicks = currentUtc.UtcDateTime.Ticks;

        if (currentTicks <= persistedTicks)
        {
            return default;
        }

        var elapsedMilliseconds =
            (currentTicks - persistedTicks) / TimeSpan.TicksPerMillisecond;

        if (elapsedMilliseconds >= maximum.Milliseconds)
        {
            return maximum;
        }

        return new Duration(elapsedMilliseconds);
    }
}
