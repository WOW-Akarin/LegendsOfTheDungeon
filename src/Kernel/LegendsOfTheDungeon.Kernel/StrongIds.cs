using System.Globalization;

namespace LegendsOfTheDungeon.Kernel;

internal static class StrongIdCodec
{
    internal static ulong RequireNonZero(ulong value, string paramName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "值必须大于 0。");
        }

        return value;
    }

    internal static ulong GetValueOrThrow(ulong value, string typeName)
    {
        if (value == 0)
        {
            throw new InvalidOperationException(typeName + " 为空，不能作为有效标识或序列化值使用。");
        }

        return value;
    }

    internal static bool TryParse(string? text, out ulong value)
    {
        value = 0;

        if (string.IsNullOrEmpty(text)
            || (text.Length > 1 && text[0] == '0')
            || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed == 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    internal static ulong Parse(string text, string typeName)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryParse(text, out var value))
        {
            throw new FormatException(typeName + " 必须是无符号 64 位范围内、非零且无符号/空白/前导零的十进制文本。");
        }

        return value;
    }

    internal static string Format(ulong value, string typeName)
    {
        return GetValueOrThrow(value, typeName).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 运行时实体标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct EntityId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建实体值。
    /// </summary>
    /// <param name="value">实体的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public EntityId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(EntityId));

    /// <summary>
    /// 从 canonical 十进制文本解析实体值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的实体值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static EntityId Parse(string text)
    {
        return new EntityId(StrongIdCodec.Parse(text, nameof(EntityId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析实体值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out EntityId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new EntityId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(EntityId));
    }
}

/// <summary>
/// 可持续关联的行动主体标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct ActorId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建主体值。
    /// </summary>
    /// <param name="value">主体的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public ActorId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(ActorId));

    /// <summary>
    /// 从 canonical 十进制文本解析主体值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的主体值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static ActorId Parse(string text)
    {
        return new ActorId(StrongIdCodec.Parse(text, nameof(ActorId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析主体值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out ActorId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new ActorId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(ActorId));
    }
}

/// <summary>
/// 世界标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct WorldId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建世界值。
    /// </summary>
    /// <param name="value">世界的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public WorldId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(WorldId));

    /// <summary>
    /// 从 canonical 十进制文本解析世界值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的世界值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static WorldId Parse(string text)
    {
        return new WorldId(StrongIdCodec.Parse(text, nameof(WorldId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析世界值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out WorldId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new WorldId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(WorldId));
    }
}

/// <summary>
/// 能力定义标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct AbilityId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建能力值。
    /// </summary>
    /// <param name="value">能力的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public AbilityId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(AbilityId));

    /// <summary>
    /// 从 canonical 十进制文本解析能力值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的能力值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static AbilityId Parse(string text)
    {
        return new AbilityId(StrongIdCodec.Parse(text, nameof(AbilityId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析能力值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out AbilityId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new AbilityId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(AbilityId));
    }
}

/// <summary>
/// 静态物品定义标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct ItemDefinitionId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建物品定义值。
    /// </summary>
    /// <param name="value">物品定义的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public ItemDefinitionId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(ItemDefinitionId));

    /// <summary>
    /// 从 canonical 十进制文本解析物品定义值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的物品定义值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static ItemDefinitionId Parse(string text)
    {
        return new ItemDefinitionId(StrongIdCodec.Parse(text, nameof(ItemDefinitionId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析物品定义值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out ItemDefinitionId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new ItemDefinitionId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(ItemDefinitionId));
    }
}

/// <summary>
/// 运行时或持久化物品实例标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct ItemInstanceId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建物品实例值。
    /// </summary>
    /// <param name="value">物品实例的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public ItemInstanceId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(ItemInstanceId));

    /// <summary>
    /// 从 canonical 十进制文本解析物品实例值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的物品实例值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static ItemInstanceId Parse(string text)
    {
        return new ItemInstanceId(StrongIdCodec.Parse(text, nameof(ItemInstanceId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析物品实例值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out ItemInstanceId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new ItemInstanceId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(ItemInstanceId));
    }
}

/// <summary>
/// 通用异步或后台工作标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct TaskId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建任务值。
    /// </summary>
    /// <param name="value">任务的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public TaskId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(TaskId));

    /// <summary>
    /// 从 canonical 十进制文本解析任务值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的任务值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static TaskId Parse(string text)
    {
        return new TaskId(StrongIdCodec.Parse(text, nameof(TaskId)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析任务值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out TaskId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new TaskId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(TaskId));
    }
}

/// <summary>
/// 实体槽位复用时的代际标识。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct EntityGeneration
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建实体代际值。
    /// </summary>
    /// <param name="value">实体代际的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public EntityGeneration(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(EntityGeneration));

    /// <summary>
    /// 从 canonical 十进制文本解析实体代际值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的实体代际值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static EntityGeneration Parse(string text)
    {
        return new EntityGeneration(StrongIdCodec.Parse(text, nameof(EntityGeneration)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析实体代际值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out EntityGeneration value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new EntityGeneration(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(EntityGeneration));
    }
}

/// <summary>
/// 同一世界标识的运行世代。值 0 保留给 CLR default/empty sentinel，不是合法业务值。
/// </summary>
public readonly record struct WorldEpoch
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建世界世代值。
    /// </summary>
    /// <param name="value">世界世代的稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 为 0 时抛出。</exception>
    public WorldEpoch(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定无符号整数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(WorldEpoch));

    /// <summary>
    /// 从 canonical 十进制文本解析世界世代值。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的世界世代值。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static WorldEpoch Parse(string text)
    {
        return new WorldEpoch(StrongIdCodec.Parse(text, nameof(WorldEpoch)));
    }

    /// <summary>
    /// 尝试从 canonical 十进制文本解析世界世代值。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true；否则返回 false。</returns>
    public static bool TryParse(string? text, out WorldEpoch value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new WorldEpoch(parsed);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 输出 invariant canonical 十进制文本；empty/default 不允许序列化。
    /// </summary>
    /// <returns>稳定十进制文本。</returns>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public override string ToString()
    {
        return StrongIdCodec.Format(_value, nameof(WorldEpoch));
    }
}

