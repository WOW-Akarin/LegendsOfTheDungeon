using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Runtime.Abstractions;

/// <summary>通用环境查询状态；失败必须显式处理，不能将无路或未知地图视为直线可通行。</summary>
public enum EnvironmentQueryStatus
{
    /// <summary>未执行或无效的默认返回值。</summary>
    Unknown = 0,
    /// <summary>查询成功，碰撞结果仍需读取 IsBlocked。</summary>
    Success,
    /// <summary>坐标不在当前环境数据覆盖范围。</summary>
    OutsideBounds,
    /// <summary>碰到不可通行障碍或起终点被障碍占据。</summary>
    Blocked,
    /// <summary>合法坐标间没有连通路径。</summary>
    NoPath,
    /// <summary>确定性的节点预算耗尽，不等价于无路。</summary>
    BudgetExceeded,
    /// <summary>请求的逻辑截止 Tick 已到。</summary>
    TimedOut,
    /// <summary>请求取消，不能把旧结果提交到世界。</summary>
    Cancelled
}

/// <summary>地面采样：Surface 以米为单位，失败时不能使用 Surface。</summary>
/// <param name="Status">查询状态。</param>
/// <param name="Surface">成功时的地面位置。</param>
/// <param name="EnvironmentVersion">环境数据快照版本。</param>
public readonly record struct TerrainQueryResult(
    EnvironmentQueryStatus Status, Position Surface, long EnvironmentVersion);

/// <summary>障碍线段检测结果；成功时 IsBlocked 表示是否碰撞。</summary>
/// <param name="Status">查询状态；成功包含清晰和受阻两种结果。</param>
/// <param name="IsBlocked">是否与固体相交；仅 Status=Success 有效。</param>
/// <param name="EnvironmentVersion">环境快照版本。</param>
public readonly record struct CollisionQueryResult(
    EnvironmentQueryStatus Status, bool IsBlocked, long EnvironmentVersion);

/// <summary>以米为单位查询地形地面；实现必须只读、无领域 World 写权限。</summary>
public interface ITerrainQuery
{
    /// <summary>查询 XY 投影位置对应的地面，取消时明确返回 Cancelled。</summary>
    /// <param name="point">三维世界位置，单位米。</param>
    /// <param name="cancellationToken">查询取消信号。</param>
    /// <returns>版本化的地面结果。</returns>
    TerrainQueryResult SampleSurface(Position point, CancellationToken cancellationToken = default);
}

/// <summary>通用障碍检测；不规定地图文件格式。</summary>
public interface ICollisionQuery
{
    /// <summary>检测世界米坐标的闭线段是否与固体相交。</summary>
    /// <param name="start">线段起点，单位米。</param>
    /// <param name="destination">线段终点，单位米。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>清晰或阻挡的版本化状态；区域未知不得默认放行。</returns>
    CollisionQueryResult TraceSegment(Position start, Position destination, CancellationToken cancellationToken = default);
}

/// <summary>路径请求；RequestVersion 由调用方递增，异步回送时必须由 P18/P28 验证是否仍最新。</summary>
/// <param name="Start">起点，米。</param>
/// <param name="Goal">终点，米。</param>
/// <param name="RequestVersion">调用方连续请求版本，必须大于零。</param>
/// <param name="MaxExpandedCells">允许搜索的最大格数，1–16384。</param>
/// <param name="CurrentTick">提交查询时已观察到的逻辑 Tick。</param>
/// <param name="DeadlineTick">可选绝对逻辑截止 Tick，不能使用墙上时间推断。</param>
public readonly record struct NavigationRequest(
    Position Start, Position Goal, long RequestVersion,
    int MaxExpandedCells = 4096, long CurrentTick = 0, long? DeadlineTick = null);

/// <summary>冻结的导航结果；路径不暴露适配器内部可变数组。</summary>
public sealed class NavigationPathResult
{
    /// <summary>创建不可变路径；失败时 Waypoints 必须为空。</summary>
    /// <param name="status">查询状态。</param>
    /// <param name="requestVersion">原样返回的请求版本。</param>
    /// <param name="environmentVersion">环境快照版本。</param>
    /// <param name="waypoints">成功时依次经过的米坐标。</param>
    public NavigationPathResult(
        EnvironmentQueryStatus status, long requestVersion, long environmentVersion,
        IEnumerable<Position> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(environmentVersion);
        var copy = waypoints.ToArray();
        if (status == EnvironmentQueryStatus.Success && copy.Length < 2)
        {
            throw new ArgumentException("成功路径必须至少包含起点和终点。", nameof(waypoints));
        }

        if (status != EnvironmentQueryStatus.Success && copy.Length != 0)
        {
            throw new ArgumentException("失败路径不得包含可被误用的路径点。", nameof(waypoints));
        }

        Status = status;
        RequestVersion = requestVersion;
        EnvironmentVersion = environmentVersion;
        Waypoints = Array.AsReadOnly(copy);
    }

    /// <summary>成功、无路、超时、取消或预算耗尽等明确状态。</summary>
    public EnvironmentQueryStatus Status { get; }

    /// <summary>原始请求版本；客户端必须拒绝过期回执。</summary>
    public long RequestVersion { get; }

    /// <summary>环境数据版本；调用方必须与当前地图版本比较。</summary>
    public long EnvironmentVersion { get; }

    /// <summary>冻结的路径点；成功时至少包括起点和终点。</summary>
    public IReadOnlyList<Position> Waypoints { get; }
}

/// <summary>只读导航 Port；后台查询仅返回值快照，不得在后台直接更改 Transform。</summary>
public interface INavigationQuery
{
    /// <summary>确定性求路径；返回值包含请求及环境版本，取消、逻辑超时与无路分别编码。</summary>
    /// <param name="request">米坐标、版本与确定性预算。</param>
    /// <param name="cancellationToken">可取消已启动的计算。</param>
    /// <returns>防御性复制的路径结果。</returns>
    NavigationPathResult FindPath(NavigationRequest request, CancellationToken cancellationToken = default);
}
