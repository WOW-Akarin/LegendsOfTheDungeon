# 当前上下文

更新时间：2026-11-16 +08:00；分支 main
项目：通用 C#/.NET 10 游戏服务器核心（非 WoW 3.3.5a 客户端兼容服务）；上游 AzerothCore 固定 SHA `17f9f9d5003708136c798ab4ef8c1b9762251bad` 仅用作通用机制参考。

## 技术方向与阶段真相

SDK 10.0.404、Linux x64、PostgreSQL 18；模块化单体 / World Actor / Ports & Adapters，单世界只允许 P18 同步 Scope 改变状态；Worker 只排队不可用异步持有实体写引用。P07/P08/P14/P17/P18/P24 等已正式验收。

P17 归档 HEAD `fa2f4a3c3ea704a9d76e8b5445fa01c96535c8e6`，CI #175 7/7 SUCCESS，含 230 Unit/16 Architecture。**现进入 P19（IN_PROGRESS，目标 C），代码正在提交，尚无本阶段实现 CI PASS，不提前验收。**

## 当前 P19 交付边界

- Domain：`Runtime.World/WorldTickPipeline` 固定四相位、Phase/Key 稳定排序；FakeClock CurrentTick 控制到期；P17 `SnapshotForTick` 的稳定 SHA256 哈希，以 World/Epoch/Tick 和按 ID 排序的 EntityIdentity/Generation/Transform/Life/Target 为输入。
- Adapter：`Platform.WorldScheduling` 复用 P18 WorldExecutor，最多 64 世界、8 Worker、每世界每轮至多 8 Tick、RunBatch ≤1024；邮箱背压和队列待执行区分，慢 Tick 单调测量不入 Hash。
- Tests：新增 P19 单元/真实 Worker Probe，CI 从七 Job 增为八 Job。实现 CI 尚待验证，不能记录通过。
- P19 不能当作 P20 Spatial/P27 移动、P62.F 完整回放、P64.F 性能、P60 生产 Host 已完成；10 World/10,000 Entity/50ms 是未达到验证的目标。

## 下一安全动作

核验 P19 初始代码提交的全部八 Job，修复失败并保留真实历史；再独立 REVIEW 和正式 ACCEPTED。入口：[PROGRESS](PROGRESS.md) / [P19 记录](progress/P19.md) / [WorldTick 契约](modules/WORLD_TICK.md) / [ROADMAP](DEVELOPMENT_ROADMAP.md) / [ARCHITECTURE](ARCHITECTURE.md) / [恢复规则](RECOVERY_WORKFLOW.md)。
