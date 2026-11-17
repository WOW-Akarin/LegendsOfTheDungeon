# P22 — Spawn / Respawn 技术契约

状态：IN_PROGRESS（目标 C）；依赖 P16.F/P19/P20/P24 均正式 ACCEPTED。本次代码尚未经过 CI，不得预先标记通过。

## 单一写所有权和注册表语义

- 新增 `Runtime.World.WorldSpawnRegistry`，属于单 World、显式注入 P17 `WorldInstance` 和 `IClock` 的**有限编排层**。它**不拥有** P17 实体 Registry、P24 Transform 或 P20 Cell 的第二份可变真相；创建/退休只能委托原有 `WorldInstance.Spawn/Despawn`，并在真实 P18 同步 `IWorldWriteContext.DemandWorld` 中运行。
- `WorldSpawnDefinition` 包含独一的非零稳定 `WorldSpawnKey`、本 World 独占的 `EntityId`、有限初始 `Transform`、`Duration` 和 `Static/Dynamic` 模式。每世界最多 4096 个 Spawn 定义；两个 Key 指向同一 EntityId、一个 Key 指向不同定义，明确返回 Conflict，不根据 Grid 重载创建相同记录。
- 实际实例身份为 `World/Epoch/EntityId/Generation`，`LastGeneration` 仅在 P17 确认新实体生成成功后递增；静态实体每次重生使用更大 Generation；当前代际存于仅含值的 `WorldSpawnEntrySnapshot`。不存在以旧 EntityId/Generation 重绑新实例的行为。
- `RegisterStatic` 只建立定义，不隐式加载 Cell 或创建实体。确切 Grid 坐标来自 P20 `TryLocateCell`（负数向下取整）；`LoadGrid` 明确激活 P20 Cell 后，下一次 Timers 阶段才有资格生成。已卸载/未创建的 Cell 绝不凭 Timer 隐式激活。重复加载、重复注册不会新增实例。
- `SpawnDynamic` 在 P18 Scope 中直接生成**一次性**实例，但必须先显式加载目标 Cell；失败不发布新的 Registry 记录。动态实例退休后状态 `Retired`，不会被 Tick Timer 复活。
- `UnloadGrid` 先检查 P20 该 Cell 总成员与 Registry 已确认的活动实例数一致，存在**任意非受管实体**则返回 Occupied、不退休已确认实例。可安全卸载时按 SpawnKey 升序退休该 Cell 的受管实例后调用 P20 `UnloadCell`；故障时进入 P17 Faulted，不谎称原子回滚。重载后静态实例必须同时满足 due Tick 与 Grid Active 才可重生。

## RespawnTimer 与 LinkedRespawn

- `WorldSpawnRegistry` 实现 `IWorldTickStep`，固定 `Phase = Timers`、`Key = world.spawn-respawn`，被 P19 `WorldTickPipeline` 以固定 Phase/Key 次序调度。无系统 `Timer`、真实 DateTime 或后台写入。
- 静态实体退休时由 `LogicalTimer.Start(IClock,RespawnDelay)` 计算单调到期逻辑 Tick，时长向上对齐至固定 TickDuration；在此之前不会重生，到期仍需 Cell Active。Ticker 必须严格逐 Tick 连续，重复同 Tick 幂等，跳号与历史 Tick 拒绝（不发布部分结果）。0 延迟也不会在**同一个已处理 Tick**重复生成，每次成功 `Advance` 只处理一次 Tick。
- 对不经 Registry、直接通过 P17 销毁的已确认实例，下一次 Timers 阶段核对到它已退休，再新建完整 Timer；如果 P17 EntityId 被其它活跃 Generation 占据，进入 `RecoveryHold` 而不试图抢占或重复创建。
- `SetLinkedRespawn(child,parent)` 只允许存在的静态 Spawn Key，形成父子有向关系：**子节点必须等其父 Spawn 对应实体真实 Active 后才能生成**。自环、两节点环和任意跨链环均用有界图遍历拒绝。执行顺序按依赖深度、再按稳定 SpawnKey 排序，避免随机字典顺序导致子节点先于父节点。
- 当前 LinkedRespawn 是确定性父活动门槛，不主动级联杀死存活的子实例。完整 AI/战斗联动待后续阶段；不能将本阶段解释为通用群组战斗机制。

## Checkpoint / Crash Recovery / 防副本安全闸

