using System.Collections.ObjectModel;

namespace LegendsOfTheDungeon.Kernel;

internal static class StableErrorCode
{
    internal const int MaxLength = 96;

    internal static string Validate(string code, string paramName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("错误码不能为空或空白。", paramName);
        }

        if (code.Length > MaxLength)
        {
            throw new ArgumentException("错误码长度不能超过 96 个字符。", paramName);
        }

        foreach (var character in code)
        {
            if ((character is >= 'a' and <= 'z')
                || (character is >= '0' and <= '9')
                || character is '.' or '_' or '-')
            {
                continue;
            }

            throw new ArgumentException("错误码只允许 lowercase ASCII a-z、0-9、点、下划线和连字符。", paramName);
        }

        return code;
    }

    internal static string RequireText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("文本不能为空或空白。", paramName);
        }

        return value;
    }
}

/// <summary>
/// 表示字段、路径或输入项级别的验证问题。
/// </summary>
public sealed class ValidationIssue
{
    /// <summary>
    /// 创建验证问题。
    /// </summary>
    /// <param name="code">稳定机器错误码，必须使用 lowercase ASCII 规则。</param>
    /// <param name="path">发生问题的字段或逻辑路径。</param>
    /// <param name="message">面向人类的中文或本地化前诊断说明。</param>
    public ValidationIssue(string code, string path, string message)
    {
        Code = StableErrorCode.Validate(code, nameof(code));
        Path = StableErrorCode.RequireText(path, nameof(path));
        Message = StableErrorCode.RequireText(message, nameof(message));
    }

    /// <summary>
    /// 获取稳定机器错误码；程序分支应依赖该值而不是 Message。
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 获取字段或逻辑路径。
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// 获取面向人类的诊断说明；措辞允许演进，不作为稳定机器键。
    /// </summary>
    public string Message { get; }
}

/// <summary>
/// 表示 Kernel 失败结果的稳定错误信息。
/// </summary>
public sealed class KernelError
{
    private readonly ReadOnlyCollection<ValidationIssue> _validationIssues;

    /// <summary>
    /// 创建错误。
    /// </summary>
    /// <param name="code">稳定机器错误码，必须使用 lowercase ASCII 规则。</param>
    /// <param name="message">面向人类的中文或本地化前错误说明。</param>
    /// <param name="validationIssues">可选字段级验证问题；构造时会防御性复制。</param>
    public KernelError(
        string code,
        string message,
        IEnumerable<ValidationIssue>? validationIssues = null)
    {
        Code = StableErrorCode.Validate(code, nameof(code));
        Message = StableErrorCode.RequireText(message, nameof(message));

        var issues = validationIssues?.ToArray() ?? [];
        _validationIssues = Array.AsReadOnly(issues);
    }

    /// <summary>
    /// 获取稳定机器错误码；传输和持久化边界必须原样保留。
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 获取面向人类的错误说明；程序逻辑不得依赖具体措辞。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 获取只读字段级验证问题集合。
    /// </summary>
    public IReadOnlyList<ValidationIssue> ValidationIssues => _validationIssues;
}
