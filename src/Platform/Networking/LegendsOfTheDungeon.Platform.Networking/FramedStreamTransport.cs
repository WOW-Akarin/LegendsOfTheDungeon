using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LegendsOfTheDungeon.Platform.Networking;

/// <summary>关闭时可审计的出站状态；已写入套接字不等于对端已确认。</summary>
public sealed class TransportShutdownReport
{
    internal TransportShutdownReport(
        bool drained, int queuedFrames, int queuedBytes,
        long writtenFrames, bool deliveryUnknown)
    {
        Drained = drained;
        QueuedFrames = queuedFrames;
        QueuedBytes = queuedBytes;
        WrittenFrames = writtenFrames;
        DeliveryUnknown = deliveryUnknown;
    }

    /// <summary>全部已接纳帧是否写入流；不代表对端接收。</summary>
    public bool Drained { get; }

    /// <summary>停止时未交付给底层流的排队帧数。</summary>
    public int QueuedFrames { get; }

    /// <summary>停止时等待发送的载荷字节数。</summary>
    public int QueuedBytes { get; }

    /// <summary>本地完整写入流的帧数，并非远端确认。</summary>
    public long WrittenFrames { get; }

    /// <summary>发送途中关闭或写异常，当前帧可能已有部分字节到达对端。</summary>
    public bool DeliveryUnknown { get; }
}

/// <summary>通用帧传输 Port；跨连接认证与世界状态写入不属于该 Port。</summary>
public interface IFrameTransport : IAsyncDisposable
{
    /// <summary>线程安全地尝试排入单连接有界发送队列；成功不意味着远端收到。</summary>
    /// <param name="payload">非空且满足最大长度的载荷。</param>
    /// <returns>已接纳、背压、关闭或限流。</returns>
    FrameOfferStatus OfferSend(ReadOnlyMemory<byte> payload);

    /// <summary>串行接收一帧并检查整个帧的读取期限；断开后返回 null。</summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>完整载荷或连接在帧边界结束。</returns>
    ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>停止接受新发送、限时排空，并返回未发/结果不确定清单的数量。</summary>
    /// <param name="timeout">非负的关闭期限。</param>
    /// <param name="cancellationToken">调用者取消等待的信号。</param>
    /// <returns>出站关停报告。</returns>
    Task<TransportShutdownReport> StopAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>可用于 MemoryStream、测试流及真实 NetworkStream 的通用有界帧传输实现。</summary>
public sealed class FramedStreamTransport : IFrameTransport
{
    private readonly Stream _stream;
    private readonly TcpClient? _tcpClient;
    private readonly FrameTransportOptions _options;
    private readonly OutboundFrameBuffer _outbound;
    private readonly FrameRateBudget _incomingRate;
    private readonly ConnectionElapsedTime _clock = new();
    private readonly CancellationTokenSource _abort = new();
    private readonly Task _sender;
    private int _disposed;
    private int _aborted;
    private int _receiving;
    private int _inFlight;
    private int _deliveryUnknown;
    private long _writtenFrames;

    /// <summary>接管指定双向流，关闭时销毁该流；不赋予上层世界写权限。</summary>
    /// <param name="stream">外部创建且由本对象拥有的双向流。</param>
    /// <param name="options">单连接帧、限流和发送容量的固定预算。</param>
    public FramedStreamTransport(Stream stream, FrameTransportOptions options)
        : this(stream, options, null)
    {
    }

    internal FramedStreamTransport(Stream stream, FrameTransportOptions options, TcpClient? tcpClient)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("帧传输需要双向可读写流。", nameof(stream));
        }

