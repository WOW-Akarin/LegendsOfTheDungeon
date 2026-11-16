# P14 — Metrics / Diagnostics 契约

## 范围和真实调用方

- **契约 ID / 版本：** `platform.diagnostics.v1`。独立 Platform.Diagnostics Assembly 无生产 ProjectReference，不依赖 Kernel、Gameplay、World、Host 或第三方 SDK。
- **真实技术调用场景：** `SafeOperationTelemetry.RunAsync` 包装 P12 的 `BackgroundWorkItem` 实际执行路径；P14 DiagnosticsProbe 在真实有限 WorkerPool 上运行成功、抛异常与慢任务，检验遥测失效不会变更被包装任务的异常/结果；另用模拟的 Tick/DB/AI/Script 技术采样验证预留指标槽位。
- **执行权：** 本模块不含 WorldExecutor、游戏命令、数据库事务和领域对象写入接口。没有真实 World Host，因此不能宣称 Runtime Tick / DB / AI / Script 已自动埋点。P19/P27/P56 等阶段按真实状态所有者主动接线。
- **可观测失败策略：** 内存聚合有限且采用受检字段；`BoundedObservationOutbox.TryOffer` 不调用任何外部 Sink，立即返回布尔背压，满队列丢弃*遥测*而不是业务事实；`FlushAsync` 必须从与业务独立的执行上下文调用，最多派发指定配额；Sink 抛异常只计入 `FailedExports`，不回滚业务任务。取消中已从队列领取的遥测可丢失；不提供跨进程可靠遥测队列。

## 类型、标签和内存上限

- `DiagnosticArea` 限定 `Tick / Queue / Database / ArtificialIntelligence / Script`；`DiagnosticOutcome` 限定 `Succeeded / Failed / Cancelled / Rejected`。
- `FixedMetricRecorder` 恰好 **5 × 4 = 20** 个槽位；记录 Count、TotalMilliseconds、MaximumMilliseconds 和 ThresholdExceeded。数值累计按 `long.MaxValue` 饱和，最大值统计明确；不提供自由标签、EntityId、ActorId、用户输入、连接串或凭证字段。单位均为毫秒。
- `DiagnosticTraceBuffer`：1–4096 条有限 FIFO，仅保留固定领域、阶段、关联 GUID 和本地序号；GUID 只在有界 Trace/CrashContext 出现，不得当作指标标签。
- `SlowOperationDetector`：1–1024 条有限最近事件，受可信来源的非负耗时与正阈值驱动；保留领域、关联 GUID 和耗时。阈值可用确定性毫秒输入测试，不强制依赖 `Thread.Sleep`。真实异步 Wrapper 使用单调 `Stopwatch` 计时，>= 阈值判定慢操作；不创建每任务 Timer。
- `DiagnosticBudget` 硬限制 Trace 4096、慢操作 1024、CrashContext 每类最多 128；`BoundedObservationOutbox` 硬限制 4096 个结构化样本。所有容量在配置时失败而不允许无界扩展。
- `FixedHealthRegistry` 固定五个组件 Runtime/Persistence/Networking/Background/Diagnostics，`Unknown` 不等于健康；只有全部由可信探针上报 Healthy 才能获得 AllHealthy。当前库**不自行声称真实生产依赖健康**。
- `CrashContextCapture` 只组合健康、固定指标和有限 Trace/Slow 的只读快照，原因采用 `CrashCause` 枚举；没有 Exception/StackTrace/UserPayload/自由文本字段。上下文快照不改变业务状态，生产侧不可把敏感信息注入 GUID 或自由扩展。

## 异常、取消、顺序和成功点

- 包装器 `SafeOperationTelemetry.RunAsync` 保留原异步工作的完成、异常、取消语义；记录耗时在 `finally`，诊断路径里的异常被隔离。计量时间采用实际 `Stopwatch`；不影响业务 Tick 逻辑时间或随机流。
- Outbox 的 `TryOffer` 成功仅说明在诊断队列中缓冲，不保证 Sink 导出；`FlushAsync` 返回的是尝试发送数量而非成功确认数量，失败有单独计数。
- Trace 事件容量满时丢最早，指标仍保持聚合总数；CrashContext 不保证持久保存。所有四个维度均由调用者选择固定枚举，不从任意玩家/会话字段派生。
- `FixedMetricRecorder` 多线程使用 lock 保持快照内部数值一致；`DiagnosticTraceBuffer` / SlowOperation / Health 同步互斥，彼此快照不保证全局事务性。可靠域事件恢复仍依赖 P11/P65。

## 测试与保留工作

- Unit 验证固定槽位、多线程并发、饱和溢出、慢任务边界、有限 Trace/CrashContext、默认健康拒绝、异常/取消传播、外部 Sink 抛错与限额丢遥测。
- DiagnosticsProbe 用真实 P12 WorkerPool 与实际 `Task.Delay` 慢任务，检查 Worker 错误隔离及结果不受遥测影响；通过时输出 `P14_DIAGNOSTICS_INTEGRATION_PASS`。
- P64.F 的环境/负载清单见 [P64 基准准备清单](../operations/P64_BASELINE.md)，仅作可重现配置准备，**尚无性能测量结果**。
- 后续 P19/P27/P56/P64 应在真实 Owner 中接入并测量；P14 仅完成通用技术层，无生产指标后端/OTLP 或持久化追踪。阶段进度见 [P14](../progress/P14.md)。
