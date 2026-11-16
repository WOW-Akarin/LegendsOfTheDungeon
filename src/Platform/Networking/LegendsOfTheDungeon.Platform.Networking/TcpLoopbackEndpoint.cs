using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LegendsOfTheDungeon.Platform.Networking;

/// <summary>仅本机地址可监听的 TCP 接收端，禁止充当未经认证的公网 ServerHost。</summary>
public sealed class TcpLoopbackEndpoint : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly FrameTransportOptions _options;
    private int _accepting;
    private int _disposed;

    /// <summary>建立本机监听并固定未接纳连接的 Socket backlog；不启动业务分发循环。</summary>
    /// <param name="port">0 为系统分配端口，否则必须为 1–65535。</param>
    /// <param name="options">每个新连接的帧与缓冲限制。</param>
    /// <param name="maximumPendingConnections">未接纳 TCP 连接的 backlog，1–128。</param>
    public TcpLoopbackEndpoint(
        int port, FrameTransportOptions options, int maximumPendingConnections = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPendingConnections, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPendingConnections, 128);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(maximumPendingConnections);
    }

    /// <summary>实际绑定的本机 TCP 端点；端口可由操作系统动态分配。</summary>
    public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;

    /// <summary>一次只允许一个接收者，接入后仅提供受限帧 Transport，不分发游戏命令。</summary>
    /// <param name="cancellationToken">停止等待接入的取消信号。</param>
    /// <returns>调用方拥有且必须释放的网络帧连接。</returns>
    public async ValueTask<IFrameTransport> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _accepting, 1, 0) != 0)
        {
            throw new InvalidOperationException("单个 Loopback 端点同时只允许一个 Accept。");
        }

        try
        {
            var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            try
            {
                return new FramedStreamTransport(client.GetStream(), _options, client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            Volatile.Write(ref _accepting, 0);
        }
    }

    /// <summary>关闭本机监听 Socket；已经接纳的连接由各自 Transport 负责释放。</summary>
    /// <returns>已停止本机监听的已完成任务。</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _listener.Stop();
        }

        return ValueTask.CompletedTask;
    }
}
