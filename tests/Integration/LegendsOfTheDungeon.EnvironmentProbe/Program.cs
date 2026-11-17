using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.Environment;

var adapter = new PlanarGridEnvironment(0, 0, 5, 3, 1, 0, [new PlaneCell(2, 0)], 99);
var (terrain, collision, navigation) = BindPorts(adapter);
var start = new Position(0.5, 0.5, 0);
var goal = new Position(4.5, 0.5, 0);
Check(terrain.SampleSurface(start).Status == EnvironmentQueryStatus.Success,
    "真实地形采样失败");
Check(collision.TraceSegment(start, goal).IsBlocked,
    "障碍直线错误放行");

var found = navigation.FindPath(new NavigationRequest(start, goal, 11));
Check(found.Status == EnvironmentQueryStatus.Success &&
    found.RequestVersion == 11 && found.EnvironmentVersion == 99 &&
    found.Waypoints.Count > 4, "导航无法实际绕过障碍");
for (var i = 1; i < found.Waypoints.Count; i++)
{
    var segment = collision.TraceSegment(found.Waypoints[i - 1], found.Waypoints[i]);
    Check(segment.Status == EnvironmentQueryStatus.Success && !segment.IsBlocked,
        "路径段穿越障碍");
}

Console.WriteLine("p23-adapter-detour PASS");
var sealedWall = new PlanarGridEnvironment(0, 0, 2, 2, 1, 0,
    [new PlaneCell(1, 0), new PlaneCell(1, 1), new PlaneCell(1, 2)]);
var noRoute = sealedWall.FindPath(new NavigationRequest(
    new Position(0.5, 1.5, 0), new Position(2.5, 1.5, 0), 12));
Check(noRoute.Status == EnvironmentQueryStatus.NoPath && noRoute.Waypoints.Count == 0,
    "真实无路情形误报成功");
Console.WriteLine("p23-adapter-unreachable PASS");
Check(navigation.FindPath(new NavigationRequest(
    start, goal, 13, MaxExpandedCells: 1)).Status == EnvironmentQueryStatus.BudgetExceeded,
    "节点预算没有阻断");
Check(navigation.FindPath(new NavigationRequest(
    start, goal, 14, CurrentTick: 21, DeadlineTick: 21)).Status == EnvironmentQueryStatus.TimedOut,
    "绝对逻辑截止点没有阻断");
Console.WriteLine("p23-adapter-budget-deadline PASS");
using var token = new CancellationTokenSource();
token.Cancel();
Check(navigation.FindPath(new NavigationRequest(start, goal, 15), token.Token).Status ==
    EnvironmentQueryStatus.Cancelled, "取消查询错误成功");
Console.WriteLine("p23-adapter-cancellation PASS");
Check(navigation.FindPath(new NavigationRequest(
    start, new Position(500, 0, 0), 16)).Status == EnvironmentQueryStatus.OutsideBounds,
    "未知区域不能被当成可达");
Console.WriteLine("p23-adapter-outside-world PASS");
Console.WriteLine("P23_ENVIRONMENT_INTEGRATION_PASS");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

// 强制经公开 Port 注入，避免测试消费者依赖具体适配器。
static (ITerrainQuery Terrain, ICollisionQuery Collision, INavigationQuery Navigation) BindPorts(
    PlanarGridEnvironment source) => (source, source, source);
