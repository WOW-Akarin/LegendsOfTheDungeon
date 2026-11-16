# 当前上下文

更新时间：2026-11-17；GitHub `WOW-Akarin/LegendsOfTheDungeon`；main 直推、不创建分支。
项目：通用 .NET 10 游戏服务器；非 WoW 3.3.5a 客户端兼容。架构基线：模块化单体、World Actor 独占同步 Scope、领域唯一真相、P03 严格依赖边界。

## 前置阶段

P00–P20、P24(B)、P26(C) 已正式 ACCEPTED。最新 P26 归档 HEAD `58e84431d5acf70a743f5a3ffc6a38be90bf7e65`，CI #195 10/10 SUCCESS（275 Unit/16 Architecture）。P17 WorldInstance、P18 WorldExecutor、P19 确定性 Tick、P20 Spatial Grid、P26 控制/标签均具备真实验证。

## 当前 P21

**P21 — Visibility / Relevance（ACCEPTED，目标 C）**，依赖 P20 + P26 已满足，首次代码 HEAD 尚无 CI 通过证据。

- `Runtime.World.WorldVisibilitySet` 为每个完整 Observer EntityIdentity 私有的派生有界可见集合；不占有可变实体或第二套 Transform。真实 P18 World Scope 发布版本化只读 `WorldVisibilityView` 与 `WorldVisibilityDelta`。
- 查询从 P20 Nearby 空间候选按距离闭区间筛选，P26 Required/Excluded Tags、Life 与 Control 状态后按 EntityId 排序；观察者本人永不属于自身列表。
- 每次成功 Refresh 获得连续 per-observer SnapshotVersion 和 World/Epoch/Observer；重复刷新不重复 Enter/Leave。目标销毁或旧代际被替换、观察者退出/死亡、Tag/Control/位置跨界在下次 Refresh 输出正确 Delta；历史视图不被原地修改。
- 最多 1024 Visible、半径最大 4096m，容量溢出不会发布部分版本；Faulted 世界拒绝刷新，P18 过期租约不能写。
- 新增 Unit、真实独立 WorldVisibilityProbe、第 11 个阻断 Job。实现 #197、独立 REVIEW #198 均 11 Job SUCCESS，正式 ACCEPTED HEAD #199 也 11/11 SUCCESS，P21 C 完整验收。

## 当前审查与下一安全动作

- 初次 CI #196 CS0165/CA1859 编译 FAIL 已修复，稳定实现 #197 11 Job 全绿，相关记录保留于 [P21](progress/P21.md)。
- 独立四视角审查：[P21 Review](reviews/P21_VISIBILITY_REVIEW.md)。独立 REVIEW HEAD 已 #198 十一 Job SUCCESS，正式 ACCEPTED，状态提交 HEAD #199 十一 Job SUCCESS。

## 不属于 P21 验收

目前只实现 Pull + Refresh，不是即时网络推送。后续 Packet/客户端编码、P27 Movement、P60 Host 授权、P62 Replay 和 P64 高负载尚未通过。P21 独立审查 #198 已 11 Job 全绿，P21 正式 ACCEPTED；P21 完成后按路线图推进后续已满足依赖的阶段。

入口：[PROGRESS](PROGRESS.md)、[P21](progress/P21.md)、[WORLD_VISIBILITY](modules/WORLD_VISIBILITY.md)、[DEVELOPMENT_ROADMAP](DEVELOPMENT_ROADMAP.md)、[ARCHITECTURE](ARCHITECTURE.md)。

## P21 正式收口与下一安全动作

- #197 稳定实现 11/11 SUCCESS；#198 独立 REVIEW 11/11 SUCCESS；#199 正式 ACCEPTED HEAD 11/11 SUCCESS（290 Unit/16 Architecture）。
- 下一阶段 **P22 Spawn / Respawn**，直接依赖 P16.F/P19/P20/P24 已全部 ACCEPTED，需实现 SpawnRegistry、RespawnTimer、LinkedRespawn、防重复 Grid 加载与崩溃恢复对照。
- P23 实际路线图是 Terrain / Collision / Navigation Ports，**不是网络 Packet 阶段**；P21 历史记录中的误写已纠正。

## 2026-11-17：P22 当前实施边界

- P21 归档 HEAD `c4fdab6fa3dee94f4eb3d0c26d899982ad3e1bc8` 的 CI #200 11/11 SUCCESS；此前 P00–P21、P24(B)、P26(C) 均已验收。
- 当前转入 **P22 Spawn / Respawn（IN_PROGRESS，目标 C）**；直接依赖 P16.F、P19、P20、P24 均满足。
- 新 P22 单世界 `WorldSpawnRegistry`：唯一 SpawnKey 与 EntityId、单调 Generation、静态/动态 Spawn、P19 Timers Phase/LogicalTimer + FakeClock、关联环检测、P20 Grid Active/Unloaded 与防非受管占用。
- 崩溃恢复只接受外部已确认 Checkpoint：Active 实体若未由 P17 恢复为完全相同 Generation，则 `RecoveryHold` 阻止自动复制；定时器使用绝对 DueTick。此阶段没有真正 PostgreSQL 持久事务、跨进程完整 Replay 或 Spawn 业务 Catalog Adapter 绑定，不可冒充全部实现。
- 已新增 Unit 与独立真实 P22 WorldSpawnProbe，第 12 个 CI Job。**初始实现 HEAD 的 CI 未运行**；保持 main 直推，不创建分支。必须等完整实现、独立 REVIEW 和 ACCEPTED 各自 12/12 SUCCESS 才正式验收。
- P21 旧章节中 Pending REVIEW 的说明以此 P22 更新和 docs/progress/P21.md 的最新正式归档为准。

入口：[P22 进度](progress/P22.md)、[WORLD_SPAWN](modules/WORLD_SPAWN.md)、[PROGRESS](PROGRESS.md)、[ROADMAP](DEVELOPMENT_ROADMAP.md)。

## P22 初次真实 CI

- 初次代码 HEAD `2b02cde` CI #201 12/12 SUCCESS，307 Unit、16 Architecture、P22 真实五场景 Probe。
- 已加强 Timers Tick 连续性/过期拒绝、4096 定义上限、旧 Checkpoint 冻结及恢复 Generation 隔离；新的代码 HEAD CI 尚待重跑。
