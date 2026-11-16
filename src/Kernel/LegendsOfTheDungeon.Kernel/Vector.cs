namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示三维向量。世界位置差向量以米为单位；其他调用场景可按其明确单位解释。
/// </summary>
public readonly record struct Vector
{
    /// <summary>
    /// 向量分量比较的默认绝对容差；该值无单位，调用方应确保比较双方采用同一单位。
    /// </summary>
    public const double DefaultTolerance = 1e-9;

    /// <summary>
    /// 创建有限三维向量。
    /// </summary>
    /// <param name="x">X 分量。</param>
    /// <param name="y">Y 分量。</param>
    /// <param name="z">Z 分量。</param>
    /// <exception cref="ArgumentOutOfRangeException">任一分量为 NaN 或 Infinity 时抛出。</exception>
    public Vector(double x, double y, double z)
    {
        X = GeometryMath.RequireFinite(x, nameof(x));
        Y = GeometryMath.RequireFinite(y, nameof(y));
        Z = GeometryMath.RequireFinite(z, nameof(z));
    }

    /// <summary>
    /// 获取 X 分量。
    /// </summary>
    public double X { get; }

    /// <summary>
    /// 获取 Y 分量。
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// 获取 Z 分量。
    /// </summary>
    public double Z { get; }

    /// <summary>
    /// 获取欧氏长度。
    /// </summary>
    /// <exception cref="OverflowException">有限分量的真实长度超过 Double 有限范围时抛出。</exception>
    public double Length => GeometryMath.Hypot3(X, Y, Z, "向量长度计算");

    /// <summary>
    /// 返回当前向量与另一向量的和。
    /// </summary>
    /// <param name="other">另一个有限向量。</param>
    /// <returns>分量相加后的向量。</returns>
    /// <exception cref="OverflowException">任一分量相加产生非有限结果时抛出。</exception>
    public Vector Add(Vector other)
    {
        return new Vector(
            GeometryMath.Add(X, other.X, "向量 X 加法"),
            GeometryMath.Add(Y, other.Y, "向量 Y 加法"),
            GeometryMath.Add(Z, other.Z, "向量 Z 加法"));
    }

    /// <summary>
    /// 返回当前向量减去另一向量的结果。
    /// </summary>
    /// <param name="other">另一个有限向量。</param>
    /// <returns>分量相减后的向量。</returns>
    /// <exception cref="OverflowException">任一分量相减产生非有限结果时抛出。</exception>
    public Vector Subtract(Vector other)
    {
        return new Vector(
            GeometryMath.Subtract(X, other.X, "向量 X 减法"),
            GeometryMath.Subtract(Y, other.Y, "向量 Y 减法"),
            GeometryMath.Subtract(Z, other.Z, "向量 Z 减法"));
    }

    /// <summary>
    /// 按有限标量缩放向量。
    /// </summary>
    /// <param name="scalar">有限缩放因子。</param>
    /// <returns>缩放后的向量。</returns>
    /// <exception cref="ArgumentOutOfRangeException">标量为 NaN 或 Infinity 时抛出。</exception>
    /// <exception cref="OverflowException">有限输入相乘产生非有限结果时抛出。</exception>
    public Vector Scale(double scalar)
    {
        GeometryMath.RequireFinite(scalar, nameof(scalar));

        return new Vector(
            GeometryMath.Multiply(X, scalar, "向量 X 缩放"),
            GeometryMath.Multiply(Y, scalar, "向量 Y 缩放"),
            GeometryMath.Multiply(Z, scalar, "向量 Z 缩放"));
    }

    /// <summary>
    /// 将非零向量归一化为长度 1。
    /// </summary>
    /// <returns>同方向单位向量。</returns>
    /// <exception cref="InvalidOperationException">当前向量长度为 0 时抛出。</exception>
    /// <exception cref="OverflowException">长度无法表示为有限 Double 时抛出。</exception>
    public Vector Normalize()
    {
        var length = Length;
        if (length == 0)
        {
            throw new InvalidOperationException("零长度向量没有可定义的归一化方向。");
        }

        return new Vector(X / length, Y / length, Z / length);
    }

    /// <summary>
    /// 判断两个向量的每个分量是否都在指定绝对容差内。
    /// </summary>
    /// <param name="other">待比较向量。</param>
    /// <param name="tolerance">非负有限绝对容差。</param>
    /// <returns>三个分量都在容差内时返回 true。</returns>
    /// <exception cref="ArgumentOutOfRangeException">容差为负数、NaN 或 Infinity 时抛出。</exception>
    public bool NearlyEquals(Vector other, double tolerance = DefaultTolerance)
    {
        GeometryMath.RequireNonNegativeFinite(tolerance, nameof(tolerance));

        return GeometryMath.DifferenceWithin(X, other.X, tolerance)
            && GeometryMath.DifferenceWithin(Y, other.Y, tolerance)
            && GeometryMath.DifferenceWithin(Z, other.Z, tolerance);
    }

    /// <summary>
    /// 根据水平 Yaw 创建右手坐标系中的单位方向向量。
    /// </summary>
    /// <param name="yaw">Yaw=0 指向 +X；正方向绕 +Z 逆时针朝 +Y。</param>
    /// <returns>Z=0 的水平单位向量。</returns>
    public static Vector FromYaw(Angle yaw)
    {
        return new Vector(Math.Cos(yaw.Radians), Math.Sin(yaw.Radians), 0);
    }
}
