# P27 — Movement State / Speed / Input 技术契约

状态：ACCEPTED（目标 C）；P20/P25/P26/P60.F 前置 ACCEPTED；初始实现待真实 CI 复验。

## 唯一状态真相与职责

- P27 `Runtime.World.WorldMovement` 提供窄 `WorldMovementIntent`、`WorldMovementSnapshot`、`WorldSpeedModel` 与 per-Entry `WorldMovementLedger`。P17 `WorldInstance` 唯一持有活动 Entity/Generation、P24 Transform 和 P20 SpatialGrid；MovementLedger **只保存会话主体、版本、最后成功序号/逻辑 Tick、固定 TickDuration 与模式，不保存第二个 Position 或路径终点**。
- 所有写入只在 P18 `IWorldWriteContext` 当前 World/Epoch/Generation 同步租约发生。可信 Host 调用 `BindMovementSubject`，经过 P60.F 的 `BoundIdentity` 才能绑定实体；外部 P18 OfferExternal 对匿名/LocalTest 无生产身份默认拒绝，并在执行时重新授权。**未来 P60 Host 适配器必须把已认证 BoundIdentity 传入构造意图，绝不从消息体复制用户声明的 subjectKey。** 当前实现不自称已接入公开 Socket 入口。
- 意图必须携带完整实体 WorldId/Epoch/Id/Generation、P60.F opaque `BoundIdentity`、绑定 `SessionVersion`、严格递增 `Sequence`、严格递增逻辑 `Tick` 与 Kernel `Transform`（构造时拒绝非有限 Position/Yaw）。领域验证 `BoundIdentity.SubjectKey + Origin + SessionVersion` 三元一致；过期 Epoch/Generation、旧会话和主体不符都不能写。
- `WorldMovementMode` 仅 Idle/Moving 最小模式；P28 路径与运动策略独立实现，P29 完整校验与处罚策略独立完成。P27 不提供网络兼容协议、客户端插值或 3D NavMesh。

## SpeedModel、控制与时间基准

- 速度唯一取自 P25 已定义键 `movement.speed` 的 **EffectiveValue**，单位米/秒；Flat/Percent Modifier 由 P25 按 Priority/Sequence 稳定叠加、来源句柄精确撤销。若未定义或超过 P27 100 m/s 输入上限，移动绑定/提交 `MovementInvalidSpeed`（不能偷偷使用默认值）。0 允许静止输入。
- 会话绑定时指定有效固定 `TickDuration`（1–1000ms）和基准 `Tick`，签发非零且不重用的绑定版本，最后成功 Sequence 初始 0。当前输入仅允许 `1..4` Tick 跨度，超过则 `MovementTickGapExceeded`，由可信 Host 重新绑定时间基准并递增会话版本。相同/旧序号或旧 Tick 一律 `MovementOutOfOrder`，失败不推进基准。
- 位移向量用轴限额优先过滤，之后按欧氏距离判定 `distance <= speed * tickDuration * elapsedTicks`；仅使用有限数字，避免平方溢出。超限 `MovementSpeedExceeded`。Rooted、Feared 拒绝位置变化，Stunned 拒绝全部输入，Dead 拒绝并使旧会话失效。P27 只是基础预算，合法击退/传送豁免、碰撞/禁区和误判定量归 P28/P29。
- 唯一写入入口仍是 P20 `WorldInstance.Relocate` 中的空间有效性检查、CellUnload 与 Transform/Spatial 同锁原子更新；普通可信世界重定位/传送通过该入口自动废止当前移动绑定，避免沿用旧基准。移动提交使用其内部同一共享核心，不额外维护空间集合。P20 SpatialOutOfBounds/CellUnloaded 拒绝时不消费 Sequence 或 Tick。
- `WorldEntitySnapshot.Movement` 是纯值只读快照，状态摘要 P19 schema v5 纳入 SessionVersion、SubjectKey、SubjectOrigin、LastSequence、LastTick、TickDuration、Mode、EffectiveSpeed；不同来源相同位置/速度不能误视同一次 Replay。v4/v5 Hash 不兼容，只能在相同构建版本比较。

## 真实验收与留存边界

- Unit：非有限构造、伪造/旧会话/乱序、跨 Grid Cell、P25 速度修饰回滚、P26 定身/死亡、P20 手工传送失效、P18 旧租约/Generation、v5 同 Tick 来源哈希。
- `MovementProbe`：真正 P17 WorldInstance、P18 WorldExecutor、P20 Spatial、P25 Modifier、P26 Control 与 P60.F DenyByDefault 探针；五组功能场景 + 100 实体/500 次提交 Release 微基准，打印 `P27_MOVEMENT_INTEGRATION_PASS`。仅测数据而非宣称 P64 性能达标。
- 旧 14 个 CI Job 全部保留，新 `world-movement` 为第 15 个阻断 Job。实现 HEAD CI、独立四视角 REVIEW HEAD CI、正式 ACCEPTED HEAD CI 均必须各自 **15/15 SUCCESS** 才能 P27 C 收口；初始提交前所有状态 NOT_RUN。
- 风险：真实生产网关、认证/撤销会话生命周期、消息体与 BoundIdentity 绑定、P28 Motion 和 P29 反作弊并未在 P27 实现；生产入口需 P60 授权和最小命令适配而不能把外部不可信 `IWorldAction` 原样投入可信 Producer。

## 实现 Gate 与独立审查

- 稳定实现 HEAD `9367a9a` CI #226 **15/15 SUCCESS**，344 Unit、16 Architecture；第十五个世界移动探针六场景与全部旧 Job PASS，Release baseline #225 已记录。
- 独立 [P27 四视角 REVIEW](../reviews/P27_MOVEMENT_REVIEW.md) 文件单独提交；审查 HEAD CI 尚未完成，正式 C 验收尚未作出。

## P27 正式验收

- 稳定实现 CI #226、独立 REVIEW CI #227 各 **15/15 SUCCESS**（344 Unit / 16 Architecture、真实 P27六类 Probe 和原有十四 Job）。本阶段目标 C 正式 ACCEPTED；正式状态 HEAD #228 15/15 SUCCESS。
- 下一 P28 Motion/Navigation，切勿把 P27 基础速度预算当成完整 P29 反作弊校验。

## 最终三阶段证据

实现 #226、独立 REVIEW #227、正式 ACCEPTED #228 **各 15/15 SUCCESS**（344 Unit、16 Architecture、六类 P27 真实 Probe/Benchmark 和旧十四项）；P27 C 收口。P19 v5 StateHash 与 v4 不兼容；生产认证网络入口归 P60，P28 Motion/P29 Violation/P64 性能独立。
