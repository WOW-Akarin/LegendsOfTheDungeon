# 当前进度

更新时间：2026-11-17
分支：main（直接更新，不创建分支）
当前阶段：**P28 — Server Motion / Path（待启动）**；P27 C 正式 ACCEPTED
正式 ACCEPTED：P00–P23、P24(B)、P25(C)、P26(C)、P27(C)；P25 与 P27 实现/独立 REVIEW/正式 ACCEPTED HEAD 的真实 CI 均全绿（最新 P27 15 Job）。

## 基线真实证据

- P26 Control/Ownership/Tags：实现 #192、独立 REVIEW #193、正式 ACCEPTED #194、归档 #195 全部 10/10 SUCCESS（275 Unit、16 Architecture）。
- P26 归档 HEAD `58e84431d5acf70a743f5a3ffc6a38be90bf7e65`；CI #195：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43912821041。
- P21 直接依赖 P20 Spatial 和 P26 标签、控制，均已接受。

## P21 正式 ACCEPTED

- 新增 Runtime.World 的 `WorldVisibilityFilter`、`WorldVisibilitySet`、`WorldVisibilityView`、`WorldVisibilityDelta`。
- 只读 P20 QueryNearby 和 P26 Tags/Control/Life；仅在 P18 当前同步 World Scope 中发布带 World/Epoch/Observer+版本的 Enter/Leave；无实体位置双写、无 WoW UpdateObject/Packet。
- 初次实现包含有界距离/成员过滤、跨边界、状态变化、退休与代际复用、观察者死亡、旧租约拒绝及快照冻结的独立 Unit 和真实 P21 WorldVisibilityProbe。
- CI 新增第 11 个 `world-visibility` 阻断 Job，维持原有十 Job 回归。
- 详细入口：[P21](progress/P21.md) / [WORLD_VISIBILITY](modules/WORLD_VISIBILITY.md)。

## P21 真实 CI

- 初始 P21 HEAD `34fbcb0` CI #196（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43915892879）在编译阶段 FAIL：CS0165 观察者快照局部变量 definite assignment、CA1859 私有 Normalize 具体返回类型。均已修复且保留失败记录。
- 稳定实现 HEAD `0af03e5a96b743e2ecce85dcc6335e9d95fa2901` CI #197（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43916141338）**11/11 SUCCESS**，290/290 Unit、16/16 Architecture、P02–P05 Gate 与真实 P21 WorldVisibilityProbe 五类场景成功，原有十项 Job 均 SUCCESS。
- 新增独立四视角审查：[P21 Review](reviews/P21_VISIBILITY_REVIEW.md)，独立 REVIEW HEAD `fe790ec26a11699e38e2b54f917268faf3dd5c98` CI #198（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43916929617）11/11 SUCCESS，P21 C 正式 ACCEPTED；正式 ACCEPTED HEAD `b16fa376bc1a323e18b2fd16bf841128f553cb3c` CI #199（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43917430402）**11/11 SUCCESS**，P21 C 正式收口。

## 下一安全动作

P21 实现 #197、独立 REVIEW #198、正式 ACCEPTED #199 各自 11/11 SUCCESS。下一按路线图启动 P22 — Spawn / Respawn（P16.F/P19/P20/P24 前置已 ACCEPTED）；P21 Pull+Refresh 不代表网络推送或 P64 性能达标。

## P21 三阶段真实验收归档

- 稳定实现 HEAD `0af03e5a96b743e2ecce85dcc6335e9d95fa2901`：CI #197 11/11 SUCCESS。
- 独立 REVIEW HEAD `fe790ec26a11699e38e2b54f917268faf3dd5c98`：CI #198 11/11 SUCCESS。
- 正式 ACCEPTED HEAD `b16fa376bc1a323e18b2fd16bf841128f553cb3c`：CI #199 11/11 SUCCESS，290 Unit、16 Architecture，P21 五类真实 Probe 和原有十项回归全绿。
- 初次 #196 CS0165/CA1859 编译 FAIL 均已保留并严格修复；未降低分析/架构/负向 Gate。
- 归档 HEAD `c4fdab6fa3dee94f4eb3d0c26d899982ad3e1bc8` CI #200 11/11 SUCCESS，P21 四轮 CI 验证完整通过；P22 进入实现阶段。

