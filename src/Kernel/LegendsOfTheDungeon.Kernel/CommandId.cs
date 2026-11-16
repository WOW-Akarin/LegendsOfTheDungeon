namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示一次 Command 的稳定非零标识；值 0 保留给 CLR default/empty sentinel。
/// </summary>
public readonly record struct CommandId
{
    private readonly ulong _value;

    /// <summary>
    /// 使用非零无符号整数创建 Command 标识。
    /// </summary>
    /// <param name="value">稳定数值，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">值为 0 时抛出。</exception>
    public CommandId(ulong value)
    {
        _value = StrongIdCodec.RequireNonZero(value, nameof(value));
    }

    /// <summary>
    /// 获取当前值是否为 CLR default/empty sentinel。
    /// </summary>
    public bool IsEmpty => _value == 0;

    /// <summary>
    /// 获取稳定数值；empty/default 不能越过该边界。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前值为 empty/default 时抛出。</exception>
    public ulong Value => StrongIdCodec.GetValueOrThrow(_value, nameof(CommandId));

    /// <summary>
    /// 从 canonical 十进制文本解析 Command 标识。
    /// </summary>
    /// <param name="text">无符号、无空白、无前导零且非零的十进制文本。</param>
    /// <returns>解析后的 Command 标识。</returns>
    /// <exception cref="ArgumentNullException">输入为 null 时抛出。</exception>
    /// <exception cref="FormatException">输入不是合法 canonical 文本时抛出。</exception>
    public static CommandId Parse(string text)
    {
        return new CommandId(StrongIdCodec.Parse(text, nameof(CommandId)));
    }

    /// <summary>
    /// 尝试解析 canonical 十进制 Command 标识。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">成功时返回解析值；失败时为 default/empty。</param>
    /// <returns>输入合法且非零时返回 true。</returns>
    public static bool TryParse(string? text, out CommandId value)
    {
        if (StrongIdCodec.TryParse(text, out var parsed))
        {
            value = new CommandId(parsed);
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
        return StrongIdCodec.Format(_value, nameof(CommandId));
    }
}
