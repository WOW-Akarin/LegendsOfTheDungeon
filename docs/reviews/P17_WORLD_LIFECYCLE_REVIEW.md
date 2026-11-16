# P17 — WorldInstance / Registry / Lifecycle 独立四视角审查

- 状态：REVIEW；目标完成度 C，尚未正式 ACCEPTED
- 实现代码基线：`15d974ab3fe3e2b863e02a9389a2d24b3f693935`；run #172 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43891431383
- 单独 REVIEW HEAD：本次审查提交，七 Job CI NOT_RUN
- 基线依赖：P15.J、P18、P24(B) 均正式 ACCEPTED；main 直推未创建分支
- 审查对象：`Runtime.World`、P18 `DemandWorld` 扩展、P03 共享职责、`WorldInstanceTests` 与 `WorldLifecycleProbe`

## 1. 功能与状态机

- 世界状态只允许 Created→Loading→Running→Draining→Stopped，异常时 Faulted。空世界的 BeginLoading/Start 同样检查 P18 真实同步 Scope，错误的 WorldId/Epoch 拒绝。
- `WorldEntitySeed` 是不可变构造意图，`WorldInstance` 内部私有 EntityCapabilities；外部取得 `WorldEntitySnapshot` 按值，不能借用外部持有可变能力绕过世界 Registry。
- `Add/Activate/Spawn` 对应 Created→Attached→Active；`Remove/Despawn` 对应 Removing→Detached/Disposed，重复成功动作明确返回 `AlreadyCompleted`、并不会二次清理。旧 Generation 即使 ID 相同也返回 `Stale`。
- `SelectTarget` 通过真正 Registry 检查同世界、同 Epoch、活动代际和 Alive；目标死亡/移除同时清空其它实体引用；被移除实体自己的目标也在退休前清空，专门修复 #170 暴露的停机顺序问题。
- `Relocate` 已验证 P24 唯一 Transform 更新与活动状态，**未宣称已完成 P20 Spatial 原子索引**；`Transfer` 仍返回 `TransferNotSupported`。

## 2. 架构与所有权

- Runtime.World 只引用 Kernel、Runtime.Abstractions、Entities.Core；没有反向依赖 Platform.WorldExecution、具体 Transport/DB/Host，也没有运行时 Get<T> 服务定位器。
- `WorldOwnershipRegistry` 只保持同进程 WorldId→实例及 EntityId→(owner,不可变身份) 的小范围 Claim，不持有任何 EntityCapabilities/Spatial 或业务可变状态。必须由**同一个 Host 组合根显式共享**，否则独立 Registry 不能保证跨世界排他；P58.T 多节点转移未支持。
- `WorldInstance` 单世界内部 Entry 是唯一生命周期 Owner；所有世界写调用 `DemandWorld`，真正修改实体前再验证其原始 World/Epoch/Entity/Generation。Kernel 触发 P03-008 三模块 fan-in 时已基于纯值/强类型基础职责补充清单 review，不降低阈值。
- 10,000 个不同 EntityId 上限与 P18 技术代际账本一致（包含已退休墓碑）；每实体 16 个清理钩子，避免无限积压。相同 EntityId 只有更高代际可重用。

## 3. 故障、安全与恢复边界

- BindEntity 失败时必须释放尚未对外发布的全局 Claim；真实 Unit 回归证明不会泄漏或产生未绑定却“已活跃”的实体。
- Cleanup 钩子按注册顺序同步调用，生命周期先转 Removing；异常向外传播并同时使 P17 WorldInstance 与 P18 WorldExecutor Faulted，Claim 保持，不能在部分写入后冒充自动回滚。
- 退休前撤销自身和其它实体的目标；`RetireEntity` 清理签发过的 Worker 回执，**即使回执事先被关键车道接纳也会执行前二次失效检查**。真实异步 Worker 的旧回执未执行、旧目标未变。
- 不可信外部身份仍由 P60.F 默认拒绝；`WorldOwnershipRegistry` 仅适用于同进程并显式共享的世界。当前无真实生产 Host、持久恢复或转服，绝不解释为“多节点全局唯一”。

## 4. 真实证据与失败保留

| 项目 | 实现 CI #172 |
|---|---|
| SDK .NET 10.0.404 / Release / locked restore | PASS |
| Unit（P17 新增 14，整体） | **230/230 PASS** |
| P03 Architecture | **16/16 PASS** |
| P02/P03/P04/P05 工程与负向门禁 | PASS |
| P17 并行世界唯一归属实测 | PASS |
| P17 真实 Worker 排队过期回执 | PASS |
| P17 Stop 清理、Epoch 重开 | PASS |
| P17 Callback 故障与 Claim 保留 | PASS |
| P11/P13/P14/P15/P16/P18 原有集成回归 | PASS |
| 完整 CI | **7/7 SUCCESS** |
| 独立 REVIEW HEAD CI | NOT_RUN，待此次文档提交 |
| 正式 ACCEPTED HEAD CI | NOT_RUN |

过程失败透明保存：#167 NuGet NU1004 漏传递 Project、#168 P03-008 缺职责说明、#169 world-lifecycle YAML 路径误拼、#170 真停机残留 Target。均修复而非跳过，#171 七 Job 首次完整 SUCCESS，#172 加固故障标记与 Claim 回滚后再七 Job 全绿。

## 审查结论与待关闭项

P17 C 规定的注册/唯一所有权、状态转换、重复操作、销毁过期回执、跨世界冲突、拒绝转移在实现层已有真实验证证据。审查未发现本阶段技术 C 必需代码阻塞。**但独立 REVIEW HEAD 尚未跑 CI，状态仍为 REVIEW。** 完成独立七 Job 检查再标记 ACCEPTED，随后另行核验正式验收 HEAD。

后续有界边界：P19 Tick/Scheduler、P20 Spatial、P27 移动输入、真实 Timer/AI/订阅适配器、P60 生产 Host、P58.T 多节点转移、P64/P66 性能长稳，均不属于此次 ACCEPTED 的事实。
