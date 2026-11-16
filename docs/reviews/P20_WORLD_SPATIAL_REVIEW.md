# P20 — Spatial / Cell / Grid 独立四视角审查

- 状态：ACCEPTED；目标 C
- 稳定实现 HEAD：`875f0cd64c2dbcf34985c0c2de3a1bbc90c7921b`
- 实现 CI #185：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43903710433，9/9 SUCCESS
- 独立 REVIEW HEAD：`f9a90264fa5fbab02d5ce596414f7c2a133e5bec`，CI #186（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43904373576）9/9 SUCCESS
- 正式 ACCEPTED HEAD：本次验收状态提交，CI NOT_RUN
- 依赖 P19 Tick 与 P24 Transform 均已正式 ACCEPTED；main 直推，无分支

## 1. 功能与一致性

- Spatial 以每 World 私有 `WorldSpatialIndex` 实现三维有界整数 Cell：`floor(x/cellSize)` 处理负坐标，默认 32m，允许 1–4096m、16,384 Cell，继承 P17 最多 10,000 EntityId。
- 完整 Add/Spawn、同 Cell 与跨 Cell Move、Remove/Despawn/Stop 与索引联动；拒绝越界位置与显式 Unloaded 目标 Cell，不修改 canonical Transform、Reverse/Cell 记录；重激活后可合法进入。重复/非法状态明确返回错误。
- 查询先进行 Cell 粗筛，再根据 **P24 Transform** 精确检验闭区间 Nearby/Range，按 EntityId 升序返回不可变 Snapshot，Attached/Removing/Detached/Disposed 不作为活动实体返回。带有 Removing 回调观察的 Unit 确保移除中的实体不出现在查询结果中。
- 重建只扫描 `WorldInstance` 中 Attached/Active 的唯一 Transform；先完整构造副本、保留 Cell Active/Unloaded 状态后替换，不能从旧索引反写位置。Stop 清除全部派生桶。
- Query 同步依赖 P17 `_gate`：读者无法看见 Transform 与索引的暂态更新。若发生不可预期内部异常，世界和 P18 执行器 fail-closed；Faulted 后拒绝可能失真的空间查询，不掩盖已发生部分写入。

## 2. 架构与所有权

- Spatial 作为 Runtime.World 内的**派生索引**，没有单独位置字段副本，也不引入额外可变世界 Manager、Service Locator、真实线程、数据库或网络适配器。
- P17 负责本世界归属、Generation 墓碑、生命周期和同步锁；P18 签发且执行期间有效的 `IWorldWriteContext` 是所有空间写入和重建的授权门禁。旧 Epoch/旧 Generation 或失效 Lease 不能修改 Grid。
- P24 `TransformCapability` 仍为坐标与角度的单一真相；P20 改造原 `WorldInstance.Relocate` 而非创建第二个可写移动入口。P19 Tick 与 Hash 使用 canonical Transform，既有 P19 集成及原有七项回归保持验证。
- 没有更改 P03 依赖白名单/架构阈值；16/16 Architecture PASS。未来 Movement P27 仍负责速度/输入/碰撞、P21 可见集合单独接空间结果，而不能旁路统一重定位。

## 3. 故障隔离、资源预算与风险

- ValidateDestination 属于 Prepare：检查 Cell 边界、Unloaded、最大 Cell 数，失败直接返回拒绝、不触及状态；Commit 在 P17 同步 `_gate` 内更新 Cell 和唯一 Transform；中途异常进入 Faulted，不声称跨两个可变结构有通用事务回滚。
- Unload 只允许空 Cell，已卸载 Cell 保留拒绝 tombstone，ActivateCell 才能重启。该 tombstone 持续占用 Cell 数直到 Stop，因此大量不同空 Cell 访问后仍可能达到 16,384 上限；达到容量明确拒绝而非无限内存膨胀。
- **首版 Query 粗筛迭代所有已分配 Cell**，最坏接近 O(CellCount+候选实体数)；当前 1600 Entity 微基准可用，但不能推导大型地图无限 Cell、10 世界/10,000 实体的 P64 负载结果。未来可引入层次/分区及卸载压缩，但须保持 P24 唯一位置与负向测试。
- 注册回调仍需由可信模块遵守 P18 同步租约；没有生产 Host 自动恢复、持久化 Grid 审计、分布式转服或 P21 Visibility。发生故障保留 P17 canonical 事实供可信恢复，不将派生索引的错误读取当作成功。

## 4. 独立真实证据与首版基准

| 检查项 | 稳定实现 CI #185 |
|---|---|
| Linux .NET 10.0.404 Release / locked restore | PASS |
| Unit（新 P20 15 项；仓库总数） | **257/257 PASS** |
| P03 Architecture | **16/16 PASS** |
| P02/P03/P04/P05 质量和负向门禁 | PASS |
| 跨 Cell、负坐标与失败 Prepare | PASS |
| 850 seeded 随机 Move 对照 Nearby/Range 暴力扫描 | PASS |
| 并发读写不观察半更新 | PASS |
| 生命周期/退休/重建/停机索引清理 | PASS |
| P20 首版 Release 微基准 Artifact | PASS |
| 原有八 Job 全量回归 | PASS |
| 实现 CI | **9/9 SUCCESS** |
| 独立 REVIEW HEAD CI | **9/9 SUCCESS**，run #186，257 Unit / 16 Architecture |
| 正式 ACCEPTED HEAD CI | NOT_RUN |

Benchmark #185 原始记录（`world-spatial` artifact）：
- Ubuntu 26.04.1 LTS、4 CPU、net10.0 Release、Seed=2071。
- 1600 Entities / 700 Move / 400 Nearby，1907 Cell，查询返回条目总计 13,289。
- Move p50/p95/p99 = **0.0040 / 0.0093 / 0.0198 ms**。
- Nearby p50/p95/p99 = **0.1866 / 0.3860 / 0.4764 ms**；线程分配 7,325,944 bytes，GC0/1/2=0。
- **不以此宣称 50ms Tick 高负载 P64 通过**；当前没有容量极限性能测试或真实 P21 Visibility、P27 Movement 功能证据。

失败历史保留：#183 `Math.Hypot` 编译失败；#184 修复后首次九 Job 全绿；#185 增加 Removing/Faulted 安全边界后九 Job 再次全绿。

## 审查结论与待关闭项

P20 C 所要求的 Add/Move/Remove、Cell 生命周期、Nearby/Range、跨 Cell 更新、失败重定位、随机暴力扫描一致、同步 Transform/Spatial 可见原子性和首版性能基准均有实际 CI 证据。未发现阶段 C 必需的代码阻塞，仍有 O(CellCount) 与 Unloaded tombstone 的明确后续性能/资源限制。

独立 REVIEW HEAD run #186 九 Job 全部 SUCCESS，P20 C 所需技术证据与首版基准已通过审查，**正式 ACCEPTED**；正式验收状态 HEAD 的 CI 仍需独立复核。P21/P27/P62/P64 不属于此次结论。
