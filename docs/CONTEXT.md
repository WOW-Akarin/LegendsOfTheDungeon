# 当前上下文

更新时间：2026-11-16 +08:00
分支：main（用户要求直接更新、不建立分支）
当前里程碑：M1 基础 → M2 最小世界
当前阶段：P24 — Entity Identity / Transform / Life（IN_PROGRESS，目标 B）

## 不变的范围与架构

- 通用 C# / .NET 10 游戏服务器核心；不兼容 WoW 3.3.5a 客户端；研究 AzerothCore 固定 SHA `17f9f9d5003708136c798ab4ef8c1b9762251bad` 的通用机制。
- 模块化单体、World Actor、Ports & Adapters；领域状态有唯一所有者，只有有效 WorldExecutor 同步 Scope 可以写 World。
- 双车道有界 Mailbox，异步结果带 World/Epoch/Entity/Generation/RequestSeq 回执；默认安全拒绝，禁止 DB/网络 Worker 持有可变实体引用。
- 统一 .NET SDK 10.0.404；Linux x64；PostgreSQL 18；后续目标 10 World / 10,000 实体、50ms 逻辑 Tick，尚未有负载通过证据。

## 稳定的已验收阶段

P00–P16 及 P18 已 ACCEPTED。P18 具有真实 WorldExecutor Guard、异步回执、故障与限额证据；最终归档 HEAD `1f1a01b` CI #162 六 Job 全绿。P17 WorldInstance 尚受 P24 阻塞，P19 WorldTick 受 P17 阻塞。

## P24 当前进展和停点

本次建立 `Entities.Core` 领域薄能力：四元组不可变身份、Transform 唯一位置真相、只读 LifeState、窄 TargetState、同步 World Demand Guard；固定组合容器不提供可变能力集合或服务定位。已编写 `EntityCapabilitiesTests`，并调整 Solution、P03 白名单、NuGet locked 图；**当前为 IN_PROGRESS，尚无本次代码的 CI PASS**。

下一安全动作：等待真实 P24 实现 HEAD CI 出结果并修复失败，再独立 REVIEW；不得绕过现有 Gate，也不得提前声明 P24 ACCEPTED。

仍未完成：P17 世界注册/实体生命周期、真实 Target 存在性、Transform/Spatial 原子协调、P19 Tick、P32 Health、生产授权 Host、P62 回放、P64/P66 性能长稳。

## 恢复入口

- [当前状态](PROGRESS.md) / [P24 阶段记录](progress/P24.md) / [Entities 模块契约](modules/ENTITIES.md)
- [路线图](DEVELOPMENT_ROADMAP.md) / [项目范围](PROJECT_SPEC.md) / [架构](ARCHITECTURE.md)
- [P18 证据](progress/P18.md) / [WorldExecutor 契约](modules/WORLD_EXECUTION.md)
- [恢复流程](RECOVERY_WORKFLOW.md) / [构建门禁](operations/BUILD.md)
