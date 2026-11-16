namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示右手坐标系中的轴对齐闭区间边界盒。
/// </summary>
public readonly record struct Bounds
{
    /// <summary>
    /// 创建轴对齐边界盒。
    /// </summary>
    /// <param name="minimum">三个轴的最小闭区间坐标。</param>
    /// <param name="maximum">三个轴的最大闭区间坐标。</param>
    /// <exception cref="ArgumentException">任一轴的 minimum 大于 maximum 时抛出。</exception>
    public Bounds(Position minimum, Position maximum)
    {
        if (minimum.X > maximum.X
            || minimum.Y > maximum.Y
            || minimum.Z > maximum.Z)
        {
            throw new ArgumentException("Bounds 每个轴都必须满足 minimum ≤ maximum。", nameof(maximum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// 获取三个轴的最小闭区间坐标。
    /// </summary>
    public Position Minimum { get; }

    /// <summary>
    /// 获取三个轴的最大闭区间坐标。
    /// </summary>
    public Position Maximum { get; }

    /// <summary>
    /// 判断位置是否位于闭区间边界盒内部或表面。
    /// </summary>
    /// <param name="position">待判断位置。</param>
    /// <returns>三个轴都位于闭区间内时返回 true。</returns>
    public bool Contains(Position position)
    {
        return position.X >= Minimum.X
            && position.X <= Maximum.X
            && position.Y >= Minimum.Y
            && position.Y <= Maximum.Y
            && position.Z >= Minimum.Z
            && position.Z <= Maximum.Z;
    }

    /// <summary>
    /// 计算位置到边界盒的最短欧氏距离；内部或表面位置距离为 0。
    /// </summary>
    /// <param name="position">待计算位置。</param>
    /// <returns>到闭区间边界盒的最短距离，单位米。</returns>
    /// <exception cref="OverflowException">有限坐标差或最终距离超出 Double 有限范围时抛出。</exception>
    public Distance DistanceTo(Position position)
    {
        var deltaX = AxisDistance(position.X, Minimum.X, Maximum.X, "Bounds X 距离");
        var deltaY = AxisDistance(position.Y, Minimum.Y, Maximum.Y, "Bounds Y 距离");
        var deltaZ = AxisDistance(position.Z, Minimum.Z, Maximum.Z, "Bounds Z 距离");

        return new Distance(GeometryMath.Hypot3(deltaX, deltaY, deltaZ, "Bounds 最短距离"));
    }

    /// <summary>
    /// 判断位置是否在边界盒或其外部指定距离的闭区间内。
    /// </summary>
    /// <param name="position">待判断位置。</param>
    /// <param name="maximumDistance">允许离边界盒的最大距离。</param>
    /// <returns>最短距离小于等于最大距离时返回 true。</returns>
    public bool IsWithin(Position position, Distance maximumDistance)
    {
        return DistanceTo(position).Meters <= maximumDistance.Meters;
    }

    private static double AxisDistance(
        double value,
        double minimum,
        double maximum,
        string operation)
    {
        if (value < minimum)
        {
            return GeometryMath.Subtract(minimum, value, operation);
        }

        if (value > maximum)
        {
            return GeometryMath.Subtract(value, maximum, operation);
        }

        return 0;
    }
}
