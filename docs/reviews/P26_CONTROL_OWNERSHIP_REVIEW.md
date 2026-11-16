# P26 — Control / Ownership / Tags 独立四视角审查

- 状态：ACCEPTED，目标 C
- 稳定实现 HEAD：`7689a25d88924511cb508127e25d1aa38cc06e43`
- 实现 CI #192：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43910885713，10/10 SUCCESS
- 独立 REVIEW HEAD：`3f8e80ade8b42fd9b21e543ea4530835c7924718`，CI #193（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43911621160）10/10 SUCCESS
- 正式 ACCEPTED HEAD：`f0ee3d9601add1a799caacfa4d49c06a3179c149`，CI #194（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43912274627）10/10 SUCCESS
- 直接依赖 P24 已正式 ACCEPTED；P17/P18/P19/P20 的世界集成/租约/Hash/Spatial 也已验收。未创建分支，main 直接更新。

## 1. 功能与不变量

- `EntityControlEffects` 为 [Flags] 枚举，只允许 Stunned/Rooted/Silenced/Feared 的单项授予。每个实体代际按业务 Source + 标志独立保存效果租约，同 Source/Flag 重复返回原句柄；两个 Stun 来源撤销一个后仍 Stunned，最后一个撤销才解除控制。最多 64 个活动控制来源。
- 永久标签和按 EffectSource 授予的临时标签是不同来源；标签格式固定 ASCII lowercase，1–48 字符、Ordinal 精确匹配，最多 64 个总 Tag 来源。不同来源同名标签共同维持有效值，不会因撤销一个或永久状态改变而误删除其它来源。满额新增返回 CapacityExceeded，世界保持 Running。
- `EntityEffectHandle` 包含完整 World/Epoch/EntityId/Generation + 不复用递增 Sequence + **Control/Tag 种类**。CI #190 实际发现 Tag.Revoke 缺 Kind 校验，可利用与控制来源相同的序号误撤销 Tag；现双路径均检查 Kind 和 Identity，保留原负向单测而非修改预期。
- Owner、Creator、Controller、Summoner 保留四个独立槽位，不等同于 `WorldOwnershipRegistry` 的跨 World 唯一实体登记。修改一个角色不覆写其它角色。关系仅保存完整 EntityIdentity，不保存对象指针，不公开可写引用。

## 2. 架构与唯一所有权

- P26 新领域逻辑仅在 `Entities.Core`，仍只依赖 Kernel/Runtime.Abstractions，P03 16/16 保持通过；P17 `WorldInstance` 调用 P18 当前同步租约执行所有新写方法。网络或非可信调用方不能从 Tag/Control Snapshot 获得写入能力。
- 世界注册表二次验证关系目标真实活动的 World/Epoch/Generation，禁止跨世界目标、过期 Generation、自身引用及 **跨关系类型** 的传递循环；DFS 的节点上限被 P17 单 World 10,000 EntityId 预算自然约束。
- 外部只读取 `WorldEntitySnapshot` 的只读来源列表、有效标签集合、四类关系。底层数据先复制再包装 ReadOnlyCollection，历史快照不会在撤销或移除之后原地变化；`QueryByTag` 只返回 Active 且按 EntityId 升序的实体，不泄漏可变集合。
- P19 `WorldStateDigest` schema **v3**：显式 little-endian UTF8 稳定长度编码 Control/Tag 来源序号、来源 ID、效果标志、永久/临时/有效 Tag、四类关系及源序号计数。对**同一 Tick** 且同一有效 Stun、但来源数量不同的世界，Hash 不得相同；两次重放完全相同来源历史必须 Hash 相同。旧 v1/v2 digest 与 v3 不能假称相同协议。

## 3. 生命周期、故障与安全边界

- 每代实体退休在 P17 `Detach` 内撤销自身 Control/Tags/Relationships，并在 P18 退休前删除其它活跃/附加实体中指向该代际的所有四类入向关系；旧 Generation 句柄不影响复用 EntityId 的新代际。重复撤销返回 AlreadyCompleted，不会降级其它 Stun 来源。
- Cleanup 回调可能抛异常：遵循 P17 fail-closed，WorldInstance/P18 Executor Faulted、Claim 留待 Host 恢复，**不伪装自动回滚**。来源效果的 SourceId 是显式业务来源编号，来源实体销毁并不会自动枚举并撤销所有引用它的跨实体效果；调用方须通过自己的受信任生命周期清理器撤销。不能借这点宣称 P32 Aura/Combat 的完整生命周期已完成。
- 64 控制来源 / 64 Tag 来源限制、每次关系 DFS 上限、P17 10,000 注册代际与 P18 同步租约要求均明确。单调来源 Sequence 不复用；达到最大 ulong 将 fail-closed 而不是与旧句柄重叠。
- P21 Enter/Leave Delta、距离过滤、网络玩家绑定、P25 Stats、P60 Host、P62 全链路 Replay、P64 多世界性能均仍未实施。P26 的按值 Tag/State 投影只为 P21 提供可消费基础，不是 P21 已接受。

## 4. 真实 CI 与失败可追溯性

| 检查 | 实现 CI #192 |
|---|---|
| Linux .NET10.0.404 Release + locked restore | PASS |
| Unit 全部 | **275/275 PASS**（新增 P26 18 项） |
| P03 Architecture | **16/16 PASS** |
| P02/P03/P04/P05 正反 Gate + 文档质量 | PASS |
| P26 双来源 Stun / Tag 和句柄跨种类隔离 | PASS |
| P26 四角色不同关系、跨角色环、自环与退休清理 | PASS |
| P26 旧代际、失效租约、幂等撤销与 64 来源上限 | PASS |
| P26 v3 Hash 同 Tick 来源差异与重放一致 | PASS |
| 原有 WorldExecutor/WorldLifecycle/WorldTick/WorldSpatial + PostgreSQL/网络/诊断集成 | PASS |
| 独立 P26 `world-control` 集成 | PASS（P26_WORLD_CONTROL_INTEGRATION_PASS） |
| 完整 CI | **10/10 SUCCESS** |
| 独立 REVIEW HEAD CI | **10/10 SUCCESS**，run #193，275 Unit / 16 Architecture |
| 正式 ACCEPTED HEAD CI | **10/10 SUCCESS**，run #194，275 Unit / 16 Architecture |

透明失败链：#189 新领域 [Flags] 枚举命名 CA1711 FAIL，#190 真实测试发现 Tag.Revoke 跨种类句柄误撤销（271/272 Unit），#191 修正后首次 10/10 SUCCESS，#192 源历史 Hash/容量/旧句柄加固后 10/10 SUCCESS。**未放松分析器或跳过真实负向测试。**

## 审查结论与下一安全动作

P26 C 必需能力的控制多来源、临时 Tag、四关系、循环与清理、确定性来源摘要、真实 P18 写租约等已有实现及可复验的 CI 证据；审查中未发现当前 C 出口内必须立即修复的阻塞。独立 REVIEW HEAD run #193 十 Job 全部 SUCCESS，P26 C 必修缺口 0，**正式 ACCEPTED**。但正式 ACCEPTED HEAD `f0ee3d9601add1a799caacfa4d49c06a3179c149` run #194 也十 Job SUCCESS，P26 C 验收完整收口。P21 Visibility 尚未实现。