## P22 首次实现（尚未验证）

- `WorldSpawnRegistry` 绑定 P17/P20 唯一真相、P18 同步租约、P19 Timers Phase 和 FakeClock；登记/生成/退休/重生使用非零唯一 SpawnKey/EntityId 和不可回退 Generation。
- 已添加 LinkedRespawn 循环检测、Grid 加载/卸载受管实体、动态一次性生成、绝对 DueTick 持久快照与 `RecoveryHold` 防重复策略。
- 新增 Unit 与真实 `WorldSpawnProbe` 五场景，CI 新增 `world-spawn`，总 **12 个阻断 Job**。首次代码 HEAD CI 尚未运行，不能称为成功。
- 详见 [P22 进度](progress/P22.md) 与 [WORLD_SPAWN 技术契约](modules/WORLD_SPAWN.md)。

## P22 稳定实现 CI #205 与独立审查

- 稳定实现 HEAD `485e94891dfd3c3c8db85196b02a6d6e755b6a26`，CI #205（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43984436900）**12/12 SUCCESS**，315/315 Unit、16/16 Architecture，真实 P22 WorldSpawnProbe 五场景和原有十一个 Job 全 PASS。
- 真实历程 #201 初次十二 Job PASS（307 Unit）、#202 因 Timer Tick 异常类型错序 311/312 Unit FAIL、#203 修复后十二 Job PASS（312 Unit）、#204 故障生命周期加固十二 Job PASS（314 Unit）、#205 防 Draining 部分副作用加固十二 Job PASS（315 Unit）。失败已透明保留，不豁免 Gate。
- 独立审查 [P22 Spawn Review](reviews/P22_SPAWN_RESPAWN_REVIEW.md) 已另行提交；独立 REVIEW HEAD `fd099e818851c255cbb125457d455eaca4b6d39c` 的 CI #206（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43984888412）**12/12 SUCCESS**，审查完成并正式 ACCEPTED。

## 下一阶段安全动作

P22 实现 #205、独立 REVIEW #206、正式 ACCEPTED #207 全部 12/12 SUCCESS。下一安全阶段是 **P23 — Terrain / Collision / Navigation Ports**，路线图依赖 P06/P09 已 ACCEPTED；P22 的 DB/Host 原子恢复和 P62 全链路 Replay 仍属后续专项。C 级只验 P22 Spawn/Timer/Grid/关联/Checkpoint 防副本安全边界，不将外部 DB/Host 事务、完整 P62 Replay 或 P64 大规模负载冒充完成。

## P22 实现 HEAD 首次真实验证与加固

- 初始 HEAD `2b02cde97635bc0e1943ead2987e3c691e5b0169` CI #201 **12/12 SUCCESS**，307 Unit/16 Architecture、真实 WorldSpawnProbe 五场景全部 PASS。
- 新增持续开发修正：拒绝定时器 Tick 跳号/回拨、每 World 4096 定义预算、已确认 checkpoint 冻结和不可信 Generation 恢复负向验证。新 HEAD CI 尚未完成，不能用 #201 代替。

## P22 固定 Tick 规则与故障边界

- #202 跳 Tick / 超时重叠错误优先级导致 Unit 311/312 FAIL，未被任何 Gate 豁免；修复后 #203 **12/12 SUCCESS**，312 Unit / 16 Architecture。
- 进一步加固 Faulted/Stopped World 的 Registry 写入拒绝及 Draining 不运行 Timers，新增两个专门 P18 真实回归，下一 HEAD CI 尚未验证。

## P22 正式验收记录

