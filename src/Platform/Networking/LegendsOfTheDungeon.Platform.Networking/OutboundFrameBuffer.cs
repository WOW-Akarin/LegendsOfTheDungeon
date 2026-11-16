using System.Threading.Channels;

namespace LegendsOfTheDungeon.Platform.Networking;

/// <summary>尝试接纳待发送帧的稳定反馈。</summary>
public enum FrameOfferStatus
{
    /// <summary>已进入发送缓冲，尚不代表对端接收。</summary>
    Accepted,
    /// <summary>帧数或总字节预算已满，无数据被接纳。</summary>
    Busy,
    /// <summary>关闭或发送出现故障，不接纳新数据。</summary>
    Closed,
    /// <summary>本连接的发送限流预算不足。</summary>
    RateLimited
}

/// <summary>帧数与字节双约束的发送缓冲；只允许单消费者，所有生产者可并发。</summary>
public sealed class OutboundFrameBuffer
{
    private readonly object _gate = new();
    private readonly Channel<byte[]> _channel;
    private readonly FrameTransportOptions _options;
    private readonly FrameRateBudget _rateBudget;
    private readonly int _maximumQueuedBytes;
    private readonly ConnectionElapsedTime _clock = new();
    private int _queuedFrames;
    private int _queuedBytes;
    private bool _closed;

    /// <summary>建立固定容量的发送缓冲。</summary>
    /// <param name="options">一条连接的共享配置预算。</param>
    public OutboundFrameBuffer(FrameTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _maximumQueuedBytes = options.MaximumQueuedBytes;
        _rateBudget = new FrameRateBudget(options.MaximumFramesPerSecond, options.MaximumBytesPerSecond);
        _channel = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(options.MaximumQueuedFrames)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    /// <summary>尚未交给单写入者的帧数。</summary>
    public int QueuedFrames
    {
        get
        {
            lock (_gate)
            {
                return _queuedFrames;
            }
        }
    }

    /// <summary>尚未交给单写入者的载荷总字节数。</summary>
    public int QueuedBytes
    {
        get
        {
            lock (_gate)
            {
                return _queuedBytes;
            }
        }
    }

    /// <summary>尝试立刻复制并接纳发送帧，拒绝不会消费限流预算。</summary>
    /// <param name="payload">外部所有权的数据，只在成功时克隆。</param>
    /// <returns>接纳、满队列、已关闭或限流。</returns>
    public FrameOfferStatus Offer(ReadOnlyMemory<byte> payload)
    {
        LengthPrefixedFrameCodec.ValidateLength(payload.Length, _options);
        lock (_gate)
        {
            if (_closed)
            {
                return FrameOfferStatus.Closed;
            }

            if (_queuedFrames >= _options.MaximumQueuedFrames
                || payload.Length > _maximumQueuedBytes - _queuedBytes)
            {
                return FrameOfferStatus.Busy;
            }

            if (!_rateBudget.TryAccept(payload.Length, _clock.Milliseconds))
            {
                return FrameOfferStatus.RateLimited;
            }

            var snapshot = payload.ToArray();
            if (!_channel.Writer.TryWrite(snapshot))
            {
                throw new InvalidOperationException("发送缓冲账本与通道容量不同步。");
            }

            _queuedFrames++;
            _queuedBytes += snapshot.Length;
            return FrameOfferStatus.Accepted;
        }
    }

    /// <summary>停止接纳新帧，允许唯一消费者排空旧帧。</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _closed = true;
            _channel.Writer.TryComplete();
        }
    }

    internal async ValueTask<byte[]?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            lock (_gate)
            {
                if (_channel.Reader.TryRead(out var frame))
                {
                    _queuedFrames--;
                    _queuedBytes -= frame.Length;
                    return frame;
                }
            }
        }

        return null;
    }

    internal void DiscardQueued()
    {
        lock (_gate)
        {
            while (_channel.Reader.TryRead(out var frame))
            {
                _queuedFrames--;
                _queuedBytes -= frame.Length;
            }
        }
    }
}
