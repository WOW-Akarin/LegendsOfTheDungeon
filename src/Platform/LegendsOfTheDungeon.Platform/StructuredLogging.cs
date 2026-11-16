using System.Text.Json;
using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Platform;

/// <summary>结构化日志严重度。</summary>
public enum LogSeverity
{
    /// <summary>诊断信息。</summary>
    Debug,
    /// <summary>普通运行信息。</summary>
    Information,
    /// <summary>异常但可继续处理。</summary>
    Warning,
    /// <summary>需要故障处理的错误。</summary>
    Error
}

/// <summary>日志类别：运行、审计和性能。</summary>
public enum LogCategory
{
    /// <summary>通用运行事件。</summary>
    Operation,
    /// <summary>安全审计事件。</summary>
    Audit,
    /// <summary>性能与耗时事件。</summary>
    Performance
}

/// <summary>固定事件代码，日志 API 不接受含秘密的任意事件文本。</summary>
public enum LogEventId
{
    /// <summary>配置验证成功。</summary>
    ConfigurationAccepted,
    /// <summary>配置验证失败。</summary>
    ConfigurationRejected,
    /// <summary>身份绑定失败。</summary>
    IdentityRejected,
    /// <summary>命令授权被拒绝。</summary>
    AuthorizationDenied,
    /// <summary>工作条目被拒绝。</summary>
    WorkRejected,
    /// <summary>操作结束。</summary>
    OperationCompleted,
    /// <summary>性能数据点。</summary>
    PerformanceSample
}

/// <summary>包含 Module/World/Entity/Correlation 的受检、不可变结构化日志上下文。</summary>
public sealed class LogContext
{
    /// <summary>创建日志上下文，拒绝无效模块和空 Correlation。</summary>
    /// <param name="module">受检模块 ID。</param>
    /// <param name="correlation">非空关联 Guid，不能携带自由文本秘密。</param>
    /// <param name="world">可选世界标识。</param>
    /// <param name="entity">可选实体标识。</param>
    public LogContext(
        string module,
        Guid correlation,
        WorldId? world = null,
        EntityId? entity = null)
    {
        if (!SafeIdentifiers.IsValid(module))
        {
            throw new ArgumentException("日志模块标识格式无效。", nameof(module));
        }

        if (correlation == Guid.Empty)
        {
            throw new ArgumentException("日志关联标识不能为空。", nameof(correlation));
        }

        if (world is { IsEmpty: true })
        {
            throw new ArgumentException("日志 WorldId 不能为 empty。", nameof(world));
        }

        if (entity is { IsEmpty: true })
        {
            throw new ArgumentException("日志 EntityId 不能为 empty。", nameof(entity));
        }

        Module = module;
        Correlation = correlation;
        World = world;
        Entity = entity;
    }

    /// <summary>获取模块标识。</summary>
    public string Module { get; }

    /// <summary>获取可选世界标识。</summary>
    public WorldId? World { get; }

    /// <summary>获取可选实体标识。</summary>
    public EntityId? Entity { get; }

    /// <summary>获取关联标识。</summary>
    public Guid Correlation { get; }
}

/// <summary>可输出到日志接收器的结构化数据；不含自由文本消息字段。</summary>
public sealed class StructuredLogEntry
{
    internal StructuredLogEntry(
        LogContext context,
        LogEventId eventId,
        LogSeverity severity,
        LogCategory category,
        bool success,
        long? elapsedMilliseconds)
    {
        Context = context;
        EventId = eventId;
        Severity = severity;
        Category = category;
        Success = success;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>获取完整日志上下文。</summary>
    public LogContext Context { get; }

    /// <summary>获取固定事件标识。</summary>
    public LogEventId EventId { get; }

    /// <summary>获取严重度。</summary>
    public LogSeverity Severity { get; }

    /// <summary>获取类别。</summary>
    public LogCategory Category { get; }

    /// <summary>获取操作是否成功的状态。</summary>
    public bool Success { get; }

    /// <summary>获取可选非负耗时毫秒数。</summary>
    public long? ElapsedMilliseconds { get; }
}

/// <summary>结构化日志写入端口，由组合根注入具体 Sink。</summary>
public interface ILogSink
{
    /// <summary>写入不含秘密原文的结构化事件。</summary>
    /// <param name="entry">受检的结构化数据。</param>
    void Write(StructuredLogEntry entry);
}

/// <summary>拒绝自由文本字段的通用结构化运行、审计和性能日志记录器。</summary>
public sealed class StructuredLogger
{
    private readonly ILogSink _sink;

    /// <summary>创建并注入日志接收器。</summary>
    /// <param name="sink">日志接收器。</param>
    public StructuredLogger(ILogSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>写入枚举事件和类型化上下文，不接受 Token、连接串或异常文本。</summary>
    /// <param name="context">结构化上下文。</param>
    /// <param name="eventId">固定事件 ID。</param>
    /// <param name="severity">严重度。</param>
    /// <param name="category">事件类别。</param>
    /// <param name="success">操作成功标记。</param>
    /// <param name="elapsedMilliseconds">可选非负耗时毫秒值。</param>
    public void Write(
        LogContext context,
        LogEventId eventId,
        LogSeverity severity,
        LogCategory category,
        bool success,
        long? elapsedMilliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Enum.IsDefined(eventId)
            || !Enum.IsDefined(severity)
            || !Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(eventId), "日志枚举配置无效。");
        }

        if (elapsedMilliseconds is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedMilliseconds),
                "日志耗时不能为负数。");
        }

        _sink.Write(new StructuredLogEntry(
            context,
            eventId,
            severity,
            category,
            success,
            elapsedMilliseconds));
    }
}

/// <summary>将固定字段序列化为结构化 JSON 的安全输出适配器。</summary>
public static class StructuredLogFormatter
{
    /// <summary>将受检日志条目转换成仅含白名单字段的 JSON。</summary>
    /// <param name="entry">结构化条目。</param>
    /// <returns>不含原始异常、Token、连接串的 JSON。</returns>
    public static string ToJson(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Serialize(new
        {
            module = entry.Context.Module,
            world = entry.Context.World?.Value,
            entity = entry.Context.Entity?.Value,
            correlation = entry.Context.Correlation.ToString("D"),
            eventId = entry.EventId.ToString(),
            severity = entry.Severity.ToString(),
            category = entry.Category.ToString(),
            success = entry.Success,
            elapsedMilliseconds = entry.ElapsedMilliseconds
        });
    }
}
