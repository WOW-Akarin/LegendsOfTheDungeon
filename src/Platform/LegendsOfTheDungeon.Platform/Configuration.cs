using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LegendsOfTheDungeon.Platform;

/// <summary>配置门禁生命周期：尚未验证、正在运行或不可恢复的验证失败。</summary>
public enum ConfigurationState
{
    /// <summary>尚未完成启动验证。</summary>
    Created,
    /// <summary>所有模块已验证、配置指纹已冻结。</summary>
    Running,
    /// <summary>启动配置无效，不允许进入运行。</summary>
    Faulted
}

/// <summary>固定的配置错误类别，不包含任何原始配置值。</summary>
public enum ConfigurationError
{
    /// <summary>容量等整数参数无效。</summary>
    InvalidCapacity,
    /// <summary>结构化日志级别无效。</summary>
    InvalidLogSeverity,
    /// <summary>缺少必需的敏感设置。</summary>
    MissingSecret
}

/// <summary>可供日志与报告安全使用的中文配置诊断，不携带原始配置值。</summary>
public sealed class ConfigurationDiagnostic
{
    internal ConfigurationDiagnostic(string moduleId, ConfigurationError code)
    {
        ModuleId = moduleId;
        Code = code;
        ChineseMessage = code switch
        {
            ConfigurationError.InvalidCapacity => "配置容量必须为正整数。",
            ConfigurationError.InvalidLogSeverity => "日志级别配置无效。",
            ConfigurationError.MissingSecret => "缺少必需的敏感配置。",
            _ => "配置无效。"
        };
    }

    /// <summary>获取经验证的模块标识。</summary>
    public string ModuleId { get; }

    /// <summary>获取不包含敏感信息的错误类别。</summary>
    public ConfigurationError Code { get; }

    /// <summary>获取固定中文诊断，不回显用户配置值。</summary>
    public string ChineseMessage { get; }
}

/// <summary>定义每个技术模块独立的启动配置验证与非敏感指纹投影。</summary>
public interface IPlatformModuleOptions
{
    /// <summary>获取稳定、受检的模块标识。</summary>
    string ModuleId { get; }

    /// <summary>检查模块配置并返回固定错误类别，不返回原始值。</summary>
    /// <returns>错误类别集合；无错误时返回空集合。</returns>
    IReadOnlyList<ConfigurationError> Validate();

    /// <summary>仅向白名单写入器输出无秘密的整数/布尔配置。</summary>
    /// <param name="writer">仅支持非敏感数值的写入器。</param>
    void DescribePublicSettings(PublicOptionsWriter writer);
}

/// <summary>只接收非敏感整数和布尔值的配置指纹写入器。</summary>
public sealed class PublicOptionsWriter
{
    private readonly SortedDictionary<string, string> _values =
        new(StringComparer.Ordinal);

    internal PublicOptionsWriter()
    {
    }

