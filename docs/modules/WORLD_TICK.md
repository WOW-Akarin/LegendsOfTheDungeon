# P19 — WorldTick / Scheduler 确定性技术契约

状态：IN_PROGRESS；目标 C；依赖 P07/P08/P14/P17 均已接受。实现提交后等待真实 CI，不能把提交视作 PASS。

## 固定相位与状态真相

- `Runtime.World/WorldTickPipeline` 固定 `Input → Timers → Simulation → Commit`，每阶段工作单元以 `(Phase, Key)` Ordinal 稳定排序；重复 Key 同阶段拒绝，步骤仅在创建时冻结，不能运行时动态增加或替换。
- `IWorldTickStep.Execute` 是同步回调，必须在 P18 WorldExecutor 签发的真实 `IWorldWriteContext` 中；Tick 只能从 1 严格连续递增，不允许空 World、不允许提前于 `IClock.CurrentTick` 或重复跳号，异常使 P17 WorldInstance 和 P18 WorldExecutor Faulted，不伪装回滚。
- `FakeClock` 可精确推进逻辑 Tick；UTC 审计时间不是逻辑时钟。平台计量用 `Stopwatch.GetTimestamp` / 独立计量 Port，绝不写入领域逻辑状态及 Hash。
- P17 `SnapshotForTick` 在有效租约中复制按 EntityId 升序的完整不可变快照，包括旧 Generation 墓碑。状态摘要采用带 schema version 的 SHA-256、明确 little-endian 编码：World/Epoch/Tick、实体数量、EntityId/Generation/状态/生命/坐标/Yaw/完整 Target 身份。相同输入及同序已接纳事件应得到同一状态 Hash，不依赖线程号、字典迭代、墙钟或机器默认字节序。
- 该 Hash 是 P19 最小世界状态回放入口，**不是** P62.F 全链路世界 Replay 的验收、世界落盘快照、加密鉴权、性能指标或异步事件历史。增加新领域状态需升级哈希 schema 和覆盖测试。

## 有界多世界调度

- `Platform.WorldScheduling/WorldTickScheduler` 为独立适配器；拥有 `WorldExecutor`、`WorldTickPipeline` 的显式可信绑定，不向不可信 Host/Socket 外泄 Producer。单次最多 64 World / 8 Worker；每世界最多 8 次追赶 Tick，单次 `RunBatch` 最多 1024 消息，默认 2 Worker/4 Tick/128 消息。
- 对 WorldId 唯一注册，调度周期与输出报告均按 WorldId 排序；后台 Worker 只提交 P18 `IWorldAction` 后由同步 WorldExecutor 写领域，绝不直接获得租约写权限。同一个调度器禁止并发 `RunDueAsync`，P18 自身继续拒绝同世界重入。
- 邮箱接纳不等于执行：每世界仅有一个 Pending Tick Action；Batch 若先消耗旧命令，跨轮保留 Pending，绝不重复 Offer 同一 Tick、跳过待处理 Tick 或报告虚假的已提交状态。
- 邮箱满报 `Backpressured`，世界故障 `Faulted`，停机 `Closed`，独占竞争 `Busy`，落后或本轮预算耗尽 `Deferred`。落后 Tick **不丢弃**，在后续调用中按每轮上限追赶；积压以 `RemainingDueTicks` 报告。
- `IWorldTickExecutionMeter` 测量每个真实 Tick Phase 同步执行耗时；大于 TickDuration 计入 SlowTicks；真实 Stopwatch 统计只用于运维报告，不影响可重复状态 Hash。

## 证据与限制

- `WorldTickTests`：相位排序与重复拒绝、FakeClock、有界慢 Tick、确定状态 Hash、旧写租约拒绝、重复/跳号 Faulted、邮箱满/先入队工作、并发调度拒绝、两世界真实并行、超限拒绝。
- `WorldTickProbe`：真实 P18 Executor 中的阶段顺序和跨重建 Hash 一致/输入改变差异、两世界有限 Worker 并行、32 Tick 积压有界追赶与慢 Tick、邮箱背压无假成功。成功必须输出 `P19_WORLD_TICK_INTEGRATION_PASS`。
- CI 从七 Job 扩展为八 Job，新增 `world-tick` 独立阻断真实探针。P62.F 完整回放/录制、P64.F 10 World 性能基线、P20 Spatial、P27 Player 移动、P60 生产 Host 均留给后续任务。测量到“慢 Tick”不代表满足 50ms 负载性能目标。