        _tcpClient = tcpClient;
        _outbound = new OutboundFrameBuffer(options);
        _incomingRate = new FrameRateBudget(options.MaximumFramesPerSecond, options.MaximumBytesPerSecond);
        _sender = Task.Run(SendLoopAsync);
    }

    /// <inheritdoc />
    public FrameOfferStatus OfferSend(ReadOnlyMemory<byte> payload)
    {
        if (Volatile.Read(ref _aborted) != 0)
        {
            return FrameOfferStatus.Closed;
        }

        return _outbound.Offer(payload);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _aborted) != 0, this);
        if (Interlocked.CompareExchange(ref _receiving, 1, 0) != 0)
        {
            throw new InvalidOperationException("同一连接只能有一个接收者，禁止竞争消费帧头。");
        }

        try
        {
            var payload = await LengthPrefixedFrameCodec.ReadAsync(
                _stream, _options, cancellationToken);
            if (payload is null)
            {
                Abort();
                return null;
            }

            if (!_incomingRate.TryAccept(payload.Length, _clock.Milliseconds))
            {
                throw new FrameProtocolException(FrameFailure.RateLimited);
            }

            return payload;
        }
        catch (OperationCanceledException)
        {
            // 单帧内取消后流游标不可安全恢复，必须关闭，避免错位解码。
            Abort();
            throw;
        }
        catch (FrameProtocolException)
        {
            Abort();
            throw;
        }
        catch (IOException)
        {
            Abort();
            throw;
        }
        catch (ObjectDisposedException)
        {
            Abort();
            throw;
        }
        finally
        {
            Volatile.Write(ref _receiving, 0);
        }
    }

    /// <inheritdoc />
    public async Task<TransportShutdownReport> StopAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        _outbound.Complete();
        var senderEnded = false;
        try
        {
            await _sender.WaitAsync(timeout, cancellationToken);
            senderEnded = true;
        }
        catch (TimeoutException)
        {
            // 不把仍在写入的帧视为成功。
        }

        var queuedFrames = _outbound.QueuedFrames;
        var queuedBytes = _outbound.QueuedBytes;
        var deliveryUnknown = Volatile.Read(ref _inFlight) != 0
            || Volatile.Read(ref _deliveryUnknown) != 0;
        var drained = senderEnded && queuedFrames == 0 && !deliveryUnknown;
        var report = new TransportShutdownReport(
            drained, queuedFrames, queuedBytes,
            Interlocked.Read(ref _writtenFrames), deliveryUnknown);
        Abort();
        _outbound.DiscardQueued();
        return report;
    }

    /// <summary>取消底层 I/O 并释放流及残留发送快照；不等待无取消能力的外部流。</summary>
    /// <returns>不阻塞等待未配合取消的自定义 Stream 的完成任务。</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        Abort();
        _outbound.DiscardQueued();
        if (_sender.IsCompleted)
        {
            _abort.Dispose();
        }
        else
        {
            // 不在后台任务仍读取 CTS.Token 时提前释放。
            _ = _sender.ContinueWith(
                static (_, state) => ((FramedStreamTransport)state!)._abort.Dispose(),
                this, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return ValueTask.CompletedTask;
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (await _outbound.DequeueAsync(_abort.Token) is { } frame)
            {
                if (Volatile.Read(ref _aborted) != 0)
                {
                    // 消费者已读取但尚未开始发送；关闭时不能伪称发送成功。
                    Volatile.Write(ref _deliveryUnknown, 1);
                    break;
                }

                Volatile.Write(ref _inFlight, 1);
                try
                {
                    await LengthPrefixedFrameCodec.WriteAsync(
                        _stream, frame, _options, _abort.Token);
                    Interlocked.Increment(ref _writtenFrames);
                }
                catch (Exception)
                {
                    Volatile.Write(ref _deliveryUnknown, 1);
                    Abort();
                    break;
                }
                finally
                {
                    Volatile.Write(ref _inFlight, 0);
                }
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // Close/Dispose 已向通道发出停止信号，剩余队列不能自动忽略。
        }
        catch (Exception)
        {
            Volatile.Write(ref _deliveryUnknown, 1);
            Abort();
        }
    }

    private void Abort()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0)
        {
            return;
        }

        _outbound.Complete();
        _abort.Cancel();
        _stream.Dispose();
        _tcpClient?.Dispose();
    }
}

/// <summary>只允许本机 Loopback 端点的 TCP 客户端工厂，避免 P13 未认证对公网开放。</summary>
public static class TcpLoopbackConnector
{
    /// <summary>连接本机 Loopback 并将 NetworkStream 交给可插拔通用帧 Transport。</summary>
    /// <param name="address">仅允许 127.0.0.0/8 或 ::1。</param>
    /// <param name="port">远端 TCP 端口，1–65535。</param>
    /// <param name="options">固定预算。</param>
    /// <param name="cancellationToken">连接取消令牌。</param>
    /// <returns>具有真实 TCP Socket 的帧 Transport。</returns>
    public static async Task<IFrameTransport> ConnectAsync(
        IPAddress address, int port, FrameTransportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        if (!IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("P13 只允许本机 Loopback，真实公网监听需生产认证 Host。", nameof(address));
        }

        var client = new TcpClient(address.AddressFamily);
        try
        {
            await client.ConnectAsync(address, port, cancellationToken);
            return new FramedStreamTransport(client.GetStream(), options, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
