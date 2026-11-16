using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LegendsOfTheDungeon.Platform.Networking;
using LegendsOfTheDungeon.Security.Contracts;

await CheckAsync("fragmented-and-coalesced-frames", FragmentationAsync);
await CheckAsync("oversized-frame-rejected-before-payload", OversizedAsync);
await CheckAsync("truncated-frame-on-disconnect", TruncatedAsync);
await CheckAsync("slow-header-times-out", SlowHeaderAsync);
await CheckAsync("bidirectional-tcp-with-pluggable-transport", BidirectionalAsync);
await CheckAsync("peer-disconnect-releases-connection", DisconnectAsync);
await CheckAsync("receive-rate-limiting", IncomingRateAsync);
await CheckAsync("slow-reader-send-backpressure", SlowReaderBackpressureAsync);
await CheckAsync("unauthorized-frames-never-reach-world-write", UnauthorizedAsync);
await CheckAsync("shutdown-reports-unknown-inflight-write", UncooperativeWriteAsync);
Console.WriteLine("P13_NETWORKING_LOOPBACK_PASS");

static async Task CheckAsync(string name, Func<Task> probe)
{
    await probe().WaitAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine("PASS " + name);
}

static FrameTransportOptions Options(
    int maxPayload = 1024, int readTimeoutMs = 1500,
    int frames = 8, int bufferedBytes = 8192,
    int maxFramesPerSecond = 10000, int maxBytesPerSecond = 100_000_000) =>
    new(maxPayload, TimeSpan.FromMilliseconds(readTimeoutMs),
        frames, bufferedBytes, maxFramesPerSecond, maxBytesPerSecond);

static TcpListener Listen()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return listener;
}

static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

static byte[] Wire(ReadOnlySpan<byte> payload)
{
    var result = new byte[4 + payload.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result, (uint)payload.Length);
    payload.CopyTo(result.AsSpan(4));
    return result;
}

static void Check(bool condition, string explanation)
{
    if (!condition)
    {
        throw new InvalidOperationException(explanation);
    }
}

static async Task<FrameProtocolException> FailureAsync(
    IFrameTransport transport, FrameFailure expected)
{
    try
    {
        _ = await transport.ReceiveAsync();
    }
    catch (FrameProtocolException error) when (error.Failure == expected)
    {
        return error;
    }

    throw new InvalidOperationException("Expected protocol failure " + expected);
}

static async Task FragmentationAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(accepted.GetStream(), Options());
        var stream = raw.GetStream();
        var first = Wire([1, 2, 3, 4, 5]);
        var second = Wire([8, 9]);

        // 两段同一 header + 接收合并的两帧：不能假定一次 TCP Read 等于一次消息。
        await stream.WriteAsync(first.AsMemory(0, 2));
        await stream.WriteAsync(first.AsMemory(2, 4));
        await stream.WriteAsync(first.AsMemory(6));
        var sticky = new byte[second.Length + first.Length];
        second.CopyTo(sticky, 0);
        first.CopyTo(sticky, second.Length);
        await stream.WriteAsync(sticky);

        Check((await receiver.ReceiveAsync())!.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }),
            "partial header/payload was not reconstructed");
        Check((await receiver.ReceiveAsync())!.SequenceEqual(new byte[] { 8, 9 }),
            "first coalesced frame was lost");
        Check((await receiver.ReceiveAsync())!.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }),
            "second coalesced frame was lost");
    }
    finally
    {
        listener.Stop();
    }
}

static async Task OversizedAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(accepted.GetStream(), Options(maxPayload: 128));
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, uint.MaxValue);
        await raw.GetStream().WriteAsync(header);
        _ = await FailureAsync(receiver, FrameFailure.TooLarge);
        Check(receiver.OfferSend(new byte[] { 1 }) == FrameOfferStatus.Closed,
            "oversize must close connection");
    }
    finally
    {
        listener.Stop();
    }
}

static async Task TruncatedAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(accepted.GetStream(), Options());
        var partial = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(partial, 8);
        partial[4] = 1;
        partial[5] = 2;
        await raw.GetStream().WriteAsync(partial);
        raw.Client.Shutdown(SocketShutdown.Send);
        _ = await FailureAsync(receiver, FrameFailure.Truncated);
    }
    finally
    {
        listener.Stop();
    }
}

static async Task SlowHeaderAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(
            accepted.GetStream(), Options(readTimeoutMs: 80));
        await raw.GetStream().WriteAsync(new byte[] { 0, 0 });
        _ = await FailureAsync(receiver, FrameFailure.Timeout);
        Check(receiver.OfferSend(new byte[] { 3 }) == FrameOfferStatus.Closed,
            "timeout must abort connection");
    }
    finally
    {
        listener.Stop();
    }
}

static async Task BidirectionalAsync()
{
    var listener = Listen();
    try
    {
        var acceptTask = listener.AcceptTcpClientAsync();
        await using var client = await TcpLoopbackConnector.ConnectAsync(
            IPAddress.Loopback, Port(listener), Options());
        using var accepted = await acceptTask;
        var serverStream = accepted.GetStream();
        Check(client.OfferSend(new byte[] { 7, 3, 1 }) == FrameOfferStatus.Accepted,
            "tcp writer did not accept frame");
        Check((await LengthPrefixedFrameCodec.ReadAsync(serverStream, Options()))!
            .SequenceEqual(new byte[] { 7, 3, 1 }), "server did not receive frame");

        await LengthPrefixedFrameCodec.WriteAsync(serverStream, new byte[] { 5, 4 }, Options());
        Check((await client.ReceiveAsync())!.SequenceEqual(new byte[] { 5, 4 }),
            "client did not receive reply");
        var report = await client.StopAsync(TimeSpan.FromSeconds(2));
        Check(report.Drained && report.WrittenFrames == 1 && report.QueuedFrames == 0,
            "graceful send shutdown did not drain");
        Check(client.OfferSend(new byte[] { 9 }) == FrameOfferStatus.Closed,
            "stopped connection accepted new work");
    }
    finally
    {
        listener.Stop();
    }
}

