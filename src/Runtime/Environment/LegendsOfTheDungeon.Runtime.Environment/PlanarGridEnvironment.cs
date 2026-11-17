using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.Environment;

/// <summary>平面网格障碍的整数坐标；每个格子在 XY 上是边长 CellSize 米的闭合矩形。</summary>
/// <param name="X">X 网格坐标。</param>
/// <param name="Y">Y 网格坐标。</param>
public readonly record struct PlaneCell(int X, int Y);

/// <summary>
/// P23 真实参考环境：有限平面高度、静态实体障碍、四邻域确定性寻路。
/// 与 WoW 地图格式无关，不保存 Entity/Transform，也不声明 3D NavMesh 或动态障碍支持。
/// </summary>
public sealed class PlanarGridEnvironment : ITerrainQuery, ICollisionQuery, INavigationQuery
{
    private readonly HashSet<PlaneCell> _blocked;
    private readonly int _minX;
    private readonly int _minY;
    private readonly int _maxX;
    private readonly int _maxY;
    private readonly double _cellSize;
    private readonly double _height;
    private readonly long _version;

    /// <summary>构造只读平面及障碍快照；格范围每轴最多 128 格，防止无界搜索。</summary>
    /// <param name="minimumX">最小包含 X 格坐标。</param>
    /// <param name="minimumY">最小包含 Y 格坐标。</param>
    /// <param name="maximumX">最大包含 X 格坐标。</param>
    /// <param name="maximumY">最大包含 Y 格坐标。</param>
    /// <param name="cellSizeMeters">网格边长，单位米，范围 0.25–64。</param>
    /// <param name="groundHeightMeters">恒定地面高度，单位米。</param>
    /// <param name="blockedCells">静态不可通行格；复制并冻结。</param>
    /// <param name="environmentVersion">数据版本，必须大于零。</param>
    public PlanarGridEnvironment(
        int minimumX, int minimumY, int maximumX, int maximumY,
        double cellSizeMeters, double groundHeightMeters,
        IEnumerable<PlaneCell> blockedCells, long environmentVersion = 1)
    {
        ArgumentNullException.ThrowIfNull(blockedCells);
        if (maximumX < minimumX || maximumY < minimumY ||
            (long)maximumX - minimumX >= 128 || (long)maximumY - minimumY >= 128)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumX), "网格范围不合法或大于 128×128。");
        }

        if (!double.IsFinite(cellSizeMeters) || cellSizeMeters < 0.25 || cellSizeMeters > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSizeMeters));
        }

        if (!double.IsFinite(groundHeightMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(groundHeightMeters));
        }

        if (environmentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(environmentVersion));
        }

        _minX = minimumX;
        _minY = minimumY;
        _maxX = maximumX;
        _maxY = maximumY;
        _cellSize = cellSizeMeters;
        _height = groundHeightMeters;
        _version = environmentVersion;
        _blocked = [];
        foreach (var cell in blockedCells)
        {
            if (!Contains(cell))
            {
                throw new ArgumentOutOfRangeException(nameof(blockedCells), "障碍不属于定义网格。");
            }

            _blocked.Add(cell);
        }
    }

    /// <inheritdoc />
    public TerrainQueryResult SampleSurface(Position point, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new TerrainQueryResult(EnvironmentQueryStatus.Cancelled, default, _version);
        }

        return TryLocate(point, out _)
            ? new TerrainQueryResult(EnvironmentQueryStatus.Success,
                new Position(point.X, point.Y, _height), _version)
            : new TerrainQueryResult(EnvironmentQueryStatus.OutsideBounds, default, _version);
    }

    /// <inheritdoc />
    public CollisionQueryResult TraceSegment(
        Position start, Position end, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new CollisionQueryResult(EnvironmentQueryStatus.Cancelled, false, _version);
        }

        if (!TryLocate(start, out _) || !TryLocate(end, out _))
        {
            return new CollisionQueryResult(EnvironmentQueryStatus.OutsideBounds, false, _version);
        }

        foreach (var cell in _blocked)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new CollisionQueryResult(EnvironmentQueryStatus.Cancelled, false, _version);
            }

            if (Intersects(start, end, cell))
            {
                return new CollisionQueryResult(EnvironmentQueryStatus.Success, true, _version);
            }
        }

        return new CollisionQueryResult(EnvironmentQueryStatus.Success, false, _version);
    }

    /// <inheritdoc />
    public NavigationPathResult FindPath(
        NavigationRequest request, CancellationToken cancellationToken = default)
    {
        NavigationPathResult Failure(EnvironmentQueryStatus status) =>
            new(status, request.RequestVersion, _version, Array.Empty<Position>());

        if (request.RequestVersion <= 0 || request.MaxExpandedCells is < 1 or > 16384 ||
            request.CurrentTick < 0 || request.DeadlineTick is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "导航请求版本、预算或逻辑 Tick 非法。");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(EnvironmentQueryStatus.Cancelled);
        }

        if (request.DeadlineTick is long deadline && request.CurrentTick >= deadline)
        {
            return Failure(EnvironmentQueryStatus.TimedOut);
        }

        if (!TryLocate(request.Start, out var origin) || !TryLocate(request.Goal, out var destination))
        {
            return Failure(EnvironmentQueryStatus.OutsideBounds);
        }

        if (_blocked.Contains(origin) || _blocked.Contains(destination))
        {
            return Failure(EnvironmentQueryStatus.Blocked);
        }

        // 四邻域 BFS 保证相同输入、版本、预算下路径与失败边界稳定；不退化成穿墙直线。
        var frontier = new Queue<PlaneCell>();
        var parent = new Dictionary<PlaneCell, PlaneCell>();
        var visited = new HashSet<PlaneCell> { origin };
        frontier.Enqueue(origin);
        var expanded = 0;
        while (frontier.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure(EnvironmentQueryStatus.Cancelled);
            }

            if (expanded >= request.MaxExpandedCells)
            {
                return Failure(EnvironmentQueryStatus.BudgetExceeded);
            }

            var current = frontier.Dequeue();
            expanded++;
            if (current == destination)
            {
                var cells = new List<PlaneCell> { current };
                while (current != origin)
                {
                    current = parent[current];
                    cells.Add(current);
                }

                cells.Reverse();
                var positions = new List<Position>(cells.Count + 2) { request.Start };
                // 使用各完整格中心中继，避免从格角穿越相邻障碍；固定四邻域无对角切角。
                foreach (var cell in cells)
                {
                    positions.Add(new Position((cell.X + 0.5) * _cellSize,
                        (cell.Y + 0.5) * _cellSize, _height));
                }

                positions.Add(request.Goal);
                return new NavigationPathResult(EnvironmentQueryStatus.Success,
                    request.RequestVersion, _version, positions);
            }

            foreach (var neighbor in Neighbors(current))
            {
                if (Contains(neighbor) && !_blocked.Contains(neighbor) && visited.Add(neighbor))
                {
                    parent.Add(neighbor, current);
                    frontier.Enqueue(neighbor);
                }
            }
        }

        return Failure(EnvironmentQueryStatus.NoPath);
    }

    private bool TryLocate(Position point, out PlaneCell cell)
    {
        var x = Math.Floor(point.X / _cellSize);
        var y = Math.Floor(point.Y / _cellSize);
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            x < _minX || x > _maxX || y < _minY || y > _maxY)
        {
            cell = default;
            return false;
        }

        cell = new PlaneCell((int)x, (int)y);
        return true;
    }

    private bool Contains(PlaneCell cell) =>
        cell.X >= _minX && cell.X <= _maxX && cell.Y >= _minY && cell.Y <= _maxY;

    private static IEnumerable<PlaneCell> Neighbors(PlaneCell cell)
    {
        // 采用固定方向顺序，避免 HashSet/线程调度影响稳定路径。
        yield return new PlaneCell(cell.X, cell.Y + 1);
        yield return new PlaneCell(cell.X + 1, cell.Y);
        yield return new PlaneCell(cell.X, cell.Y - 1);
        yield return new PlaneCell(cell.X - 1, cell.Y);
    }

    private bool Intersects(Position start, Position end, PlaneCell cell)
    {
        var minX = cell.X * _cellSize;
        var minY = cell.Y * _cellSize;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var low = 0d;
        var high = 1d;
        return Clip(-dx, start.X - minX, ref low, ref high) &&
               Clip(dx, minX + _cellSize - start.X, ref low, ref high) &&
               Clip(-dy, start.Y - minY, ref low, ref high) &&
               Clip(dy, minY + _cellSize - start.Y, ref low, ref high);
    }

    private static bool Clip(double p, double q, ref double low, ref double high)
    {
        if (p == 0)
        {
            return q >= 0;
        }

        var ratio = q / p;
        if (p < 0)
        {
            if (ratio > high) return false;
            if (ratio > low) low = ratio;
        }
        else
        {
            if (ratio < low) return false;
            if (ratio < high) high = ratio;
        }

        return true;
    }
}