- 实现 HEAD `485e94891dfd3c3c8db85196b02a6d6e755b6a26`：CI #205 **12/12 SUCCESS**，315 Unit / 16 Architecture。
- 独立 REVIEW HEAD `fd099e818851c255cbb125457d455eaca4b6d39c`：CI #206 **12/12 SUCCESS**，315 Unit / 16 Architecture。
- P22 C 必需 Spawn/Respawn/LinkedRespawn/Grid/Checkpoint 防副本证据齐备，必修技术阻塞 0，**正式 ACCEPTED**。
- 正式 ACCEPTED HEAD `3eb5167cdb7492e5a7a79492c28c4c01e3b8bfe5` CI #207（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43985257531）**12/12 SUCCESS**。真实 DB 跨事务恢复、完整 P62 Replay、P64 负载仍不在 P22 C 结论内。

## P22 三阶段与归档实证

- 稳定实现 HEAD `485e94891dfd3c3c8db85196b02a6d6e755b6a26`，CI #205 **12/12 SUCCESS**。
- 独立 REVIEW HEAD `fd099e818851c255cbb125457d455eaca4b6d39c`，CI #206 **12/12 SUCCESS**。
- 正式 ACCEPTED HEAD `3eb5167cdb7492e5a7a79492c28c4c01e3b8bfe5`，CI #207 **12/12 SUCCESS**，315 Unit / 16 Architecture + P22 真实五场景 Probe + 原有十一项回归全部 PASS。
- 实现期间 #202 Tick 异常边界 311/312 Unit FAIL 已真实修复；其余正反质量 Gate 未降低；本次归档 HEAD 自身的 CI 待提交后运行核验，不提前伪称通过。
- 下一阶段 **P23 Terrain / Collision / Navigation Ports**（目标 B+C），直接依赖 P06/P09 已具备；不能将 P23 误写为网络 Packet。

## P23 首次实现（IN_PROGRESS，未经 CI）

- P22 三阶段 CI #205/#206/#207 全部 12/12 SUCCESS，归档 HEAD `803aba72c02efa09c9fad7cc10240ff47cf9357c`；P23 直接依赖 P06/P09 已 ACCEPTED。
- 新增 `ITerrainQuery`/`ICollisionQuery`/`INavigationQuery`，只通过 Kernel 米坐标与明确失败状态交互，绝不读 WoW Map/VMap/MMap。
- `PlanarGridEnvironment` 是真实有限平面 + 静态格障碍参考 Adapter：确定性四邻域 BFS、线段阻挡检查、版本快照、防御性路径复制；Fake 仅保留在 Port 测试。
- 明确区分区域外、阻挡、不可达、预算耗尽、逻辑截止 Tick、取消；不提供真实 NavMesh、复杂 3D 地形或 P28 Movement 集成。
- 首批 P23 Unit 和独立 EnvironmentProbe 已接入 `environment-query` CI 阻断 Job（预计 13 Job），但**尚未得到提交 HEAD 的 CI 证据；不能 ACCEPTED**。
- 详见 [P23](progress/P23.md) 与 [ENVIRONMENT_QUERIES](modules/ENVIRONMENT_QUERIES.md)。

## P23 稳定实现与独立审查

- P23 加固后实现 HEAD `cd9e804b944a3ae2ea0a5103bcede1e593b91e33`，CI #213（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43995006336）**13/13 SUCCESS**，322 Unit、16 Architecture、真实 P23 EnvironmentProbe 五场景和所有旧回归全绿。
- 失败链 #209 CA1716、#210 CA1512、#211 CA1859 保留；#212 首次 13 Job 全绿，#213 结果不变量加固后再次全绿，所有 Gate 保持。
- P23 已进入 **REVIEW**；[四视角审查](reviews/P23_ENVIRONMENT_REVIEW.md) 另行提交，必须等待 REVIEW HEAD 自身 CI 全绿后才能正式 ACCEPTED。暂无生产 P28 Movement/AI 调用方；3D NavMesh 和硬实时寻路不在此参考 Adapter 结论内。

## P23 正式 ACCEPTED 决议（状态 HEAD 待复核）