static async Task DisconnectAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(accepted.GetStream(), Options());
        raw.Client.Shutdown(SocketShutdown.Send);
        Check(await receiver.ReceiveAsync() is null, "EOF at frame boundary must return null");
        Check(receiver.OfferSend(new byte[] { 1 }) == FrameOfferStatus.Closed, "EOF must release transport");
        await receiver.DisposeAsync();
    }
    finally
    {
        listener.Stop();
    }
}

static async Task IncomingRateAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(
            accepted.GetStream(), Options(maxFramesPerSecond: 1));
        var first = Wire([1]);
        var second = Wire([2]);
        await raw.GetStream().WriteAsync(first.Concat(second).ToArray());
        Check((await receiver.ReceiveAsync())!.SequenceEqual(new byte[] { 1 }),
            "first frame unexpectedly blocked");
        _ = await FailureAsync(receiver, FrameFailure.RateLimited);
    }
    finally
    {
        listener.Stop();
    }
}

static async Task SlowReaderBackpressureAsync()
{
    var listener = Listen();
    try
    {
        var acceptTask = listener.AcceptTcpClientAsync();
        var budget = Options(maxPayload: 16_384, frames: 1,
            bufferedBytes: 16_384, maxFramesPerSecond: 100_000);
        await using var client = await TcpLoopbackConnector.ConnectAsync(
            IPAddress.Loopback, Port(listener), budget);
        using var accepted = await acceptTask;
        accepted.ReceiveBufferSize = 1024;
        var frame = new byte[16_384];
        var busy = false;
        for (var i = 0; i < 1024; i++)
        {
            var result = client.OfferSend(frame);
            if (result == FrameOfferStatus.Busy)
            {
                busy = true;
                break;
            }

            Check(result == FrameOfferStatus.Accepted,
                "slow receiver unexpectedly triggered another status: " + result);
        }

        Check(busy, "slow peer must eventually trigger bounded backpressure");
        var report = await client.StopAsync(TimeSpan.FromMilliseconds(100));
        Check(client.OfferSend(new byte[] { 1 }) == FrameOfferStatus.Closed, "shutdown accepted new frames");
        Check(report.QueuedFrames >= 0 && report.QueuedFrames <= 1,
            "send queue exceeded declared bound");
    }
    finally
    {
        listener.Stop();
    }
}

static async Task UnauthorizedAsync()
{
    var listener = Listen();
    try
    {
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, Port(listener));
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var receiver = new FramedStreamTransport(accepted.GetStream(), Options());
        await raw.GetStream().WriteAsync(Wire([99, 1]));
        var frame = await receiver.ReceiveAsync();
        Check(frame is { Length: 2 }, "pre-auth transport frame missing");

        var gate = new NetworkIngressGate(
            new DenyByDefaultIdentityBinder(), new DenyByDefaultCommandAuthorizer());
        var result = await gate.VerifyAsync(
            new AuthenticationEvidence("untrusted-credential"), "world.write");
        var simulatedWorldWrites = 0;
        if (result.IsSuccess)
        {
            simulatedWorldWrites++;
        }

        Check(result.IsFailure && simulatedWorldWrites == 0,
            "unauthorized loopback connection crossed world write boundary");
        Check(result.Error.Code == "security.identity.denied",
            "default denial must have stable security error");
    }
    finally
    {
        listener.Stop();
    }
}


static async Task UncooperativeWriteAsync()
{
    var controlledStream = new BlockingWriteStream();
    await using var transport = new FramedStreamTransport(
        controlledStream, Options(maxPayload: 64, frames: 2, bufferedBytes: 128));
    Check(transport.OfferSend(new byte[] { 1, 2, 3 }) == FrameOfferStatus.Accepted,
        "cannot prepare controlled in-flight frame");
    await controlledStream.Started.WaitAsync(TimeSpan.FromSeconds(2));
    try
    {
        var report = await transport.StopAsync(TimeSpan.FromMilliseconds(60));
        Check(!report.Drained, "uncooperative sender was incorrectly marked drained");
        Check(report.DeliveryUnknown, "in-flight partial send must be delivery-unknown");
        Check(report.WrittenFrames == 0, "no completed write is possible before release");
        Check(transport.OfferSend(new byte[] { 9 }) == FrameOfferStatus.Closed,
            "shutdown must reject subsequent frames");
        await transport.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }
    finally
    {
        controlledStream.Release();
    }
}

// 故意无视 CancellationToken 的测试替身，验证关闭不会假定外部流自动配合取消。
file sealed class BlockingWriteStream : Stream
{
    private readonly TaskCompletionSource<bool> _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Started => _started.Task;

    internal void Release() => _release.TrySetResult(true);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => 0;

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _started.TrySetResult(true);
        return new ValueTask(_release.Task);
    }
}
