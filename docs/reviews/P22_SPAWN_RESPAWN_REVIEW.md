# P22 — Spawn / Respawn 独立四视角审查

- 状态：ACCEPTED（目标 C）
- 稳定实现 HEAD：`485e94891dfd3c3c8db85196b02a6d6e755b6a26`
- 实现 CI #205：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43984436900，**12/12 SUCCESS**。
- 独立 REVIEW HEAD：`fd099e818851c255cbb125457d455eaca4b6d39c`，CI #206（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43984888412）12/12 SUCCESS。
- 正式 ACCEPTED HEAD：`3eb5167cdb7492e5a7a79492c28c4c01e3b8bfe5`，CI #207（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43985257531）12/12 SUCCESS。
- 直接依赖 P16.F、P19、P20、P24 均已正式 ACCEPTED，P17/P18 世界生命周期及 P21 Visibility 也有独立 CI 证据；遵循 main 直推、不创建分支。

## 1. 领域功能与确定性语义

- `WorldSpawnKey` 是每世界独一非零稳定键，`WorldSpawnDefinition` 包括唯一 EntityId、P24 初始 Transform、Duration 和 Static/Dynamic。一个 SpawnKey 的不同定义拒绝、多个 Key 指向相同 EntityId 拒绝；静态点相同定义重注册和 Grid 重载幂等。总定义预算 4096，满额明确拒绝。
- 静态 Spawn 不隐式加载 Cell；只有 P20 Cell 明确 Active 且 P19 Timers 步骤到达后才可 Spawn。动态 Spawn 需已激活 Cell，在可信 P18 Scope 内一次性创建，退休后不借 Timer 重生。
- 实体 Generation 仅在 P17 `WorldInstance.Spawn` 真正返回 Spawned 后确认。退休不会复制或重用旧 Generation；FakeClock 固定 50ms/LogicalTimer 向上取整延迟，提前不生成、同 Tick 重算幂等；Tick 跳号、回拨和超时用明确错误拒绝且不改变 LastProcessedTick。
- LinkedRespawn 在静态定义之间显式建立关系，自环、跨三节点长环拒绝，Timers 阶段执行顺序由链深再 SpawnKey 固定排序；子实例只有在父 Registry 当前状态 Active 且 P17 精确身份真实 Active 时才会生成，未实现主动级联击杀，不能误称战斗群组系统。
- 已确认实例被外部 P17 Despawn 后下一个 Timers 核查旧实体确实退休，再开始新的逻辑延迟；重复/过期事件不得生成两个活动实体。

## 2. 架构、写入授权与所有权

- 新代码只加入已验收的 `Runtime.World` 生产项目，不产生新的 production 项目、不突破 P03 依赖白名单；P03 16/16 Architecture PASS。
- SpawnRegistry 只是显式注入 `WorldInstance` 与 `IClock` 的有限调度层。唯一 EntityIdentity/Generation 的真实事实始终由 P17 `WorldInstance`、P18 `IWorldWriteContext` 与跨世界 `WorldOwnershipRegistry` 校验；唯一位置来自 P24 Transform，Cell 真相来自 P20。没有第二个 Entity Manager，也未接触网络/数据库适配器。
- Registry 实现 `IWorldTickStep`：固定 `WorldTickPhase.Timers` 和 `world.spawn-respawn` Key，在 P19 WorldTickPipeline 的真实执行顺序中运行，不持有后台 Writer、不创建 Thread/Timer、不使用墙钟作为 Core timer 依据。
- 所有修改均在真实 P18 当期同步 World Scope 内进行；旧 Lease 不能修改 Registry；World Faulted/Stopped 拒绝 Registry 写入，Draining 不处理 Timers、不可卸载有受管实体的 Cell，也不能重新关联/恢复确认为 Active。P17 Lifecycle 真实 Stop 仍拥有最终清理权限。

## 3. Grid、安全恢复与资源风险