- `Capture(context)` 只在 P18 Scope 中形成 `WorldSpawnCheckpoint`（World/Epoch/LastTick/Version、Spawn 定义、Cell、状态、LastGeneration、确认的 Current Identity、DueTick、LinkedTo），条目按 SpawnKey 排序并冻结；**它不是 DB 落盘成功证据**。P11/P60 可信外部持久化 Adapter 必须自行确保快照与 P17 canonical 世界归属的提交顺序。
- `Restore(context,world,clock,checkpoint)` 检验世界/代际、时钟、配额、实体 ID 与 SpawnKey 唯一性、位置派生 Cell、计时器结构以及 LinkedRespawn 图无环。恢复本身不生成任何实体。
- 如果 checkpoint 声称已确认的 `Active` 实例而新 WorldInstance 尚未恢复**完全相同的 EntityIdentity 且 Active**，该 Spawn 进入 `RecoveryHold`；不因暂时看不到实体就推断其已死亡并生成新副本。通过 `ConfirmRecovered` 只能在 P17 当前 Registry 已确实恢复相同完整代际时解除闸门，无法通过伪造确认绕过。Checkpoint 等待状态遇见冲突的其它 Live EntityId 也进入此闸。
- 等待重生的 Timer 保存**绝对 DueTick**；恢复后以当前逻辑 Tick 比较，不按墙钟重新启动一个随机延迟。真实数据库/外部 WorldInstance 构造和跨进程完整原子 Checkpoint 未完成，应明确标记为 Adapter 待办；测试模拟的是**已确认快照输入**与真实新/旧 P17 世界对象的无副本重建。
- P22 Registry 的内部状态尚未并入 P19 的 `WorldStateDigest`（它计算的是 P17 活跃实体 canonical 状态）；P22 Checkpoint 已独立保留 Timer/SpawnKey/关联图供未来 P62.F 合并回放，不冒充已经实现跨存储 replay Hash。

## 出口测试

- 实际 Unit：重复注册/Cell 加载不重复生成、FakeClock 2/3 Tick 边界与 Generation 递增、0 延迟幂等、未加载与卸载 Grid、外来实体占据 Cell 拒绝卸载、一时动态生成、关联循环与父先子后、外部 P17 退休识别、当前实例匹配恢复、崩溃恢复缺 canonical 的阻断、恢复已等待 Timer、冲突 Live Entity 阻断、P19 Timers 真正接线的重复 Replay、过期 P18 Scope 拒绝。
- 独立 `WorldSpawnProbe` 使用真实 WorldExecutor、WorldInstance、WorldSpatialIndex 与 FakeClock，覆盖防重复、LinkedRespawn、Cell 卸载、动态一次性和崩溃恢复，共 5 个场景；通过时输出 `P22_WORLD_SPAWN_INTEGRATION_PASS`。
- CI 新增第 12 个阻断 `world-spawn` Job：.NET 10.0.404、locked restore、Release Probe 和 Artifact；此前 11 个阻断集成与单元、架构、P02–P05 全部持续有效。
- C 级正式验收需**实现 HEAD、独立 REVIEW HEAD、正式 ACCEPTED HEAD** 分别运行 12/12 SUCCESS，再归档真实记录；任何失败先修复不得削弱质量 Gate。

## 初次代码 HEAD 的真实 CI

- 实现 `2b02cde` CI #201：12/12 SUCCESS，307 Unit / 16 Architecture；独立 WorldSpawnProbe 验证唯一注册、FakeClock、关联环、Grid 卸载、动态点和崩溃防副本。
- 首次成功后审查补强了跳 Tick 拒绝、4096 条定义预算、Checkpoint 旧快照冻结和完整 Generation 恢复防冒认，新 HEAD 尚需再跑全部 12 Job，**不可提前 ACCEPTED**。

## 世界故障边界加固

- World.State 为 Faulted/Stopped 时所有 SpawnRegistry 写 API、Capture 都拒绝；保留 `TryGet` 快照供可信诊断。Draining 期间 `Advance` 拒绝继续生成，静态/动态新增也因 P17 Running 状态拒绝。
- 新增 P18 Scope 真实测试：Faulted 时 Tick/Despawn/SetLinkedRespawn/Capture 均不能成功，且 Version、LastProcessedTick、已确认 Current 不改变；Draining 不能推进 Spawn Timers，Stop 后不能再激活 Cell。加固 HEAD CI 仍需实际执行，不能提前宣称通过。

## Draining 期间的副作用前置拒绝

- `UnloadGrid`、`SetLinkedRespawn` 和 `ConfirmRecovered` 明确要求 P17 世界仍为 Running。尤其卸载 Grid 必须在退休任何确认实例前拒绝 Draining，以免发生先 Despawn 再 UnloadCell InvalidState 的部分提交；`Despawn` 仍可在 Draining 中用于可信关闭清理。
- 增加对应真实 P18 生命周期单测。正式 REVIEW 前必须对最新 HEAD 跑完整 12 Job；此处不表示测试已通过。
