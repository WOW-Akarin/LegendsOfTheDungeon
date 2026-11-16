# P20 — Spatial / Cell / Grid 技术契约

状态：REVIEW（目标 C）；依赖 P19 与 P24 已正式 ACCEPTED。稳定实现 HEAD `875f0cd64c2dbcf34985c0c2de3a1bbc90c7921b` 的 CI #185 九 Job SUCCESS；独立 REVIEW HEAD 和 ACCEPTED HEAD 尚需各自验证。

## 唯一 Transform 真相与派生索引

- P24 `EntityCapabilities.Transform` 是位置/方向的**唯一可写真相**。P20 `WorldSpatialIndex` 是 P17 `WorldInstance` 私有的派生 Cell 桶与 `EntityId → Cell` 反向映射；**不复制第二份位置坐标**、不暴露 `EntityCapabilities`、不增加万能 Manager 或跨 World Writer。
- 所有 Add/Spawn/Remove/Despawn/Stop/Relocate 走 P17 `_gate` + 真实 P18 `IWorldWriteContext`；空间索引和 Transform 的同步修改对所有 Read API 不会暴露半更新。任何非预期修改异常向 P18 传播并将世界标记 Faulted，不假称回滚。清理回调异常同理，保留 Claim 由未来可信 Host 诊断。Faulted 世界可读取 P17 单实体 canonical 快照供诊断，但 Nearby/Range 查询会抛错拒绝将可能损坏的派生索引解释为可信结果。
- 空间 Cell 为边长 `cellSize` 米的三维整数网格，默认 32m，构造允许 1–4096m；使用 `floor(x/cellSize)`，因此 -0.01m 属于 -1 Cell，恰好 +32m 属于 +1 Cell。越过有符号 32 位 Cell 范围返回 `SpatialOutOfBounds`，拒绝而不改变位置。每 World 最多 16,384 个 Cell 记录（含空的 Unloaded Cell），已有 P17 10,000 个 EntityId 的代际上限。
- `Relocate` 统一进行真实 World/Epoch/Generation/Active 验证，计算目的地 Cell、确认仍 Active 和有预算，**Prepare 失败不修改 Transform 或派生索引**；在同一 Scope/锁下 Commit 源 Cell → 目标 Cell 与 Transform，跨 Cell 不允许两个位置同时可见。它仍不是 P27 玩家移动速度/碰撞/路径输入验证。
- Add/Spawn 在正式绑定 P18 Entity 前先检查初始 Cell；绑定成功后附加派生索引；Detach 成功同步删除成员，Stop 清理全部桶。EntityId 退休墓碑不会留在可查询活动索引，但仍留在 P17 代际 Registry。

## Query、Cell 生命周期与可重建语义

- `QueryNearby(center,Distance)` 只对活动实体按三维实际欧氏距离作闭区间判断（半径等于距离仍包含）。`QueryRange(Bounds)` 只对活动实体按三轴闭区间判断；候选由 Cell 算出后精确过滤。两个查询都按 EntityId 升序返回按值 `WorldEntitySnapshot`，不暴露索引桶或实际可变实体引用。
- `ActivateCell` 显式加载新 Cell 或重新激活 Unloaded；`UnloadCell` 仅允许空 Active Cell，occupied 返回 `Occupied`，空 Unloaded 重复返回 `AlreadyCompleted`。已显式卸载 Cell 的新 Spawn 或 Move 返回 `CellUnloaded`，不会通过隐式自动创建绕过卸载语义。
- 尚未见过的 Grid Cell 可在合法 Spawn/Move 时按有界预算自动激活；已卸载 Cell 保留状态作为拒绝墓碑。World.Stop 释放所有 Grid 记录。所有 Cell 生命周期写入要求 World 正在 Running 且真实 P18 Scope；读 API 不授予写能力。
- `RebuildSpatialIndex(context)` 只用 P24 唯一 Transform 与仍 Attached/Active 的 P17 实体，从头构造替换索引，保留现有 Cell 的 Active/Unloaded 状态；准备全部成功后才替换旧 index 字典。失败时 fail-closed，绝不反写位置真相或伪称重建成功。

## 正确性与首版性能基线

- Unit：负坐标/恰好 Cell 边界、Attach/Spawn/Remove、Active/Unloaded、跨 Cell 与相同 Cell 移动、超界/卸载拒绝、旧 Generation 过期、Stop、真实 Scope 过期、Rebuild 和 seeded 随机 500 次 Move + Nearby/Range 暴力扫描比对，以及 Removing 清理回调期间查询和 Faulted 读拒绝。
- 独立 `WorldSpatialProbe`：真实 P18 同步 Scope 进行跨 Cell、失败重定位、随机 850 次 Move 与暴力扫描对照、读写并发快照一致、退休代际/重建/Stop。成功输出 `P20_WORLD_SPATIAL_INTEGRATION_PASS`。
- 首版性能基线：Release、SDK 10.0.404、Linux x64、1600 Entities、700 次同步 Relocate、400 次 Nearby Query、固定 Seed=2071，记录单步 p50/p95/p99、初次 Spawn 时长、GC 计数、当前线程分配、CPU 数和 OS。具体时长以真正 CI Artifact 输出为准，**不设置没有测量依据的随机性能门槛，不把此微基准冒充 10 World/10,000 Entity/50ms 完整负载**。
- CI 由八个 Job 扩展成九个阻断 Job；新 `world-spatial` locked restore + Release probe + artifact。P20 C 出口必须实现 CI、独立四视角 REVIEW HEAD、正式 ACCEPTED HEAD 各自九 Job 全绿。

## 非 P20 完成项

P21 Visibility、P27 移动碰撞/速度、P29 Terrain/Navigation、P60 Host、P62 全链路 Replay、P64 性能压测和 P66 长期运行均未通过。当前 Grid 没有动态流式地形内存分页或分布式多节点拥有者，只对单 World 内已加载/已卸载 Cell 的状态负责。

## 实现验证与明确风险

- #183 `Math.Hypot` 编译 FAIL 保留，#184 首次九 Job 全绿；#185 两个加固单测后 257 Unit / 16 Architecture、9/9 CI 再次 SUCCESS。
- #185 首版基准：`P20_BENCHMARK_V1` 在 Ubuntu 26.04.1 LTS、4 CPU、.NET 10 Release，1600 Entity / 700 Move / 400 Nearby，seed=2071；Move p50/p95/p99=0.0040/0.0093/0.0198 ms；Query p50/p95/p99=0.1866/0.3860/0.4764 ms，当前线程分配 7,325,944 bytes，GC0/1/2 均 0。原始证据在 #185 的 `world-spatial` Artifact。
- 当前查询的 Cell 候选是遍历已分配桶的 O(CellCount) 粗筛，真实上限 16,384；空 Cell 即使 Unloaded 也暂留 tombstone 至 Stop，会消耗预算。这是 P64 优化、未来 Grid 生命周期接线的风险，不得伪称大规模最优或无限地图动态分页。
- Faulted 允许 P17 单体真相快照供可信诊断，但拒绝 Nearby/Range；生产 Host 恢复、跨节点所有权、P21 可见域仍待后续工作。
