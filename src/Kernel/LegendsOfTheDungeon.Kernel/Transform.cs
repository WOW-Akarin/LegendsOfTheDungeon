namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示世界位置与水平 Yaw 的组合变换；Yaw=0 指向 +X，正方向朝 +Y。
/// </summary>
public readonly record struct Transform
{
    /// <summary>
    /// 创建位置与方向组合。
    /// </summary>
    /// <param name="position">有限世界位置。</param>
    /// <param name="yaw">已归一化的方向角。</param>
    public Transform(Position position, Angle yaw)
    {
        Position = position;
        Yaw = yaw;
    }

    /// <summary>
    /// 获取世界位置。
    /// </summary>
    public Position Position { get; }

    /// <summary>
    /// 获取水平 Yaw，单位弧度。
    /// </summary>
    public Angle Yaw { get; }

    /// <summary>
    /// 获取当前 Yaw 对应的水平单位前向向量。
    /// </summary>
    /// <returns>右手坐标系中的水平单位方向。</returns>
    public Vector Forward()
    {
        return Vector.FromYaw(Yaw);
    }
}
