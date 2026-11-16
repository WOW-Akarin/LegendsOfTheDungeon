# P19 — WorldTick / Scheduler 独立四视角审查

- 状态：REVIEW，目标 C；正式 ACCEPTED 尚未给出
- 实现 HEAD：`630615a915fe0e976314e94c2b88fad7c7f501ff`
- 实现 CI #179：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43897180562，八 Job 全部 SUCCESS
- 独立 REVIEW HEAD：本审查提交，CI NOT_RUN；正式 ACCEPTED HEAD：NOT_RUN
- 基线依赖：P07/P08/P14/P17 均 ACCEPTED；main 直接更新，不创建分支

## 1. 功能与确定性

- `WorldTickPipeline` 只有 Input/Timers/Simulation/Commit 四个冻结顺序；步骤在构造时按 Phase/Key Ordinal 排序，重复键/不合法阶段/超过预算被拒绝。
- 真实 P18 Scope 中完成 Tick 1、2、3……；提前、重复、跳号以及 Phase 内异常都会令 P17 WorldInstance 和 P18 WorldExecutor Faulted，且不会记为已提交 Tick。P19 不再出现 #177 的两层故障状态不一致。
- FakeClock 控制到期，真实 UTC 审计时间改变不会改变状态 Hash。SHA256 使用显式 little-endian、schema 版本、World/Epoch/Tick 与按 EntityId 排序的 EntityIdentity/Generation/生命周期/Life/Transform/Target；不混入字典顺序、线程信息或计量时间。
- 相同初始状态/接纳操作/固定步骤序列应得到同一 Hash；不同 Transform/目标/生命变化得到不同 Hash。Hash **不包含尚未迁入 P17 Registry 的独立外部模拟器内部状态**；后续扩展领域状态须显式修订摘要 schema，不能把本阶段称为完整 P62 Replay。

## 2. 架构与所有权

- Domain 逻辑与快照在 `Runtime.World`，P19 平台适配器只负责 Worker/背压/单调耗时，不持有可变 EntityCapabilities，也不重写 P18 Scope 认证。
- P03-006 在 CI #178 正确拦截了 `WorldTickBinding(WorldExecutor)` 平台实现泄漏，已改成 Runtime.Abstractions `IWorldTickDispatchPort`，P18 真实 Executor 显式实现；P19 ProjectReference 与 public API 中均无 P18 具体项目，不调低架构 Gate。
- `IWorldTickDispatchPort` 是只供可信 Host 组合根保管的投递+消费能力，不得交给不可信 Socket/HTTP 代码。跨世界全局所有权仍在 P17 的显式共享 `WorldOwnershipRegistry`；调度器不提供万能世界状态查找器。

## 3. 安全、故障、负载边界

- 单个调度实例不可重入，P18 仍按 WorldExecutor 唯一同步 Scope 拒绝同世界并发。最多 64 World/8 Worker、每世界每轮 8 Tick、RunBatch 1024 消息是硬预算，不代表这些峰值已通过性能压测。
- 每世界只保留一个 Pending Tick：即使此前 P18 邮箱消费了更早的 Action，下一轮也不会重复 Offer。同世界正常积压不能静默跳过；Mailbox Busy 与 Accepted、真实 Commit 区分明确。
- 慢 Tick 计量来自真实 Stopwatch 或测试注入的单调 IWorldTickExecutionMeter，数据用于报告，不参与 determinism。超过 TickDuration 计为 Slow；测试用 Fake Meter 证明门槛和追赶预算，**未证明 10 World/10,000 Entity/50ms 负载通过**。
- 同进程一个 World 可以被两个独立调度器不正确地重复绑定；当前 P18/领域 Tick 校验将拒绝或在竞态时使 World Faulted。可信 Host 必须保证每世界只配置一个调度器，这属于组合根约束，未来 P60 Host 接线需另加自动守护。
- 领域回调异常可能已部分改写世界，P18/P17 会 fail closed、保留故障状态，不假称事务恢复。P19 不触碰生产认证/多进程/存储回放。

## 4. 真实证据

| 检查 | 实现 CI #179 |
|---|---|
| Linux .NET 10.0.404 locked restore + Release build | PASS |
| Unit（P19 新增 12） | **242/242 PASS** |
| P03 Architecture | **16/16 PASS** |
| P02/P03/P04/P05 构建、文档和负向合约 Gate | PASS |
| P19 固定 Phase/Hash 真实同步世界回放 | PASS |
| P19 双世界有限 Worker 并行及无同世界重入 | PASS |
| P19 FakeClock 有界追赶与慢 Tick 度量 | PASS |
| P19 Mailbox Backpressure 不虚报已提交 | PASS |
| 原 P11/P13/P14/P15/P16/P17/P18 集成回归 | PASS |
| 本轮全部 CI | **8/8 SUCCESS** |
| 独立 REVIEW HEAD | NOT_RUN |
| 正式 ACCEPTED HEAD | NOT_RUN |

过程失败透明保留：#176 测试格式化 CA1305、#177 重复 Tick 两层故障状态不一致、#178 P03-006 具体平台类型泄漏；全部修复后 #179 真实八 Job 全绿，无放松门禁或跳过测试。

## 审查结论

P19 C 最小技术交付（确定性 Tick、稳定状态 Hash、逻辑时钟、多世界有界调度、慢 Tick、背压）具备实证，实现 CI 无必需阻塞。但本审查提交的 CI 尚未运行，**当前只能标记 REVIEW**。待 REVIEW HEAD 八 Job 全绿，再提交正式 ACCEPTED，并独立核验 ACCEPTED HEAD。

保留后续边界：P20 Spatial、P27 Movement、P60 Host 生产身份、P62.F 全链路回放、P64.F 性能、P66 长稳。不将最小 P19 重放 Hash 等同于完整回放/强性能证据。
