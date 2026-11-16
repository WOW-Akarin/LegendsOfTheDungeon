using System.Diagnostics;

namespace LegendsOfTheDungeon.Platform.Networking;

/// <summary>确定性的单连接固定窗口限流，单个窗口最多指定帧数及字节数。</summary>
public sealed class FrameRateBudget
{
    private readonly object _gate = new();
    private readonly int _maximumFrames;
    private readonly int _maximumBytes;
    private long _windowStart = -1;
    private int _frames;
    private int _bytes;

    /// <summary>固定每 1000 毫秒计数上限，无 Timer 或背景任务。</summary>
    /// <param name="maximumFrames">一秒内最大帧数。</param>
    /// <param name="maximumBytes">一秒内最大 payload 字节数。</param>
    public FrameRateBudget(int maximumFrames, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumFrames = maximumFrames;
        _maximumBytes = maximumBytes;
    }

    /// <summary>尝试为一帧预留预算；被拒绝不会消费配额。</summary>
    /// <param name="payloadBytes">非负载荷大小。</param>
    /// <param name="elapsedMilliseconds">自连接建立起非负单调毫秒数。</param>
    /// <returns>是否获得预算。</returns>
    public bool TryAccept(int payloadBytes, long elapsedMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMilliseconds);
        lock (_gate)
        {
            if (_windowStart == -1)
            {
                _windowStart = elapsedMilliseconds;
            }

            if (elapsedMilliseconds < _windowStart)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            }

            if (elapsedMilliseconds - _windowStart >= 1000)
            {
                _windowStart = elapsedMilliseconds;
                _frames = 0;
                _bytes = 0;
            }

            if (_frames >= _maximumFrames || payloadBytes > _maximumBytes - _bytes)
            {
                return false;
            }

            _frames++;
            _bytes += payloadBytes;
            return true;
        }
    }
}

/// <summary>单调墙钟耗时仅供传输限流，不用于游戏逻辑 Tick 或回放。</summary>
internal sealed class ConnectionElapsedTime
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    internal long Milliseconds => _watch.ElapsedMilliseconds;
}