- 稳定实现 HEAD `cd9e804b944a3ae2ea0a5103bcede1e593b91e33` CI #213 **13/13 SUCCESS**。
- 独立 REVIEW HEAD `dc568e4fe317810a544e4bf8895089e2e84e4746` CI #214（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43995437055）**13/13 SUCCESS**。
- 322 Unit / 16 Architecture、五类真实 P23 EnvironmentProbe 和原有十二项 Job 回归完整通过。目标 B+C 范围内必须修复的阻塞 0，**P23 正式 ACCEPTED**。
- 本次状态提交 HEAD 的 CI **尚未运行**；不得用 #214 代表正式 ACCEPTED HEAD 的结果。未来 P28/AI 调用方验收 N/A，真实 NavMesh、硬实时抢占和 P64 负载仍非本阶段结论。
- 下一阶段建议 **P25 — Stats / Resources / Modifier**（直接依赖 P24/P07 均 ACCEPTED）。P26 已单独 ACCEPTED，P27 需要 P25 才能推进。

## P23 三阶段完整 CI 验收归档（最新证据）

- 稳定实现 HEAD `cd9e804b944a3ae2ea0a5103bcede1e593b91e33`，CI #213 **13/13 SUCCESS**。
- 独立 REVIEW HEAD `dc568e4fe317810a544e4bf8895089e2e84e4746`，CI #214 **13/13 SUCCESS**。
- 正式 ACCEPTED HEAD `82851e7ef97b22d28a16c6eb275ec0ecb330a966`，CI #215（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43995827389）**13/13 SUCCESS**。基线 322/322 Unit、16/16 Architecture、新 P23 真实五场景及原有十二项阻断 Job 全 PASS；三阶段证据齐全。
- #209/#210/#211 三次编译失败与修复记录保留，未降低 CS1591/分析器/架构/负向 Gate。P23 目标 B+C 正式收口，未来 P28/P37 真消费方、3D NavMesh 与 P64 性能单独验收。
- 下阶段按依赖图推进 **P25 Stats / Resources / Modifier**（前置 P24/P07 ACCEPTED）；本归档提交自身的 CI 不预先声称已通过。

## P25 首次实现（IN_PROGRESS，未验收）

- 前置 P24(B)/P07 均已 ACCEPTED；P23 三阶段与最终归档 #216 全部 13/13 SUCCESS。入口 main HEAD `84b43c1c59c1d22c14669dce68f41fb65c91b099`。
- `Entities.Core.EntityAttributeCapability` 拥有基础/一层派生属性、来源修饰句柄、ResourcePool 唯一可写状态。只有 P18 同步 Scope 能变更；世界通过 P17 canonical `WorldInstance` 的窄能力命令路由，不交付可变实体引用。
- 同步重算采用 decimal 两位小数 AwayFromZero、优先级与句柄序号稳定排序、最多 64 属性/64 资源/256 来源；先验证候选，成功后才提交。最大资源降低裁剪 Current、不凭空回复；死亡拒绝资源消费/回复。
- P19 `WorldStateDigest` schema v4 纳入全部属性/派生公式/来源修饰/资源账本及末尾句柄序号，区分相同有效值不同来源。
- 新增 Unit 和真实 P18/P17/P19 `AttributesProbe` 五场景，第 14 个阻断 CI Job `entity-attributes`。本次首次提交的 CI **NOT_RUN**，不可将完成代码视为阶段验收。参见 [P25](progress/P25.md) 与 [ENTITY_ATTRIBUTES](modules/ENTITY_ATTRIBUTES.md)。

## P25 稳定实现与独立 REVIEW 阶段

- P25 稳定代码 HEAD `a90505a1f4ce23dc0ac6e37c945ebb28839d5073`，CI #220（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999035617）**14/14 SUCCESS**，334 Unit、16 Architecture、真实 `entity-attributes` 五场景及原有全部 13 Job 回归通过。
- 失败历程 #217 xUnit2031 Release Build FAIL、#218 P20 既有快照相等 330/331 Unit FAIL；#219 修复后 14 Job SUCCESS，#220 256 Modifier 限额及 Resource Decimal 溢出增强后再绿。未弱化 Gate。
- 独立四视角审查 [P25 REVIEW](reviews/P25_ENTITY_ATTRIBUTES_REVIEW.md) 已作为单独提交候选，阶段进入 **REVIEW**；审查 HEAD 自身 CI 尚待运行，正式 ACCEPTED 尚未给出。下阶段 P27 必须等 P25 验收完成。