- `UnloadGrid` 必须在退休任何受管实体前核查 P20 Cell 当前成员全部属于本 Registry 的已确认 Active Spawn，发现任意非受管或 Attachment 即拒绝 Occupied，避免错误删除其它模块的实体。随后按 SpawnKey 顺序退休受管实例，再调用 P20 空 Cell 卸载；中途不可预期异常只可 Faulted，不声称跨对象事务回滚。
- Draining 状态下，UnloadGrid/SetLinkedRespawn/ConfirmRecovered 在**任何副作用前**拒绝；该缺口曾在实现审查时被识别，现有专门 Unit 回归验证没有半退休、World.Stop 仍可正常完成。
- Checkpoint 是 World/Epoch、最后 Tick、版本、原始定义、Cell、最大 Generation、活动完整身份、绝对 DueTick、LinkedTo 的值拷贝，恢复时拒绝不一致的 World/Epoch、时钟落后、重名 Key/EntityId、错误 Cell、破损父关系或环。
- **已确认 Active 但 P17 尚未精确恢复同一 World/Epoch/EntityId/Generation 的实例时，RecoveryHold 禁止任何自动重生**；凭空调用 ConfirmRecovered 不能放行。真实外部 P17 恢复 exact Identity 后可以重新确认；处于 Waiting 状态但发现其它活跃实例也进入 Hold，避免双份实例。
- 已经测试真实 WorldExecutor 下对全新 WorldInstance 的 Checkpoint 还原和恢复身份；**并没有实现真实 DB 落盘/全存储原子事务**，不能把内存 Checkpoint + 重建模拟写成生产崩溃恢复已经联通。外部 P11/P60 Adapter 和 P62 完整 Replay 必须另行验收。
- P19 WorldStateDigest 当前只覆盖 P17 canonical 实体状态，**尚未包含 P22 SpawnRegistry 的 Timer/LinkedRespawn 账本**；这些值已纳入 P22 Checkpoint，完整跨子系统 Replay/Hash 是 P62 后续工作。大量 Spawn/Cell 与 N×World Tick 的性能不属于当前 P64 性能结论。

## 4. 正反测试、CI 和失败可追溯性

| 审查证据 | 稳定实现 CI #205 |
|---|---|
| .NET 10.0.404 Release locked restore | PASS |
| P22 + 原仓库 Unit 总数 | **315/315 PASS** |
| P03 Architecture | **16/16 PASS** |
| P02/P03/P04/P05 质量和负向门禁 | PASS |
| 唯一 SpawnKey/EntityId、重复加载、4096 配额 | PASS |
| FakeClock 到期 50ms/重生新 Generation/零延迟不双生 | PASS |
| LinkedRespawn 长环拒绝、父先子后稳定顺序 | PASS |
| 动态 Spawn、Grid Unloaded/Occupied 保护 | PASS |
| 外部退休后 Timer 重新起算，不用旧 Generation | PASS |
| Crash Checkpoint 确认实例隔离、冲突身份拒绝、绝对 DueTick | PASS |
| Faulted/Stopped/Draining 不修改 Registry / 无部分 Grid 退休 | PASS |
| 真实 P19 Timers Step + WorldExecutor replay | PASS |
| 独立 `world-spawn` 五场景 Probe | PASS，P22_WORLD_SPAWN_INTEGRATION_PASS |
| 之前十一项集成与文档门禁 | PASS |
| 完整实现 HEAD CI | **12/12 SUCCESS** |
| 独立 REVIEW HEAD CI | **12/12 SUCCESS**，run #206，315 Unit/16 Architecture |
| 正式 ACCEPTED HEAD CI | **12/12 SUCCESS**，run #207，315 Unit/16 Architecture |

失败链：初次 #201 12/12 PASS；#202 真实 `SkippedAndHistoricalTimersTicks` 负向测试发现 Tick 同时跳号又超时的异常类型优先级错误，311/312 Unit PASS、一个 FAIL；#203 调整验证顺序后 12/12 PASS；#204 Faulted/Stopped 与 Draining 的生命周期守卫后 12/12 PASS；#205 防止 Draining 下卸载先退休造成部分提交后 12/12 PASS。**没有放松质量门禁或把不合格实现伪称验收。**

## 审查结论

阶段 P22 C 的 SpawnRegistry、固定重生时钟、LinkedRespawn、动态一次性 Spawn、Grid 加载/卸载、防重复以及可信 Checkpoint 重建防副本均有真实 Unit + P18/P19/P20 集成证据；当前 C 技术出口没有未关闭的实现阻塞。

独立 REVIEW HEAD run #206 十二 Job 全绿，P22 C 必需阻塞 0，**正式 ACCEPTED**；正式 ACCEPTED HEAD `3eb5167cdb7492e5a7a79492c28c4c01e3b8bfe5` 的 run #207 也 12 Job SUCCESS，P22 C 完整验收。真实 PostgreSQL 事务、P62 Replay 完整状态 Hash、P64 性能不包含在 C 阶段结论里。

## 正式收口

实现 #205、独立 REVIEW #206、正式 ACCEPTED HEAD #207 均 12/12 SUCCESS。315 Unit / 16 Architecture 和全部真实 Spawn/Grid/Checkpoint/WorldTick 集成通过，C 级必需缺口 0；不含 PostgreSQL 实盘跨事务恢复和 P62/P64 大规模结论。
