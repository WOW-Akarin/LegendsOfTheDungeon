# P26 — Control / Ownership / Tags 技术契约

状态：IN_PROGRESS（目标 C）；直接依赖 P24 已 ACCEPTED，P17 世界和 P18 同步写租约也已验收。此文档伴随初次代码提交，**本阶段 CI 尚未验证**。

## 实体能力与唯一拥有者

- `Entities.Core` 新增 `EntityControlCapability`、`EntityTagCapability`、`EntityRelationshipCapability`，仍放在 P24 薄 `EntityCapabilities` 显式组合中，不创建公开 `Get<T>`、第二个可变全能容器或平台依赖。
- 控制 `Stunned/Rooted/Silenced/Feared` 是多个**独立来源**的并集；授予使用 `EntityEffectSource(ulong非零)`，返回 `EntityEffectHandle`（包含 World/Epoch/EntityId/Generation + 递增 Sequence + Control/Tag 类型）。同来源+同控制标志重复授予返回原句柄，不重复计数；撤销一个 Stun 来源不能解除另一个来源。控制/标签令牌不同类型，不能跨能力误撤销。
- Tag 强制 ASCII 小写规范 `[a-z][a-z0-9._:-]*`，长度 1–48。永久 Tag 与同名临时多来源 Tag 独立；撤销临时句柄不会删除其他来源或永久标记。每实体最多 64 个控制来源、64 个标签来源（含永久），超限明确拒绝。外部只得 `Ordinal` 排序、不可变的标签及来源快照，不能借 `IReadOnlyList` 篡改内部集合。
- `Owner`、`Creator`、`Controller`、`Summoner` 是 **四个不同角色**，分别持有可为空的 `EntityIdentity` 四元组而不是目标 Entity 对象引用；修改一个角色不覆盖另三个。
- `WorldInstance.SetRelation` 必须在 P18 真实租约中验证真实 P17 注册表的 World/Epoch/Generation/Active。禁止跨世界、旧代际、自环以及通过多个不同关系角色形成的传递闭环；用实体关系图有界 DFS 拒绝成环后才改变唯一关系状态。
- 退出时 P17 `Detach` 在签发退休之前，撤销正在删除实体的全部出向关系、控制/标签来源，遍历注册表清理其它仍活动/附加实体对其所有四类关系的**入向引用**。新代际不会共享旧句柄或旧关系；`Stop` 按实体 ID 顺序清理；异常仍令 P17/P18 Faulted、保留 Claim，不假称任意写操作自动回滚。
- `QueryByTag` 在 Registry 锁内按有效 Tag 筛选 Active 实体并按 EntityId 升序返回按值冻结快照，P21 未来可使用 Nearby + 标签与状态进行 Visibility Filter。未接入客户端 Packet/UpdateMask。

## P19 状态摘要与重放可复现性

- P26 扩充 `WorldEntitySnapshot` 的 `Control`、`Tags`、`Relationships` 以及 Control/Tag 来源明细与递增最大序号，保证同一有效状态下撤销语义不同的来源集合**不会被误视为相同的确定性状态**。
- P19 SHA256 `WorldStateDigest` schema 从 v1/v2 升级为 **v3**，显式 little-endian 编码每实体控制标志、来源 Sequence/Source/Flag、临时标签来源、永久 Tag、有序有效 Tag 和四类含存在位的关系身份。未使用墙钟、线程 ID、字典迭代顺序；旧 schema digest 不应被当作新版本的同义值。
- 本版是 P19/P26 最小内存状态摘要扩展，仍不是 P62.F 跨 DB/网络/AI/随机流的完整回放验收。

## P26 出口证据要求

- Unit 在真实 P18 同步租约内检验：两个 Stun 来源移除一个仍受控、跨 Control/Tag 类型句柄不能误撤销、永久/临时同名 Tag、多角色独立、跨角色闭环拒绝、销毁入向关系清理、旧 Generation 句柄和失效 Lease、能力/标签上限、只读快照不会随世界变化原地更新、状态 Hash 确定性。
- 独立 `WorldControlProbe` 真实 Executor 集成 4 场景，成功必须输出 `P26_WORLD_CONTROL_INTEGRATION_PASS`；CI 由 9 Job 扩展为 10 Job，全量旧 PostgreSQL/StaticData/Networking/WorldExecutor/WorldTick/WorldSpatial 不得回归失败。
- 正式 C 验收必须经过代码实施 HEAD、独立四视角 REVIEW HEAD、正式 ACCEPTED HEAD 各自全部 10 Job CI SUCCESS；CI 真实 FAIL 必须记录修复，不得豁免 P02–P05 Gate。

## 尚未完成

- P21 Visibility 的实时 Enter/Leave Delta、距离和状态/标签过滤；P27 移动、P25 Stats/Resources、P60 Host、P62 完整 Replay 与 P64 大规模性能/长期测试均不能由本阶段替代。
- 当前 `EntityEffectSource` 是显式非零业务来源编号，撤销由签发方或其生命周期清理器发起；**来源实体销毁并不自动推导所有 EffectSourceId 引用**。P26 负责目标实体退休时清理自身来源，后续 Aura/Ability/AI 消费者须通过受信任生命周期回调撤销其他实体上的来源。
