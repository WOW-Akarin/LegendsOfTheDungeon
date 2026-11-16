using System.Buffers.Binary;

namespace LegendsOfTheDungeon.Platform.Networking;

/// <summary>与具体游戏协议无关的网络帧异常类别；不回显不可信数据。</summary>
public enum FrameFailure
{
    /// <summary>非正的长度字段。</summary>
    InvalidLength,
    /// <summary>声明长度或待发送帧超过配置限制。</summary>
    TooLarge,
    /// <summary>报头或载荷尚未完整收到即断开。</summary>
    Truncated,
    /// <summary>整个帧超过读取期限。</summary>
    Timeout,
    /// <summary>超过每连接接收速率上限。</summary>
    RateLimited
}

/// <summary>网络帧错误，公开错误码固定且不含原始字节或凭证。</summary>
public sealed class FrameProtocolException : IOException
{
    /// <summary>建立不泄露原始网络输入的固定错误。</summary>
    /// <param name="failure">受限错误类别。</param>
    public FrameProtocolException(FrameFailure failure)
        : base(failure switch
        {
            FrameFailure.InvalidLength => "网络帧长度无效。",
            FrameFailure.TooLarge => "网络帧超过最大长度。",
            FrameFailure.Truncated => "网络帧未完整接收。",
            FrameFailure.Timeout => "网络帧接收超时。",
            FrameFailure.RateLimited => "网络帧接收频率超过限额。",
            _ => "网络帧无效。"
        })
    {
        Failure = failure;
    }

    /// <summary>固定错误类别。</summary>
    public FrameFailure Failure { get; }
}

/// <summary>单连接帧、速率与发送缓冲预算，生产配置必须有上限。</summary>
public sealed class FrameTransportOptions
{
    /// <summary>硬性最大单帧载荷；不能通过配置突破。</summary>
    public const int AbsoluteMaximumPayloadBytes = 1_048_576;

    /// <summary>每连接等待队列的硬帧数上限。</summary>
    public const int AbsoluteMaximumQueuedFrames = 256;

    /// <summary>每连接等待队列的硬字节上限（4 MiB）。</summary>
    public const int AbsoluteMaximumQueuedBytes = 4 * 1024 * 1024;

    /// <summary>整帧读取最多允许 30 秒，防止无限慢速占用。</summary>
    public static TimeSpan AbsoluteMaximumFrameReadTimeout => TimeSpan.FromSeconds(30);

    /// <summary>创建显式限制的帧传输设置。</summary>
    /// <param name="maximumPayloadBytes">单帧最大负载字节数，1–1048576。</param>
    /// <param name="frameReadTimeout">整帧读取期限，正数。</param>
    /// <param name="maximumQueuedFrames">单连接最大等待发送帧数。</param>
    /// <param name="maximumQueuedBytes">单连接最大等待发送载荷字节数。</param>
    /// <param name="maximumFramesPerSecond">每秒最多接纳的收/发帧数。</param>
    /// <param name="maximumBytesPerSecond">每秒最多接纳的收/发载荷字节数。</param>
    public FrameTransportOptions(
        int maximumPayloadBytes, TimeSpan frameReadTimeout,
        int maximumQueuedFrames, int maximumQueuedBytes,
        int maximumFramesPerSecond, int maximumBytesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPayloadBytes, AbsoluteMaximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frameReadTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameReadTimeout, AbsoluteMaximumFrameReadTimeout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueuedFrames);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumQueuedFrames, AbsoluteMaximumQueuedFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueuedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumQueuedBytes, AbsoluteMaximumQueuedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFramesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytesPerSecond);
        MaximumPayloadBytes = maximumPayloadBytes;
        FrameReadTimeout = frameReadTimeout;
        MaximumQueuedFrames = maximumQueuedFrames;
        MaximumQueuedBytes = maximumQueuedBytes;
        MaximumFramesPerSecond = maximumFramesPerSecond;
        MaximumBytesPerSecond = maximumBytesPerSecond;
    }

    /// <summary>最大 payload 字节数，报头另加 4 字节。</summary>
    public int MaximumPayloadBytes { get; }

    /// <summary>整个帧的读取超时，而非每个分片单独刷新。</summary>
    public TimeSpan FrameReadTimeout { get; }

    /// <summary>等待发送的最大帧数。</summary>
    public int MaximumQueuedFrames { get; }

    /// <summary>等待发送的最大载荷字节数。</summary>
    public int MaximumQueuedBytes { get; }

    /// <summary>一秒内允许的帧数。</summary>
    public int MaximumFramesPerSecond { get; }

    /// <summary>一秒内允许的载荷字节数。</summary>
    public int MaximumBytesPerSecond { get; }
}

/// <summary>四字节无符号大端长度 + 非空二进制 payload；不定义任何游戏操作码。</summary>
public static class LengthPrefixedFrameCodec
{
    /// <summary>读取一帧；在帧边界正常 EOF 返回 null，帧内 EOF 报截断错误。</summary>
    /// <param name="source">可读取的流，调用方负责串行读取。</param>
    /// <param name="options">固定最大帧和整帧超时限制。</param>
    /// <param name="cancellationToken">上层取消，与超时分开上报。</param>
    /// <returns>非空完整 payload 或流末尾的 null。</returns>
    public static async ValueTask<byte[]?> ReadAsync(
        Stream source, FrameTransportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.FrameReadTimeout);
        try
        {
            var header = new byte[sizeof(uint)];
            var read = await ReadAtMostAsync(source, header, budget.Token);
            if (read == 0)
            {
                return null;
            }

            if (read != header.Length)
            {
                throw new FrameProtocolException(FrameFailure.Truncated);
            }

            var declared = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (declared == 0)
            {
                throw new FrameProtocolException(FrameFailure.InvalidLength);
            }

            if (declared > options.MaximumPayloadBytes)
            {
                // 拒绝长度声明后才分配 payload，不依赖攻击者提供的数据分配大缓冲。
                throw new FrameProtocolException(FrameFailure.TooLarge);
            }

            var payload = new byte[(int)declared];
            if (await ReadAtMostAsync(source, payload, budget.Token) != payload.Length)
            {
                throw new FrameProtocolException(FrameFailure.Truncated);
            }

            return payload;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FrameProtocolException(FrameFailure.Timeout);
        }
    }

    /// <summary>将一帧串行写入流，不自行并发交错写入。</summary>
    /// <param name="destination">独占的单写入方。</param>
    /// <param name="payload">非空且在限制内的载荷。</param>
    /// <param name="options">帧预算。</param>
    /// <param name="cancellationToken">写入取消信号。</param>
    public static async ValueTask WriteAsync(
        Stream destination, ReadOnlyMemory<byte> payload,
        FrameTransportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ValidateLength(payload.Length, options);
        var header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await destination.WriteAsync(header, cancellationToken);
        await destination.WriteAsync(payload, cancellationToken);
    }

    internal static void ValidateLength(int length, FrameTransportOptions options)
    {
        if (length <= 0)
        {
            throw new FrameProtocolException(FrameFailure.InvalidLength);
        }

        if (length > options.MaximumPayloadBytes)
        {
            throw new FrameProtocolException(FrameFailure.TooLarge);
        }
    }

    private static async ValueTask<int> ReadAtMostAsync(
        Stream source, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await source.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return read;
    }
}
