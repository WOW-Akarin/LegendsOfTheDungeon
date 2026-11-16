# P12 — Concurrency 独立审查

- 代码基线：d9e22e456055ae362509523b3c74709514bb40cd
- 实现 CI：run #83 SUCCESS
- 状态：ACCEPTED，独立 REVIEW HEAD run #84 与正式 ACCEPTED HEAD run #85 均 SUCCESS
- 日期：2026-11-16 +08:00

## 四视角

**功能正确性。** BoundedMailbox 对 Normal/Critical 使用独立上限，Critical burst 对普通输入提供抗饥饿保障，跨独立 Drain 仍维持配额。Scheduler 用纯逻辑 Tick + monotonic ID 排序，不使用墙钟或每实体系统 Timer。WorkChannel 以 bounded Channel 为真正容量上限，并用受检 ID 跟踪工作生命周期。WorkerPool 固定并行度，异常不退出 Worker，取消不会被伪装为成功。

**架构与所有权。** Platform.Concurrency 零生产 ProjectReference，未引用业务模块或 WorldExecutor。技术层不知道 World/Entity 状态，不授予写权限。调用方必须将安全快照入队，并在未来的 P18 重新检查 WorldId/Epoch/Generation/授权。Critical 路由权限同样必须由接入方验证。

**失败和恢复。** 超时 Stop 的报告包含未完成 ID 与 Queued/Running 状态；Dispose 后未领取回调释放但 ID 为 Abandoned。仍在执行的不可取消任务不能被 .NET 强制杀死，也不能被报告为已完成。调用方应先读取 ShutdownReport，再处理恢复，严禁对可能已提交的 DB 事务直接生成新幂等键。

**证据可复现。** CI run #83 Ubuntu/.NET 10.0.404 locked-mode 全绿，123 Unit、16 Architecture、全部原有 Gate、PostgreSQL Integration。测试包含多线程并发生产者、有限 worker、异常/取消、排空超时及跨组件链路。文档记录 #77/#78 analyzer 和 #79 调度竞态首次失败，不关闭 Analyzer、不删除负例。

## 明确保留风险

- 没有实现 P18 WorldExecutor 的执行租约和唯一世界写上下文，不能使用本后台池直接修改游戏状态。
- 没有真实网络/会话/授权 Adapter；Critical 车道只由可信接入方使用。
- 真实业务重试、落库确认、世界关停次序需 P18、P47、P65 后续提供领域协作与持久账本。
- 本阶段测试证明有界正确性，不是 P64 吞吐/延迟的真实负载结果。
- 非配合取消的运行中代码无法由技术层强制终止，需由上层进程隔离策略和未完成工作恢复处理。

## 审查结论

P12 技术范围与路线图出口一致，四视角代码审查未发现必需未解决项，独立 REVIEW HEAD 48d5604 的 run #84 已 SUCCESS，P12 技术模块 B+C 正式 ACCEPTED；正式 ACCEPTED HEAD 14f509c 的 run #85 也完整 SUCCESS。
