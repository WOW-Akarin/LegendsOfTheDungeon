namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示绕 +Z 轴的方向角，单位弧度，canonical 范围为 [0, 2π)。
/// </summary>
public readonly record struct Angle
{
    /// <summary>
    /// 创建并归一化有限弧度角。
    /// </summary>
    /// <param name="radians">任意有限弧度值。</param>
    /// <exception cref="ArgumentOutOfRangeException">输入为 NaN 或 Infinity 时抛出。</exception>
    public Angle(double radians)
    {
        Radians = GeometryMath.NormalizeRadians(radians);
    }

    /// <summary>
    /// 获取 canonical 弧度值，范围为 [0, 2π)。
    /// </summary>
    public double Radians { get; }
}
