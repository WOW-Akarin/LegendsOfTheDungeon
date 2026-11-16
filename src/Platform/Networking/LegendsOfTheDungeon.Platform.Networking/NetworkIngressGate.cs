using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Security.Contracts;

namespace LegendsOfTheDungeon.Platform.Networking;

/// <summary>不负责分发业务写的生产入口安全门禁：缺生产身份或授权即拒绝。</summary>
public sealed class NetworkIngressGate
{
    private readonly IIdentityBindingPort _binder;
    private readonly ICommandAuthorizationPort _authorizer;

    /// <summary>必须显式提供身份绑定器与命令授权器；默认接口实现全部拒绝。</summary>
    /// <param name="binder">P60.F 可信身份绑定 Port。</param>
    /// <param name="authorizer">P60.F 命令授权 Port。</param>
    public NetworkIngressGate(
        IIdentityBindingPort binder, ICommandAuthorizationPort authorizer)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    /// <summary>认证并授权一个固定操作码；只返回判断结果，不直接调用 World 写入 API。</summary>
    /// <param name="evidence">不得记录或序列化的原始身份凭证包装。</param>
    /// <param name="operationCode">由可信入口指定的稳定操作名称，不使用未经验证的网络数据。</param>
    /// <param name="cancellationToken">验证取消令牌。</param>
    /// <returns>授权通过或稳定拒绝错误；本阶段没有生产身份签发实现。</returns>
    public async ValueTask<Result> VerifyAsync(
        AuthenticationEvidence evidence, string operationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = await _binder.BindAsync(evidence, cancellationToken);
        if (binding.IsFailure)
        {
            return Result.Failure(binding.Error);
        }

        var production = ProductionIdentityGuard.RequireProduction(binding.Value);
        if (production.IsFailure)
        {
            return production;
        }

        return _authorizer.Authorize(binding.Value, operationCode);
    }
}
