using System.Text.Json;

namespace LegendsOfTheDungeon.Platform.Diagnostics;

/// <summary>只输出受检枚举、GUID 与非负耗时的结构化诊断日志格式化器。</summary>
public static class StructuredDiagnosticFormatter
{
    /// <summary>将一条已记录的慢操作事实转换为字段白名单 JSON，绝不接收异常或用户参数。</summary>
    /// <param name="record">受信任检测器生成的有限慢操作记录。</param>
    /// <returns>带稳定 slow_operation 事件代码的结构化 JSON。</returns>
    public static string SlowOperationToJson(SlowOperationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(new
        {
            eventId = "slow_operation",
            area = record.Area.ToString(),
            correlation = record.Correlation.ToString("D"),
            elapsedMilliseconds = record.ElapsedMilliseconds
        });
    }

    /// <summary>将有界崩溃上下文转为固定字段结构化 JSON，用于安全的运维诊断。</summary>
    /// <param name="snapshot">仅包含受检值和有限快照的 CrashContext。</param>
    /// <returns>不含自由文本、玩家数据和原始异常的结构化 JSON。</returns>
    public static string CrashContextToJson(CrashContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(new
        {
            cause = snapshot.Cause.ToString(),
            correlation = snapshot.Correlation.ToString("D"),
            traces = snapshot.Traces.Select(static item => new
            {
                sequence = item.Sequence,
                area = item.Area.ToString(),
                stage = item.Stage.ToString(),
                correlation = item.Correlation.ToString("D")
            }),
            slowOperations = snapshot.SlowOperations.Select(static item => new
            {
                area = item.Area.ToString(),
                correlation = item.Correlation.ToString("D"),
                elapsedMilliseconds = item.ElapsedMilliseconds
            }),
            metrics = snapshot.Metrics.Select(static item => new
            {
                area = item.Area.ToString(),
                outcome = item.Outcome.ToString(),
                count = item.Count,
                totalMilliseconds = item.TotalMilliseconds,
                maximumMilliseconds = item.MaximumMilliseconds,
                thresholdExceeded = item.ThresholdExceeded
            }),
            health = snapshot.Health.Select(static item => new
            {
                component = item.Component.ToString(),
                condition = item.Condition.ToString()
            })
        });
    }
}