## P25 正式 ACCEPTED 决议（提交 HEAD 待验证）

- P25 稳定实现 HEAD `a90505a1f4ce23dc0ac6e37c945ebb28839d5073`，CI #220（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999035617）**14/14 SUCCESS**，334 Unit、16 Architecture、五类真实 P25 Probe。
- 独立四视角 REVIEW HEAD `6d66ae91c0c0dec713d36214869bcccaf2d7aef7`，CI #221（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999422655）**14/14 SUCCESS**。功能、架构、失败恢复与证据均无 C 级必修阻塞。
- 范围内 P25 Stats/Resources/Modifier 真实 World/P18/P19 功能 **正式 ACCEPTED（目标 C）**。当前本次状态提交 HEAD 的 CI **NOT_RUN**；不把 #221 假称为正式状态 HEAD 的结果。
- 后续不包括 P32/P34 战斗血量致死、P38 经济预留、P62 全存储 Replay 或 P64 负载。下一开发阶段依路线图 **P27 — Movement State / Speed / Input**（P20/P25/P26/P60.F 依赖满足）。

## P25 三阶段真实 CI 正式归档

- 稳定实现 HEAD `a90505a1f4ce23dc0ac6e37c945ebb28839d5073`，CI #220（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999035617）**14/14 SUCCESS**。
- 独立 REVIEW HEAD `6d66ae91c0c0dec713d36214869bcccaf2d7aef7`，CI #221（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999422655）**14/14 SUCCESS**。
- 正式 ACCEPTED HEAD `59d8d55943e5a70d340e4d04d34ff1f3834d19d5`，CI #222（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999787227）**14/14 SUCCESS**。334 Unit、16 Architecture、P25 五类真实 AttributesProbe 与旧十三 Job 全部 PASS；P25 C 三阶段完整收口。
- 初次 #217 xUnit2031 BUILD FAIL、#218 旧 P20 快照相等 330/331 Unit FAIL，均真实修复且保留证据，无 Gate 豁免；#219 首次 14 Job 全绿，#220 加固后全绿。
- 现进入下个阶段 **P27 — Movement State / Speed / Input**（直接依赖 P20/P25/P26/P60.F 均已满足）。目前 P27 未实现/未测试，不能将其纳入 P25 结论。P19 digest v4 不兼容 v3 的历史 StateHash；P62/P64 仍为后续专项。
- 本归档 HEAD 自身的 CI 需提交后独立验证，不能以 #222 代替。

## P27 初次实现（IN_PROGRESS，首次 CI 待运行）

- 上一真实完整基线 P25 归档 HEAD `bd10dfe75eda9fb3b1608aba1b277f73faaf32d5`，CI #223（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44000218931）**14/14 SUCCESS**，334 Unit / 16 Architecture。P27 直接依赖 P20/P25/P26/P60.F 均可复用。
- P27 在 Runtime.World 的独立 `WorldMovement.cs` 实现移动输入模型、SpeedModel、会话进度与真实 WorldInstance 重定位。仅保存主体/会话/输入序号/逻辑 Tick，不保存第二份 Position；唯一 Transform 与 Spatial 仍由 P20 Relocate 原子更新。
- 不信任客户端声明的主体键：`WorldMovementIntent` 改为携带 P60.F 的 `BoundIdentity`，验证来源+主体+会话版本；外部 P18 `OfferExternal` 仍默认拒绝 LocalTest、匿名与未授权。可信 Host 负责先绑定主体，P60 正式生产身份与具体协议解析未在本阶段凭空实现。
- `movement.speed` 来自 P25 原有 Attr/Modifier（上限 100 m/s），P26 Root/Stun/Fear 与 P24 死亡禁止移动；旧序号/旧 Tick/跨 4 Tick 跳变、越界空间目标拒绝不消费序号。手工世界传送/死亡显式使移动会话失效。
- P19 WorldStateDigest 从 v4 升 v5，纳入主体来源、会话版本、最后成功序号/逻辑 Tick、当前速度与模式；旧 Hash 不可直接跨版本比对。
- 新 Unit + 真实 P18/P17/P19/P60.F MovementProbe 五类场景及 100 实体×5 Tick 的记录型 Release 速度基准；新增第 15 个阻断 Job `world-movement`，本次实现尚未经过 CI，**NOT_RUN/NOT_ACCEPTED**。参见 [P27](progress/P27.md) 和 [WORLD_MOVEMENT](modules/WORLD_MOVEMENT.md)。

