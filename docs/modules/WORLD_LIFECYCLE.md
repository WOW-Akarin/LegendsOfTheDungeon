# P17 — WorldInstance / Registry / Lifecycle 技术契约

状态：ACCEPTED；P17 目标 C。P15.J/P18/P24 已 ACCEPTED。实现 HEAD `15d974ab3fe3e2b863e02a9389a2d24b3f693935` 的 run #172 七 Job SUCCESS，独立 REVIEW HEAD `28dcac573e1ea024bdb78b34e3f2e939e1107382` 的 CI #173 七 Job SUCCESS；正式 ACCEPTED HEAD `404381f6764e301218d4ef76cea499abb90d46f3` CI #174 七 Job SUCCESS。

## 所有权与注册

- `Runtime.World` 是独立 Core Assembly，依赖 Kernel、Runtime.Abstractions、Entities.Core。没有具体 Platform/DB/Transport/Host 依赖。
- `WorldOwnershipRegistry` 是显式注入的进程内唯一归属协调器；一个组合根中的**全部 WorldInstance 必须共享它**，并且同一个 WorldId 同时只能注册一个 WorldInstance。共享协调器不是全局静态万能 Manager，不含实体可变能力。
- `WorldInstance` 持有私有 `Dictionary<EntityId, Entry>`，任何读者仅能取得按值 `WorldEntitySnapshot`；不能取得直接可变的 `EntityCapabilities`。实体预创建以不可变 `WorldEntitySeed` 传入。
- `WorldOwnershipRegistry.TryClaim` 和真实 P18 `BindEntity` 按顺序完成；若 Bind 失败同步释放全局预留，不制造“注册成功”假象。所有修改在同一个真实 `IWorldWriteContext` 中完成。
- `IWorldWriteContext.DemandWorld` 增加空世界也能验证的世界级真实同步 Scope；实现复用 P18 的线程/Scope/运行时独占/WorldEpoch 门禁。生命周期 Created→Loading→Running→Draining→Stopped；显式 Faulted 不自动释放所有权。
- Registry 记录 EntityGeneration 墓碑；相同 EntityId 退休后只有更大的 Generation 才能重用。P18 本身另有硬上限 10,000 个已记录 ID；P17 同样限制每 World 10,000 个不同 ID（含墓碑）。

## 实体生命周期

- `Add`：Created Seed → Attached；`Activate`：Attached → Active；`Spawn`：一次同步租约内 Add+Activate。禁止同一 World 当前活跃 EntityId 重复 Add。
- `Remove`：Attached/Active → Removing → Detached；`Despawn`：Attached/Active → Removing → Disposed，Detached→Disposed；重复调用 `AlreadyCompleted`，已过期身份 `Stale`。
- `IWorldEntityCleanup.OnRemoving` 由实体生命周期负责按登记顺序执行、每代最多一次；每实体最多 16 个，重复实例拒绝。由后续 Timer、订阅、AI、路径请求撤销组件接入，不会虚构当前已有真实 Timer/AI。
- 只有所有清理成功后才 P18 RetireEntity 并释放跨世界归属。清理异常意味着 P18 WorldExecutor 与 P17 WorldInstance 同时 Faulted，实体留在 Removing 并保留 Claim 供可信 Host 恢复；不声称自动回滚或异步清理完成。
- Stop 只允许 Draining；按稳定 EntityId 顺序清理尚附加/活动实体，撤销回执，全部释放后才能取消 WorldId 注册。
- 目标选择在 P17 Registry 验证同 World/Epoch、实际活动 Generation、Alive 状态；移除或死亡的目标由同世界其它实体同步清除对应身份引用。输出按值快照，不能留下可变目标实体引用。
- `Relocate` 仅是 P17 生命周期下验证已 Active 的实体后更新 P24 唯一 Transform。P20/P27 的 Spatial 原子索引联动尚未存在，生产世界移动入口不能提前使用此基础操作当作完整空间移动。
- `Transfer` 对有效跨世界请求明确返回 `TransferNotSupported`，不复制实体或同时设置两个 Owner。

## P17 出口与真实证据

- Unit：生命周期非法顺序、真实 P18 Scope 失效、同 World 重复 Add、跨 World 唯一归属竞争、退休与代际重用、旧 Epoch、目标失效清理、回调顺序/重复/故障、Stop 和 TransferNotSupported。
- 独立 `WorldLifecycleProbe`：真实后台 Task 在两个 WorldExecutor 并行声明相同 EntityId 时仅一个成功；真实 Worker 回执在排队后因目标 Despawn 而过期，执行前丢弃；有序 Shutdown 清理并释放世界归属、重开新 Epoch；清理异常使世界 Faulted 且不提前释放 Claim。
- CI 在既有六 Job 之外增设独立 `world-lifecycle` 阻断 Job，执行 locked restore + Release 真实集成，成功打印 `P17_WORLD_LIFECYCLE_INTEGRATION_PASS`。
- 尚不包含多进程跨节点协调、P58.T 转服、P19 Tick、P20 Spatial、P27 输入移动、P60 生产认证 Host。进程内共享协调器须由未来 Host 组合根明确保管，不可创建世界时各配一个互不相通的实例。