    /// <summary>加入一个公开的非负整数值。</summary>
    /// <param name="key">安全且唯一的配置键。</param>
    /// <param name="value">非负整数值。</param>
    public void AddInteger(string key, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Add(key, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>加入一个公开的布尔值；敏感信息只能表达是否配置。</summary>
    /// <param name="key">安全且唯一的配置键。</param>
    /// <param name="value">非敏感布尔值。</param>
    public void AddBoolean(string key, bool value)
    {
        Add(key, value ? "true" : "false");
    }

    internal string CanonicalText =>
        string.Concat(_values.Select(static item =>
            $"{item.Key.Length}:{item.Key}={item.Value.Length}:{item.Value};"));

    private void Add(string key, string value)
    {
        if (!SafeIdentifiers.IsValid(key))
        {
            throw new ArgumentException("公开配置键格式无效。", nameof(key));
        }

        if (!_values.TryAdd(key, value))
        {
            throw new InvalidOperationException("同一模块不能重复写入公开配置键。");
        }
    }
}

internal static class SafeIdentifiers
{
    internal static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.' or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>封装敏感配置值；默认文本表示永不透露其内容。</summary>
public sealed class SensitiveValue
{
    private readonly string? _secret;

    /// <summary>保存敏感配置，供模块验证是否已配置；不用于日志。</summary>
    /// <param name="secret">Token、连接串等敏感原始内容。</param>
    public SensitiveValue(string? secret)
    {
        _secret = secret;
    }

    /// <summary>获取敏感配置是否已提供；绝不返回原值。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_secret);

    /// <summary>返回固定脱敏标记，不显示原始内容。</summary>
    /// <returns>固定脱敏标记。</returns>
    public override string ToString() => "[敏感信息已隐藏]";
}

/// <summary>通用日志技术模块的独立 Options。</summary>
public sealed class LoggingOptions : IPlatformModuleOptions
{
    /// <summary>创建日志模块设置。</summary>
    /// <param name="maximumBufferedRecords">允许的最大日志缓冲记录数。</param>
    /// <param name="minimumSeverity">最低日志级别。</param>
    public LoggingOptions(int maximumBufferedRecords, LogSeverity minimumSeverity)
    {
        MaximumBufferedRecords = maximumBufferedRecords;
        MinimumSeverity = minimumSeverity;
    }

    /// <inheritdoc />
    public string ModuleId => "platform.logging";

    /// <summary>获取最大日志缓冲记录数。</summary>
    public int MaximumBufferedRecords { get; }

    /// <summary>获取最低日志级别。</summary>
    public LogSeverity MinimumSeverity { get; }

    /// <inheritdoc />
    public IReadOnlyList<ConfigurationError> Validate()
    {
        var errors = new List<ConfigurationError>();
        if (MaximumBufferedRecords <= 0)
        {
            errors.Add(ConfigurationError.InvalidCapacity);
        }

        if (!Enum.IsDefined(MinimumSeverity))
        {
            errors.Add(ConfigurationError.InvalidLogSeverity);
        }

        return errors;
    }

    /// <inheritdoc />
    public void DescribePublicSettings(PublicOptionsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.AddInteger("maximum_buffered_records", MaximumBufferedRecords);
        writer.AddInteger("minimum_severity", (int)MinimumSeverity);
    }
}

/// <summary>通用后台工作容量的独立技术 Options，不包含游戏规则。</summary>
public sealed class WorkerOptions : IPlatformModuleOptions
{
    /// <summary>创建指定技术模块的后台工作设置。</summary>
    /// <param name="moduleId">稳定模块 ID。</param>
    /// <param name="maximumPending">最多待处理条目数。</param>
    public WorkerOptions(string moduleId, int maximumPending)
    {
        ModuleId = moduleId;
        MaximumPending = maximumPending;
    }

    /// <inheritdoc />
    public string ModuleId { get; }

    /// <summary>获取最大待处理容量。</summary>
    public int MaximumPending { get; }

    /// <inheritdoc />
    public IReadOnlyList<ConfigurationError> Validate() =>
        MaximumPending > 0 ? [] : [ConfigurationError.InvalidCapacity];

    /// <inheritdoc />
    public void DescribePublicSettings(PublicOptionsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.AddInteger("maximum_pending", MaximumPending);
    }
}

/// <summary>敏感环境变量的存在性校验，不暴露 Token 和连接串原文。</summary>
public sealed class SecretsOptions : IPlatformModuleOptions
{
    private readonly SensitiveValue _token;
    private readonly SensitiveValue _connectionString;

    /// <summary>创建仅用于校验敏感设置是否齐备的技术 Options。</summary>
    /// <param name="token">访问 Token 封装值。</param>
    /// <param name="connectionString">连接串封装值。</param>
    public SecretsOptions(SensitiveValue token, SensitiveValue connectionString)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _connectionString = connectionString
            ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <inheritdoc />
    public string ModuleId => "platform.secrets";

    /// <inheritdoc />
    public IReadOnlyList<ConfigurationError> Validate() =>
        _token.IsConfigured && _connectionString.IsConfigured
            ? []
            : [ConfigurationError.MissingSecret];

    /// <inheritdoc />
    public void DescribePublicSettings(PublicOptionsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.AddBoolean("token_configured", _token.IsConfigured);
        writer.AddBoolean("connection_configured", _connectionString.IsConfigured);
    }
}

/// <summary>验证所有技术模块配置并在成功后冻结公开指纹的启动门禁。</summary>
public sealed class ConfigurationStartup
{
    private readonly SortedDictionary<string, IPlatformModuleOptions> _modules =
        new(StringComparer.Ordinal);
    private readonly List<ConfigurationDiagnostic> _diagnostics = [];
    private string? _fingerprint;

    /// <summary>获取门禁当前生命周期。</summary>
    public ConfigurationState State { get; private set; }

    /// <summary>获取安全的中文诊断快照，不包含配置原值。</summary>
    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics =>
        _diagnostics.AsReadOnly();

    /// <summary>仅在成功进入 Running 后获取非敏感配置 SHA-256 指纹。</summary>
    public string Fingerprint => _fingerprint
        ?? throw new InvalidOperationException("配置尚未成功进入 Running。");

    /// <summary>启动前注册独立模块 Options，不允许重复模块 ID。</summary>
    /// <param name="options">可信技术模块的 Options。</param>
    public void Register(IPlatformModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureCreated();

        if (!SafeIdentifiers.IsValid(options.ModuleId))
        {
            throw new ArgumentException("模块标识格式无效。", nameof(options));
        }

        if (!_modules.TryAdd(options.ModuleId, options))
        {
            throw new InvalidOperationException("技术模块配置不能重复注册。");
        }
    }

    /// <summary>验证全部模块；错误时中文失败并保持非 Running。</summary>
    /// <returns>不包含敏感原文的稳定 SHA-256 配置指纹。</returns>
    /// <exception cref="InvalidOperationException">配置缺失、无效或启动已被调用时抛出。</exception>
    public string Start()
    {
        EnsureCreated();

        try
        {
            if (_modules.Count == 0)
            {
                throw new InvalidOperationException("启动失败：未注册任何技术模块配置。");
            }

            foreach (var pair in _modules)
            {
                foreach (var code in pair.Value.Validate())
                {
                    _diagnostics.Add(new ConfigurationDiagnostic(pair.Key, code));
                }
            }

            if (_diagnostics.Count > 0)
            {
                var messages = _diagnostics.Select(static item =>
                    $"{item.ModuleId}：{item.ChineseMessage}");
                throw new InvalidOperationException(
                    "启动配置验证失败：" + string.Join("；", messages));
            }

            var builder = new StringBuilder();
            foreach (var pair in _modules)
            {
                var writer = new PublicOptionsWriter();
                pair.Value.DescribePublicSettings(writer);
                builder.Append(pair.Key.Length)
                    .Append(':').Append(pair.Key)
                    .Append('{').Append(writer.CanonicalText)
                    .Append('}');
            }

            _fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
                .ToLowerInvariant();
            State = ConfigurationState.Running;
            return _fingerprint;
        }
        catch
        {
            State = ConfigurationState.Faulted;
            throw;
        }
    }

    private void EnsureCreated()
    {
        if (State != ConfigurationState.Created)
        {
            throw new InvalidOperationException("启动门禁已验证或已失败，不可再次注册或启动。");
        }
    }
}
