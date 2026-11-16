namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示右手世界坐标系中的三维位置，单位为米，+Z 向上。
/// </summary>
public readonly record struct Position
{
    /// <summary>
    /// 创建有限世界位置。
    /// </summary>
    /// <param name="x">X 坐标，单位米。</param>
    /// <param name="y">Y 坐标，单位米。</param>
    /// <param name="z">Z 坐标，单位米。</param>
    /// <exception cref="ArgumentOutOfRangeException">任一坐标为 NaN 或 Infinity 时抛出。</exception>
    public Position(double x, double y, double z)
    {
        X = GeometryMath.RequireFinite(x, nameof(x));
        Y = GeometryMath.RequireFinite(y, nameof(y));
        Z = GeometryMath.RequireFinite(z, nameof(z));
    }

    /// <summary>
    /// 获取 X 坐标，单位米。
    /// </summary>
    public double X { get; }

    /// <summary>
    /// 获取 Y 坐标，单位米。
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// 获取 Z 坐标，单位米。
    /// </summary>
    public double Z { get; }

    /// <summary>
    /// 计算到另一位置的欧氏距离。
    /// </summary>
    /// <param name="other">目标位置。</param>
    /// <returns>非负距离，单位米。</returns>
    /// <exception cref="OverflowException">有限坐标的差或距离超出 Double 有限范围时抛出。</exception>
    public Distance DistanceTo(Position other)
    {
        var deltaX = GeometryMath.Subtract(other.X, X, "位置 X 差");
        var deltaY = GeometryMath.Subtract(other.Y, Y, "位置 Y 差");
        var deltaZ = GeometryMath.Subtract(other.Z, Z, "位置 Z 差");

        return new Distance(GeometryMath.Hypot3(deltaX, deltaY, deltaZ, "位置距离计算"));
    }

    /// <summary>
    /// 判断另一位置是否位于给定闭区间距离内。
    /// </summary>
    /// <param name="other">目标位置。</param>
    /// <param name="maximumDistance">允许的最大距离，边界相等时仍视为范围内。</param>
    /// <returns>实际距离小于等于最大距离时返回 true。</returns>
    public bool IsWithin(Position other, Distance maximumDistance)
    {
        return DistanceTo(other).Meters <= maximumDistance.Meters;
    }

    /// <summary>
    /// 使用有限向量平移当前位置。
    /// </summary>
    /// <param name="offset">平移向量；位置场景下分量单位为米。</param>
    /// <returns>平移后的新位置。</returns>
    /// <exception cref="OverflowException">有限坐标相加产生非有限结果时抛出。</exception>
    public Position Translate(Vector offset)
    {
        return new Position(
            GeometryMath.Add(X, offset.X, "位置 X 平移"),
            GeometryMath.Add(Y, offset.Y, "位置 Y 平移"),
            GeometryMath.Add(Z, offset.Z, "位置 Z 平移"));
    }

    /// <summary>
    /// 计算从当前位置指向另一位置的位移向量。
    /// </summary>
    /// <param name="other">目标位置。</param>
    /// <returns>目标位置减当前位置的三维向量，单位米。</returns>
    /// <exception cref="OverflowException">有限坐标相减产生非有限结果时抛出。</exception>
    public Vector VectorTo(Position other)
    {
        return new Vector(
            GeometryMath.Subtract(other.X, X, "位置 X 向量差"),
            GeometryMath.Subtract(other.Y, Y, "位置 Y 向量差"),
            GeometryMath.Subtract(other.Z, Z, "位置 Z 向量差"));
    }
}
