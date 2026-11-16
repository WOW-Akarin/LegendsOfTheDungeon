using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Security.Contracts;

/// <summary>已绑定身份的来源类别，不将本地测试身份当作生产凭证。</summary>
public enum IdentityOrigin
{
    /// <summary>真实认证适配器所建立的生产身份；P10 不提供生产签发实现。</summary>
    Production = 1,
    /// <summary>只用于离线单元测试的模拟身份。</summary>
    LocalTest = 2
}

/// <summary>经过信任边界后可用于授权判断的身份；P10 不开放生产身份构造。</summary>
public sealed class BoundIdentity
{
    private BoundIdentity(string subjectKey, IdentityOrigin origin)
    {
        SubjectKey = subjectKey;
        Origin = origin;
    }

    /// <summary>获取稳定主体键；不包含 Token 等认证秘密。</summary>
    public string SubjectKey { get; }

    /// <summary>获取身份来源。</summary>
    public IdentityOrigin Origin { get; }

    /// <summary>为本地测试建立独立的模拟身份，不作为生产认证凭证。</summary>
    /// <param name="subjectKey">只用于本地模拟的主体键。</param>
    /// <returns>LocalTest 来源身份。</returns>
    public static BoundIdentity CreateLocalTest(string subjectKey)
    {
        if (string.IsNullOrWhiteSpace(subjectKey)
            || subjectKey.Length > 64
            || subjectKey.Any(static character =>
                character is not (>= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '.' or '-' or '_')))
        {
            throw new ArgumentException("本地测试主体键格式无效。", nameof(subjectKey));
        }

        return new BoundIdentity(subjectKey, IdentityOrigin.LocalTest);
    }
}

/// <summary>封装待认证证据；默认字符串表示永不暴露原始凭证。</summary>
public sealed class AuthenticationEvidence
{
    private readonly string? _credential;

    /// <summary>创建不应记录到日志中的认证证据。</summary>
    /// <param name="credential">不可信外部输入的认证凭证。</param>
    public AuthenticationEvidence(string? credential)
    {
        _credential = credential;
    }

    /// <summary>获取凭证是否非空，不泄漏内容。</summary>
    public bool IsProvided => !string.IsNullOrWhiteSpace(_credential);

    /// <summary>输出固定脱敏标记。</summary>
    /// <returns>固定脱敏标记。</returns>
    public override string ToString() => "[认证证据已隐藏]";
}

/// <summary>P60.F 最小认证/主体绑定 Port；生产适配器须在入口验证输入后签发身份。</summary>
public interface IIdentityBindingPort
{
    /// <summary>尝试绑定身份；无认证适配器时必须返回拒绝。</summary>
    /// <param name="evidence">敏感认证证据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已绑定身份或稳定拒绝错误。</returns>
    ValueTask<Result<BoundIdentity>> BindAsync(
        AuthenticationEvidence evidence,
        CancellationToken cancellationToken);
}

/// <summary>P60.F 命令授权 Port；后续实现必须再次核验会话和主体绑定。</summary>
public interface ICommandAuthorizationPort
{
    /// <summary>判断指定主体是否获准执行操作；默认实现全部拒绝。</summary>
    /// <param name="identity">待授权身份，可为空。</param>
    /// <param name="operationCode">稳定操作代码，不能取作任意用户输入。</param>
    /// <returns>授权成功或稳定拒绝错误。</returns>
    Result Authorize(BoundIdentity? identity, string operationCode);
}

/// <summary>未配置真实生产认证适配器时，拒绝全部身份绑定请求。</summary>
public sealed class DenyByDefaultIdentityBinder : IIdentityBindingPort
{
    /// <inheritdoc />
    public ValueTask<Result<BoundIdentity>> BindAsync(
        AuthenticationEvidence evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            Result.Failure<BoundIdentity>(new KernelError(
                "security.identity.denied",
                "尚未配置可信生产身份绑定器，默认拒绝。")));
    }
}

/// <summary>缺少明确安全策略时拒绝全部命令的默认授权实现。</summary>
public sealed class DenyByDefaultCommandAuthorizer : ICommandAuthorizationPort
{
    /// <inheritdoc />
    public Result Authorize(BoundIdentity? identity, string operationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);

        return Result.Failure(new KernelError(
            "security.authorization.denied",
            "尚未配置命令授权规则，默认拒绝。"));
    }
}

/// <summary>生产入口防止本地测试身份被当作生产认证结果使用。</summary>
public static class ProductionIdentityGuard
{
    /// <summary>检验身份来源是否为生产；P10 无生产签发者，默认不能获得通过。</summary>
    /// <param name="identity">输入身份，可为空。</param>
    /// <returns>生产身份检验结果，测试/空身份返回拒绝。</returns>
    public static Result RequireProduction(BoundIdentity? identity)
    {
        if (identity?.Origin == IdentityOrigin.Production)
        {
            return Result.Success();
        }

        return Result.Failure(new KernelError(
            "security.identity.production_required",
            "生产入口拒绝本地测试或缺失身份。"));
    }
}
