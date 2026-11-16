namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示非负有限距离，单位为米。
/// </summary>
public readonly record struct Distance
{
    /// <summary>
    /// 默认绝对比较容差，单位米。
    /// </summary>
    public const double DefaultToleranceMeters = 1e-9;

    /// <summary>
    /// 创建非负有限距离。
    /// </summary>
    /// <param name="meters">距离，单位米，必须大于等于 0 且为有限数。</param>
    /// <exception cref="ArgumentOutOfRangeException">输入为负数、NaN 或 Infinity 时抛出。</exception>
    public Distance(double meters)
    {
        Meters = GeometryMath.RequireNonNegativeFinite(meters, nameof(meters));
    }

    /// <summary>
    /// 获取距离米值。
    /// </summary>
    public double Meters { get; }

    /// <summary>
    /// 判断两个距离是否在给定绝对容差内。
    /// </summary>
    /// <param name="other">待比较距离。</param>
    /// <param name="toleranceMeters">非负有限绝对容差，单位米。</param>
    /// <returns>差值小于等于容差时返回 true。</returns>
    /// <exception cref="ArgumentOutOfRangeException">容差为负数、NaN 或 Infinity 时抛出。</exception>
    public bool NearlyEquals(
        Distance other,
        double toleranceMeters = DefaultToleranceMeters)
    {
        GeometryMath.RequireNonNegativeFinite(toleranceMeters, nameof(toleranceMeters));
        return GeometryMath.DifferenceWithin(Meters, other.Meters, toleranceMeters);
    }
}