## P27 稳定实现与独立四视角 REVIEW

- 初始实现 #224 CS8602 Build FAIL；修复 HEAD `e401899271be3853e4a85ddea70a86669fc81d83` CI #225 **15/15 SUCCESS**（342 Unit、16 Architecture），P27 真实六类 Probe 与旧十四 Job 全 PASS。
- 额外边界加固 HEAD `9367a9ac29076bdaabc6dc356a900e1bb5ec646c` CI #226（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010607879）**15/15 SUCCESS**，344 Unit、16 Architecture、全部 P27/旧 CI Job PASS。
- [P27 四视角独立审查](reviews/P27_MOVEMENT_REVIEW.md) 单独提交，本阶段进入 **REVIEW**。审查 HEAD CI 尚未运行；正式 ACCEPTED 尚未下结论。

## P27 正式 ACCEPTED 决议（状态 HEAD CI 待跑）

- 稳定实现 HEAD `9367a9ac29076bdaabc6dc356a900e1bb5ec646c`，CI #226（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010607879）**15/15 SUCCESS**。
- 独立 REVIEW HEAD `d40b21b1d2759df5b0ba1dc23d7e420d67e55895`，CI #227（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010992018）**15/15 SUCCESS**。344 Unit、16 Architecture、真正 P27 MovementProbe 六类场景与原有 14 Job 回归完整全绿。
- P27 目标 C 在 P60.F 已认证主体、会话/序号与逻辑 Tick、P25 Speed Modifier、P26 控制及 P20 Relocate+Spatial 原子一致性范围内正式 **ACCEPTED**，四视角必需阻塞 0。
- 真实生产 P60 网络命令绑身份、P28 Motion/Navigation、P29 完整 Violation/Corrections、P62 Replay/P64 负载仍独立验收；P19 Hash v5 与 v4 不可直接比较。
- 本次正式状态 HEAD CI 尚未运行，**不能以 #227 代替正式验收状态 HEAD 自身的证据**。下一阶段 P28 — Server Motion / Path，依赖 P23/P27 已满足。

## P27 三阶段正式 CI 与归档证据

- 稳定实现 HEAD `9367a9ac29076bdaabc6dc356a900e1bb5ec646c`，CI #226（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010607879）**15/15 SUCCESS**。
- 独立 REVIEW HEAD `d40b21b1d2759df5b0ba1dc23d7e420d67e55895`，CI #227（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010992018）**15/15 SUCCESS**。
- 正式 ACCEPTED HEAD `df3e192471c4ec9ebe976c42e5c8bb9994e0c203`，CI #228（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44011308289）**15/15 SUCCESS**，344 Unit、16 Architecture，六类真实 P27 MovementProbe/基准及既有十四 Job 全 PASS。
- 初次 #224 CS8602 Release 编译 FAIL，#225 修复后首次 15/15 全绿（342 Unit），#226 加固后 15/15 全绿（344 Unit），全部原始失败和修复保留，未禁用 Nullable/Analyzers 或旧测试。
- P27 目标 C 技术与审查全部正式 ACCEPTED。下阶段 **P28 Server Motion / Path**，直接依赖 P23/P27 均 ACCEPTED；P28 目前尚未实施。P60 生产网关、P29 Violation、P62/P64 仍为后续专项。
- 本次归档 HEAD 自身 CI **尚未运行**；不能将 #228 误当归档 HEAD 的验证结果。
