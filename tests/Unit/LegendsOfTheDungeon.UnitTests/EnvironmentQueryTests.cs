using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.Environment;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class EnvironmentQueryTests
{
    private static readonly Position Start = new(0.5, 0.5, 0);
    private static readonly Position Goal = new(4.5, 0.5, 0);

    [Fact]
    public void TerrainAndCollisionUseMetresAndRejectUnknownCoverage()
    {
        ITerrainQuery terrain = new PlanarGridEnvironment(-2, -2, 5, 3, 1, 7.5,
            [new PlaneCell(2, 0)], environmentVersion: 42);
        ICollisionQuery collision = (ICollisionQuery)terrain;
        var surface = terrain.SampleSurface(new Position(-0.1, -0.1, 88));
        Assert.Equal(EnvironmentQueryStatus.Success, surface.Status);
        Assert.Equal(new Position(-0.1, -0.1, 7.5), surface.Surface);
        Assert.Equal(42, surface.EnvironmentVersion);
        Assert.Equal(EnvironmentQueryStatus.OutsideBounds,
            terrain.SampleSurface(new Position(-2.01, 0, 0)).Status);
        Assert.Equal(EnvironmentQueryStatus.OutsideBounds,
            collision.TraceSegment(Start, new Position(99, 0, 0)).Status);
        var blocked = collision.TraceSegment(Start, Goal);
        Assert.Equal(EnvironmentQueryStatus.Success, blocked.Status);
        Assert.True(blocked.IsBlocked);
        Assert.Equal(42, blocked.EnvironmentVersion);
        Assert.False(collision.TraceSegment(Start, new Position(1.5, 0.5, 0)).IsBlocked);
    }

    [Fact]
    public void NavigationDetoursObstacleAndNeverInventsDirectRoute()
    {
        var adapter = new PlanarGridEnvironment(0, 0, 5, 3, 1, 0,
            [new PlaneCell(2, 0)], 8);
        INavigationQuery navigation = adapter;
        var path = navigation.FindPath(new NavigationRequest(Start, Goal, 5));
        Assert.Equal(EnvironmentQueryStatus.Success, path.Status);
        Assert.Equal(5, path.RequestVersion);
        Assert.Equal(8, path.EnvironmentVersion);
        Assert.Equal(Start, path.Waypoints[0]);
        Assert.Equal(Goal, path.Waypoints[^1]);
        Assert.True(path.Waypoints.Count > 6);
        for (var i = 1; i < path.Waypoints.Count; i++)
        {
            var collision = adapter.TraceSegment(path.Waypoints[i - 1], path.Waypoints[i]);
            Assert.Equal(EnvironmentQueryStatus.Success, collision.Status);
            Assert.False(collision.IsBlocked);
        }

        Assert.Equal(path.Waypoints, navigation.FindPath(
            new NavigationRequest(Start, Goal, 5)).Waypoints);
    }

    [Fact]
    public void SealedWallIsNoPathAndBlockedEndpointsAreNotSuccess()
    {
        var adapter = new PlanarGridEnvironment(0, 0, 2, 2, 1, 0,
            [new PlaneCell(1, 0), new PlaneCell(1, 1), new PlaneCell(1, 2)]);
        var noPath = adapter.FindPath(new NavigationRequest(
            new Position(0.5, 1.5, 0), new Position(2.5, 1.5, 0), 1));
        Assert.Equal(EnvironmentQueryStatus.NoPath, noPath.Status);
        Assert.Empty(noPath.Waypoints);
        Assert.Equal(EnvironmentQueryStatus.Blocked, adapter.FindPath(new NavigationRequest(
            new Position(1.5, 1.5, 0), new Position(2.5, 1.5, 0), 2)).Status);
        Assert.Equal(EnvironmentQueryStatus.OutsideBounds, adapter.FindPath(new NavigationRequest(
            Start, new Position(9, 9, 0), 3)).Status);
    }

    [Fact]
    public void DeadlineBudgetAndCancellationAreDistinctFromNoPath()
    {
        var adapter = new PlanarGridEnvironment(0, 0, 5, 3, 1, 0, []);
        Assert.Equal(EnvironmentQueryStatus.TimedOut, adapter.FindPath(
            new NavigationRequest(Start, Goal, 7, CurrentTick: 10, DeadlineTick: 10)).Status);
        Assert.Equal(EnvironmentQueryStatus.BudgetExceeded, adapter.FindPath(
            new NavigationRequest(Start, Goal, 8, MaxExpandedCells: 1)).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(EnvironmentQueryStatus.Cancelled, adapter.FindPath(
            new NavigationRequest(Start, Goal, 9), cancellation.Token).Status);
        Assert.Equal(EnvironmentQueryStatus.Cancelled,
            adapter.SampleSurface(Start, cancellation.Token).Status);
        Assert.Equal(EnvironmentQueryStatus.Cancelled,
            adapter.TraceSegment(Start, Goal, cancellation.Token).Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.FindPath(
            new NavigationRequest(Start, Goal, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.FindPath(
            new NavigationRequest(Start, Goal, 1, MaxExpandedCells: 0)));
    }

    [Fact]
    public void ObstacleInputsAndPathOutputsAreDefensivelyCopied()
    {
        var obstacles = new List<PlaneCell> { new(2, 0) };
        var adapter = new PlanarGridEnvironment(0, 0, 5, 3, 1, 0, obstacles);
        obstacles.Clear();
        Assert.True(adapter.TraceSegment(Start, Goal).IsBlocked);
        var source = new List<Position> { Start, Goal };
        var snapshot = new NavigationPathResult(EnvironmentQueryStatus.Success, 17, 1, source);
        source[0] = new Position(99, 0, 0);
        Assert.Equal(Start, snapshot.Waypoints[0]);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Position>)snapshot.Waypoints)[0] = new Position(88, 0, 0));
        Assert.Throws<ArgumentException>(() => new NavigationPathResult(
            EnvironmentQueryStatus.NoPath, 17, 1, [Start]));
    }

    [Fact]
    public void PortFakeCannotSilentlyTurnTimeoutIntoSuccess()
    {
        ITerrainQuery terrain = new PortFake();
        ICollisionQuery collision = (ICollisionQuery)terrain;
        INavigationQuery navigation = (INavigationQuery)terrain;
        Assert.Equal(EnvironmentQueryStatus.OutsideBounds, terrain.SampleSurface(Start).Status);
        Assert.Equal(EnvironmentQueryStatus.Blocked, collision.TraceSegment(Start, Goal).Status);
        var result = navigation.FindPath(new NavigationRequest(Start, Goal, 101));
        Assert.Equal(EnvironmentQueryStatus.TimedOut, result.Status);
        Assert.Equal(101, result.RequestVersion);
        Assert.Empty(result.Waypoints);
    }

    [Fact]
    public void GridConfigurationAndCellBoundariesAreStrict()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanarGridEnvironment(0, 0, 129, 1, 1, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanarGridEnvironment(0, 0, 1, 1, 0, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanarGridEnvironment(0, 0, 1, 1, 1, 0, [new PlaneCell(3, 0)]));
        var adapter = new PlanarGridEnvironment(-2, -2, 1, 1, 1, 0, []);
        Assert.Equal(EnvironmentQueryStatus.Success,
            adapter.SampleSurface(new Position(-0.001, -0.001, 0)).Status);
        Assert.Equal(EnvironmentQueryStatus.OutsideBounds,
            adapter.SampleSurface(new Position(-2.001, 0, 0)).Status);
        Assert.Equal(EnvironmentQueryStatus.Success,
            adapter.SampleSurface(new Position(1, 1, 0)).Status);
    }

    private sealed class PortFake : ITerrainQuery, ICollisionQuery, INavigationQuery
    {
        public TerrainQueryResult SampleSurface(Position point, CancellationToken cancellationToken = default) =>
            new(EnvironmentQueryStatus.OutsideBounds, default, 1);

        public CollisionQueryResult TraceSegment(
            Position start, Position end, CancellationToken cancellationToken = default) =>
            new(EnvironmentQueryStatus.Blocked, true, 1);

        public NavigationPathResult FindPath(
            NavigationRequest request, CancellationToken cancellationToken = default) =>
            new(EnvironmentQueryStatus.TimedOut, request.RequestVersion, 1, Array.Empty<Position>());
    }
}
