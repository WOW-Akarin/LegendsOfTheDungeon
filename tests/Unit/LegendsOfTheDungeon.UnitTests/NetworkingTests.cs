using System.Buffers.Binary;
using System.Net;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.Networking;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class NetworkingTests
{
    private static FrameTransportOptions Options(
        int maxPayload = 64, int frames = 3, int queuedBytes = 128,
        int framesPerSecond = 100, int bytesPerSecond = 1024) =>
        new(maxPayload, TimeSpan.FromSeconds(1), frames,
            queuedBytes, framesPerSecond, bytesPerSecond);

    [Fact]
    public async Task FrameWireFormatIsNetworkBigEndianAndRoundTrips()
    {
        var options = Options();
        using var wire = new MemoryStream();
        await LengthPrefixedFrameCodec.WriteAsync(wire, new byte[] { 7, 8, 9 }, options);
        Assert.Equal(new byte[] { 0, 0, 0, 3, 7, 8, 9 }, wire.ToArray());
        wire.Position = 0;
        Assert.Equal(new byte[] { 7, 8, 9 },
            await LengthPrefixedFrameCodec.ReadAsync(wire, options));
        Assert.Null(await LengthPrefixedFrameCodec.ReadAsync(wire, options));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 }, FrameFailure.InvalidLength)]
    [InlineData(new byte[] { 0, 0, 1, 0 }, FrameFailure.TooLarge)]
    [InlineData(new byte[] { 0, 0 }, FrameFailure.Truncated)]
    [InlineData(new byte[] { 0, 0, 0, 3, 1 }, FrameFailure.Truncated)]
    public async Task InvalidLengthsAndPartialFramesFailClosed(byte[] bytes, FrameFailure failure)
    {
        using var wire = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<FrameProtocolException>(
            async () => await LengthPrefixedFrameCodec.ReadAsync(wire, Options()));
        Assert.Equal(failure, error.Failure);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizeOutgoingIsRejectedBeforeAnyWrite()
    {
        using var wire = new MemoryStream();
        var tooLarge = new byte[65];
        var failure = await Assert.ThrowsAsync<FrameProtocolException>(
            async () => await LengthPrefixedFrameCodec.WriteAsync(wire, tooLarge, Options()));
        Assert.Equal(FrameFailure.TooLarge, failure.Failure);
        Assert.Equal(0, wire.Length);
    }

    [Fact]
    public void FrameTransportBudgetsValidateTheirHardCeilings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(maxPayload: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(maxPayload: 1_048_577));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(frames: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(queuedBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(frames: 257));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(queuedBytes: 4_194_305));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameTransportOptions(10, TimeSpan.FromSeconds(31), 1, 10, 1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameTransportOptions(10, TimeSpan.Zero, 1, 10, 1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameTransportOptions(10, TimeSpan.FromSeconds(1), 1, 10, 0, 10));
    }

    [Fact]
    public void RateBudgetRejectsWithoutConsumingAndResetsAtOneSecond()
    {
        var budget = new FrameRateBudget(2, 5);
        Assert.True(budget.TryAccept(2, 0));
        Assert.True(budget.TryAccept(3, 0));
        Assert.False(budget.TryAccept(1, 999));
        Assert.True(budget.TryAccept(5, 1000));
        Assert.False(budget.TryAccept(5, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryAccept(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryAccept(-1, 1001));

        var byteLimited = new FrameRateBudget(4, 3);
        Assert.False(byteLimited.TryAccept(4, 0));
        Assert.True(byteLimited.TryAccept(3, 0));
        Assert.False(byteLimited.TryAccept(1, 0));
    }

    [Fact]
    public void OutboundBackpressureIsBoundedByBytesAndFrameCount()
    {
        var buffer = new OutboundFrameBuffer(Options(64, frames: 3, queuedBytes: 6));
        var callerBytes = new byte[] { 1, 2, 3 };
        Assert.Equal(FrameOfferStatus.Accepted, buffer.Offer(callerBytes));
        callerBytes[0] = 99;
        Assert.Equal(FrameOfferStatus.Accepted, buffer.Offer(new byte[] { 4, 5 }));
        Assert.Equal(FrameOfferStatus.Busy, buffer.Offer(new byte[] { 6, 7 }));
        Assert.Equal(2, buffer.QueuedFrames);
        Assert.Equal(5, buffer.QueuedBytes);

        buffer.Complete();
        Assert.Equal(FrameOfferStatus.Closed, buffer.Offer(new byte[] { 1 }));
        Assert.Equal(2, buffer.QueuedFrames);
    }

    [Fact]
    public void ConcurrentProducersCannotOverfillBoundedSocketSendQueue()
    {
        var buffer = new OutboundFrameBuffer(
            Options(maxPayload: 64, frames: 7, queuedBytes: 70,
                framesPerSecond: 100_000, bytesPerSecond: 1_000_000));
        var results = new FrameOfferStatus[500];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = buffer.Offer(new byte[8]);
        });

        Assert.Equal(7, results.Count(static result => result == FrameOfferStatus.Accepted));
        Assert.Equal(493, results.Count(static result => result == FrameOfferStatus.Busy));
        Assert.Equal(7, buffer.QueuedFrames);
        Assert.Equal(56, buffer.QueuedBytes);
        buffer.Complete();
    }

    [Fact]
    public void ConcurrentRateRequestsCannotBypassSingleConnectionBudget()
    {
        var budget = new FrameRateBudget(5, 50);
        var accepted = 0;
        Parallel.For(0, 500, _ =>
        {
            if (budget.TryAccept(10, 10))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.Equal(5, accepted);
        Assert.False(budget.TryAccept(1, 999));
        Assert.True(budget.TryAccept(1, 1010));
    }

    [Fact]
    public async Task AcceptedOutgoingPayloadIsSnapshottedBeforeCallerMutation()
    {
        using var wire = new MemoryStream();
        await using var transport = new FramedStreamTransport(
            wire, Options(maxPayload: 64, frames: 2, queuedBytes: 64));
        var callerOwned = new byte[] { 5, 6, 7 };
        Assert.Equal(FrameOfferStatus.Accepted, transport.OfferSend(callerOwned));
        callerOwned[0] = 99;
        var shutdown = await transport.StopAsync(TimeSpan.FromSeconds(5));
        Assert.True(shutdown.Drained);
        Assert.False(shutdown.DeliveryUnknown);
        Assert.Equal(1L, shutdown.WrittenFrames);
        Assert.Equal(new byte[] { 0, 0, 0, 3, 5, 6, 7 }, wire.ToArray());
    }

    [Fact]
    public void SendingFrameRateLimitIsVisibleAndDoesNotEnqueueOnDenial()
    {
        var buffer = new OutboundFrameBuffer(
            Options(64, frames: 10, queuedBytes: 100, framesPerSecond: 1));
        Assert.Equal(FrameOfferStatus.Accepted, buffer.Offer(new byte[] { 1 }));
        Assert.Equal(FrameOfferStatus.RateLimited, buffer.Offer(new byte[] { 2 }));
        Assert.Equal(1, buffer.QueuedFrames);
    }

    [Fact]
    public async Task DefaultNetworkIngressDeniesUnknownOrLocalTestIdentities()
    {
        var defaultGate = new NetworkIngressGate(
            new DenyByDefaultIdentityBinder(), new DenyByDefaultCommandAuthorizer());
        var denial = await defaultGate.VerifyAsync(
            new AuthenticationEvidence("private-secret-sentinel"), "world.command");
        Assert.True(denial.IsFailure);
        Assert.Equal("security.identity.denied", denial.Error.Code);
        Assert.DoesNotContain("private-secret", denial.Error.Message, StringComparison.Ordinal);

        var authorizer = new CountingAuthorizer();
        var localGate = new NetworkIngressGate(new LocalOnlyBinder(), authorizer);
        var localDenial = await localGate.VerifyAsync(
            new AuthenticationEvidence("private-secret-sentinel"), "world.command");
        Assert.True(localDenial.IsFailure);
        Assert.Equal("security.identity.production_required", localDenial.Error.Code);
        Assert.Equal(0, authorizer.Calls);
        // 本网络 API 只返回验证结果，完全不提供任何世界写回调入口。
    }

    [Fact]
    public async Task TcpFactoryDoesNotPermitPublicInternetEndpoint()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => TcpLoopbackConnector.ConnectAsync(
                IPAddress.Parse("8.8.8.8"), 10000, Options()));
        Assert.Contains("Loopback", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoopbackServerBacklogIsExplicitlyBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TcpLoopbackEndpoint(0, Options(), maximumPendingConnections: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TcpLoopbackEndpoint(0, Options(), maximumPendingConnections: 129));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TcpLoopbackEndpoint(-1, Options()));
    }

    private sealed class LocalOnlyBinder : IIdentityBindingPort
    {
        public ValueTask<Result<BoundIdentity>> BindAsync(
            AuthenticationEvidence evidence, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(
                BoundIdentity.CreateLocalTest("offline.test")));
        }
    }

    private sealed class CountingAuthorizer : ICommandAuthorizationPort
    {
        public int Calls { get; private set; }

        public Result Authorize(BoundIdentity? identity, string operationCode)
        {
            Calls++;
            return Result.Success();
        }
    }
}
