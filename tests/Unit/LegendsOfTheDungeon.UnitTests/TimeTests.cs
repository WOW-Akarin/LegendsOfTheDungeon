using LegendsOfTheDungeon.Kernel;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class TimeTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DurationUsesNonNegativeIntegerMilliseconds()
    {
        Assert.Equal(0, default(Duration).Milliseconds);
        Assert.Equal(1500, new Duration(1500).Milliseconds);
        Assert.Equal(2000, Duration.FromSeconds(2).Milliseconds);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), new Duration(1500).ToTimeSpan());

        Assert.Throws<ArgumentOutOfRangeException>(() => new Duration(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Duration(long.MaxValue));
    }

    [Fact]
    public void DurationArithmeticRejectsOverflow()
    {
        var maximum = new Duration(
            TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond);

        Assert.Throws<OverflowException>(() => maximum.Add(new Duration(1)));
        Assert.Throws<OverflowException>(() => new Duration(1).Multiply(ulong.MaxValue));
        Assert.Throws<OverflowException>(() => Duration.FromSeconds(long.MaxValue));
    }

    [Fact]
    public void DurationAndTickComparisonOperatorsFollowScalarOrder()
    {
        var shortDuration = new Duration(10);
        var longDuration = new Duration(20);
        var earlyTick = new Tick(3);
        var lateTick = new Tick(4);

        Assert.True(shortDuration < longDuration);
        Assert.True(shortDuration <= longDuration);
        Assert.True(longDuration > shortDuration);
        Assert.True(longDuration >= shortDuration);
        Assert.Equal(0, shortDuration.CompareTo(new Duration(10)));

        Assert.True(earlyTick < lateTick);
        Assert.True(earlyTick <= lateTick);
        Assert.True(lateTick > earlyTick);
        Assert.True(lateTick >= earlyTick);
        Assert.Equal(0, earlyTick.CompareTo(new Tick(3)));
    }

    [Fact]
    public void TickStartsAtZeroAndRejectsOverflow()
    {
        Assert.Equal(0UL, default(Tick).Value);
        Assert.Equal(7UL, new Tick(5).Add(2).Value);

        Assert.Throws<OverflowException>(() => new Tick(ulong.MaxValue).Add(1));
    }

    [Fact]
    public void FakeClockAdvancesTickAndMonotonicTimeWithoutSleep()
    {
        var clock = new FakeClock(new Duration(50), Epoch);

        clock.AdvanceTicks(3);

        Assert.Equal(3UL, clock.CurrentTick.Value);
        Assert.Equal(150, clock.MonotonicElapsed.Milliseconds);
        Assert.Equal(Epoch, clock.UtcNow);
    }

    [Fact]
    public void FakeClockNormalizesAuditUtcAndCanAdjustItIndependently()
    {
        var offsetTime = new DateTimeOffset(
            2026,
            11,
            16,
            8,
            0,
            0,
            TimeSpan.FromHours(8));
        var clock = new FakeClock(new Duration(50), offsetTime);

        Assert.Equal(Epoch, clock.UtcNow);

        clock.SetUtc(Epoch.AddHours(10));
        Assert.Equal(Epoch.AddHours(10), clock.UtcNow);
        Assert.Equal(0UL, clock.CurrentTick.Value);
        Assert.Equal(0, clock.MonotonicElapsed.Milliseconds);

        clock.SetUtc(Epoch.AddHours(-10));
        Assert.Equal(Epoch.AddHours(-10), clock.UtcNow);
        Assert.Equal(0UL, clock.CurrentTick.Value);
    }

    [Fact]
    public void FakeClockAdvanceIsAtomicWhenTickOverflows()
    {
        var clock = new FakeClock(
            new Duration(50),
            Epoch,
            new Tick(ulong.MaxValue),
            new Duration(100));

        Assert.Throws<OverflowException>(() => clock.AdvanceTicks(1));
        Assert.Equal(ulong.MaxValue, clock.CurrentTick.Value);
        Assert.Equal(100, clock.MonotonicElapsed.Milliseconds);
    }

    [Fact]
    public void FakeClockAdvanceIsAtomicWhenElapsedTimeOverflows()
    {
        var maximum = new Duration(
            TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond);
        var clock = new FakeClock(
            new Duration(1),
            Epoch,
            new Tick(10),
            maximum);

        Assert.Throws<OverflowException>(() => clock.AdvanceTicks(1));
        Assert.Equal(10UL, clock.CurrentTick.Value);
        Assert.Equal(maximum, clock.MonotonicElapsed);
    }

    [Fact]
    public void FakeClockRejectsZeroTickDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FakeClock(default, Epoch));
    }

    [Fact]
    public void LogicalTimerRejectsClockWithZeroTickDuration()
    {
        Assert.Throws<InvalidOperationException>(
            () => LogicalTimer.Start(new InvalidClock(), new Duration(1)));
    }

    [Fact]
    public void ZeroDurationTimerIsDueOnCurrentTick()
    {
        var clock = new FakeClock(new Duration(50), Epoch);
        var timer = LogicalTimer.Start(clock, default);

        Assert.Equal(clock.CurrentTick, timer.DueTick);
        Assert.True(timer.IsDue(clock));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 2)]
    [InlineData(100, 2)]
    [InlineData(101, 3)]
    public void PositiveTimeoutRoundsUpToFirstEligibleTick(
        long milliseconds,
        ulong expectedTicks)
    {
        var clock = new FakeClock(new Duration(50), Epoch);
        var timer = LogicalTimer.Start(clock, new Duration(milliseconds));

        Assert.Equal(expectedTicks, timer.DueTick.Value);
    }

    [Fact]
    public void TimersWithSameDueTickExpireTogetherDeterministically()
    {
        var clock = new FakeClock(new Duration(50), Epoch);
        var first = LogicalTimer.Start(clock, new Duration(51));
        var second = LogicalTimer.Start(clock, new Duration(100));

        Assert.Equal(first.DueTick, second.DueTick);

        clock.AdvanceTicks(1);
        Assert.False(first.IsDue(clock));
        Assert.False(second.IsDue(clock));

        clock.AdvanceTicks(1);
        Assert.True(first.IsDue(clock));
        Assert.True(second.IsDue(clock));
    }

    [Fact]
    public void CancelledTimerNeverBecomesDue()
    {
        var clock = new FakeClock(new Duration(50), Epoch);
        var timer = LogicalTimer.Start(clock, new Duration(50)).Cancel();

        Assert.True(timer.IsCancelled);
        clock.AdvanceTicks(10);
        Assert.False(timer.IsDue(clock));
        Assert.Equal(timer, timer.Cancel());
    }

    [Fact]
    public void DefaultTimerIsRejected()
    {
        var clock = new FakeClock(new Duration(50), Epoch);
        var timer = default(LogicalTimer);

        Assert.Throws<InvalidOperationException>(() => _ = timer.DueTick);
        Assert.Throws<InvalidOperationException>(() => _ = timer.IsCancelled);
        Assert.Throws<InvalidOperationException>(() => timer.IsDue(clock));
        Assert.Throws<InvalidOperationException>(() => timer.Cancel());
    }

    [Fact]
    public void TimerRejectsDueTickOverflow()
    {
        var clock = new FakeClock(
            new Duration(50),
            Epoch,
            new Tick(ulong.MaxValue),
            default);

        Assert.Throws<OverflowException>(
            () => LogicalTimer.Start(clock, new Duration(1)));
    }

    [Fact]
    public void WallClockAdjustmentDoesNotChangeCoreTimer()
    {
        var clock = new FakeClock(new Duration(50), Epoch);
        var timer = LogicalTimer.Start(clock, new Duration(100));

        clock.SetUtc(Epoch.AddYears(100));
        Assert.False(timer.IsDue(clock));

        clock.SetUtc(Epoch.AddYears(-10));
        Assert.False(timer.IsDue(clock));

        clock.AdvanceTicks(2);
        Assert.True(timer.IsDue(clock));
    }

    [Fact]
    public void OfflineElapsedTreatsClockRollbackAsZero()
    {
        var elapsed = OfflineTime.CalculateElapsed(
            Epoch,
            Epoch.AddMinutes(-5),
            Duration.FromSeconds(3600));

        Assert.True(elapsed.IsZero);
    }

    [Fact]
    public void OfflineElapsedFloorsSubMillisecondAndAppliesBusinessCap()
    {
        var current = Epoch
            .AddHours(3)
            .AddTicks(9999);

        var capped = OfflineTime.CalculateElapsed(
            Epoch,
            current,
            Duration.FromSeconds(3600));

        var uncappedSubMillisecond = OfflineTime.CalculateElapsed(
            Epoch,
            Epoch.AddTicks(9999),
            new Duration(100));

        Assert.Equal(3_600_000, capped.Milliseconds);
        Assert.Equal(0, uncappedSubMillisecond.Milliseconds);
    }
}
