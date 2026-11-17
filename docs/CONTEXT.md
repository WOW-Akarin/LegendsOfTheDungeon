# 当前上下文

更新时间：2026-11-17；GitHub `WOW-Akarin/LegendsOfTheDungeon`；main 直推、不创建分支。
项目：通用 .NET 10 游戏服务器；非 WoW 3.3.5a 客户端兼容。架构基线：模块化单体、World Actor 独占同步 Scope、领域唯一真相、P03 严格依赖边界。

## 当前最新阶段

P00–P23、P24(B)、P25(C)、P26(C) 均正式 ACCEPTED。最新 P25 稳定实现 #220、独立 REVIEW #221、正式 ACCEPTED HEAD `59d8d55` #222 均 14/14 SUCCESS（334 Unit / 16 Architecture）；当前 **P27 Movement State / Speed / Input（ACCEPTED，C）** 已具备三阶段真实 CI；当前 **P28 Server Motion / Path（IN_PROGRESS）**（直接依赖 P23/P27 ACCEPTED），首次真实 CI 尚未运行。main 直推、不创建分支。

## 前置阶段

P00–P20、P24(B)、P26(C) 已正式 ACCEPTED。最新 P26 归档 HEAD `58e84431d5acf70a743f5a3ffc6a38be90bf7e65`，CI #195 10/10 SUCCESS（275 Unit/16 Architecture）。P17 WorldInstance、P18 WorldExecutor、P19 确定性 Tick、P20 Spatial Grid、P26 控制/标签均具备真实验证。

## 当前 P21

**P21 — Visibility / Relevance（ACCEPTED，目标 C）**，依赖 P20 + P26 已满足，首次代码 HEAD 尚无 CI 通过证据。

- `Runtime.World.WorldVisibilitySet` 为每个完整 Observer EntityIdentity 私有的派生有界可见集合；不占有可变实体或第二套 Transform。真实 P18 World Scope 发布版本化只读 `WorldVisibilityView` 与 `WorldVisibilityDelta`。
- 查询从 P20 Nearby 空间候选按距离闭区间筛选，P26 Required/Excluded Tags、Life 与 Control 状态后按 EntityId 排序；观察者本人永不属于自身列表。
- 每次成功 Refresh 获得连续 per-observer SnapshotVersion 和 World/Epoch/Observer；重复刷新不重复 Enter/Leave。目标销毁或旧代际被替换、观察者退出/死亡、Tag/Control/位置跨界在下次 Refresh 输出正确 Delta；历史视图不被原地修改。
- 最多 1024 Visible、半径最大 4096m，容量溢出不会发布部分版本；Faulted 世界拒绝刷新，P18 过期租约不能写。
- 新增 Unit、真实独立 WorldVisibilityProbe、第 11 个阻断 Job。实现 #197、独立 REVIEW #198 均 11 Job SUCCESS，正式 ACCEPTED HEAD #199 也 11/11 SUCCESS，P21 C 完整验收。

## 当前审查与下一安全动作

- 初次 CI #196 CS0165/CA1859 编译 FAIL 已修复，稳定实现 #197 11 Job 全绿，相关记录保留于 [P21](progress/P21.md)。
- 独立四视角审查：[P21 Review](reviews/P21_VISIBILITY_REVIEW.md)。独立 REVIEW HEAD 已 #198 十一 Job SUCCESS，正式 ACCEPTED，状态提交 HEAD #199 十一 Job SUCCESS。

## 不属于 P21 验收

目前只实现 Pull + Refresh，不是即时网络推送。后续 Packet/客户端编码、P27 Movement、P60 Host 授权、P62 Replay 和 P64 高负载尚未通过。P21 独立审查 #198 已 11 Job 全绿，P21 正式 ACCEPTED；P21 完成后按路线图推进后续已满足依赖的阶段。

入口：[PROGRESS](PROGRESS.md)、[P21](progress/P21.md)、[WORLD_VISIBILITY](modules/WORLD_VISIBILITY.md)、[DEVELOPMENT_ROADMAP](DEVELOPMENT_ROADMAP.md)、[ARCHITECTURE](ARCHITECTURE.md)。

## P21 正式收口与下一安全动作

- #197 稳定实现 11/11 SUCCESS；#198 独立 REVIEW 11/11 SUCCESS；#199 正式 ACCEPTED HEAD 11/11 SUCCESS（290 Unit/16 Architecture）。
- 下一阶段 **P22 Spawn / Respawn**，直接依赖 P16.F/P19/P20/P24 已全部 ACCEPTED，需实现 SpawnRegistry、RespawnTimer、LinkedRespawn、防重复 Grid 加载与崩溃恢复对照。
- P23 实际路线图是 Terrain / Collision / Navigation Ports，**不是网络 Packet 阶段**；P21 历史记录中的误写已纠正。

## 2026-11-17：P22 当前实施边界

- P21 归档 HEAD `c4fdab6fa3dee94f4eb3d0c26d899982ad3e1bc8` 的 CI #200 11/11 SUCCESS；此前 P00–P21、P24(B)、P26(C) 均已验收。
- 当前转入 **P22 Spawn / Respawn（IN_PROGRESS，目标 C）**；直接依赖 P16.F、P19、P20、P24 均满足。
- 新 P22 单世界 `WorldSpawnRegistry`：唯一 SpawnKey 与 EntityId、单调 Generation、静态/动态 Spawn、P19 Timers Phase/LogicalTimer + FakeClock、关联环检测、P20 Grid Active/Unloaded 与防非受管占用。
- 崩溃恢复只接受外部已确认 Checkpoint：Active 实体若未由 P17 恢复为完全相同 Generation，则 `RecoveryHold` 阻止自动复制；定时器使用绝对 DueTick。此阶段没有真正 PostgreSQL 持久事务、跨进程完整 Replay 或 Spawn 业务 Catalog Adapter 绑定，不可冒充全部实现。
- 已新增 Unit 与独立真实 P22 WorldSpawnProbe，第 12 个 CI Job。**初始实现 HEAD 的 CI 未运行**；保持 main 直推，不创建分支。必须等完整实现、独立 REVIEW 和 ACCEPTED 各自 12/12 SUCCESS 才正式验收。
- P21 旧章节中 Pending REVIEW 的说明以此 P22 更新和 docs/progress/P21.md 的最新正式归档为准。

入口：[P22 进度](progress/P22.md)、[WORLD_SPAWN](modules/WORLD_SPAWN.md)、[PROGRESS](PROGRESS.md)、[ROADMAP](DEVELOPMENT_ROADMAP.md)。

## P22 初次真实 CI

- 初次代码 HEAD `2b02cde` CI #201 12/12 SUCCESS，307 Unit、16 Architecture、P22 真实五场景 Probe。
- 已加强 Timers Tick 连续性/过期拒绝、4096 定义上限、旧 Checkpoint 冻结及恢复 Generation 隔离；新的代码 HEAD CI 尚待重跑。

## P22 实现复核

- #201 12/12 SUCCESS（307 Unit）；#202 311/312 Unit FAIL（跳号/超时重叠的异常类型）；#203 修复后 12/12 SUCCESS（312 Unit / 16 Architecture）。
- 最新安全加固将 Faulted/Stopped 下 SpawnRegistry 所有写入及 Capture 禁止，Draining 下 Tick 禁止，另增两项生命周期回归；新 HEAD CI 尚需验证。

## P22 稳定实现审查 HEAD

- 稳定 HEAD `485e94891dfd3c3c8db85196b02a6d6e755b6a26` CI #205 **12/12 SUCCESS**（315 Unit/16 Architecture，全部旧集成与 P22 真实 Probe）。
- P22 状态转为 **REVIEW（目标 C）**：独立审查 [P22](reviews/P22_SPAWN_RESPAWN_REVIEW.md) 已另行提交，但 REVIEW HEAD CI 尚未验证。
- 失败历史 #202（311/312 Unit FAIL，Timer Tick 非法组合的异常类型），#203 修复后 12 Job PASS、#204 Faulted/Stopped 守卫 12 Job PASS、#205 Draining Grid 前置拒绝 12 Job PASS。
- Checkpoint 是明确的内存领域快照 + 外部持久化 Port 边界，并非 PostgreSQL 真实跨事务提交；崩溃后确认实例无 P17 真实恢复回执时永久 RecoveryHold，不能自动猜测再生。P62 完整跨存储 replay 与 P64 负载仍待后续阶段。

## P22 正式验收状态（待验收 HEAD CI）

- 稳定实现 `485e948` CI #205 12/12 SUCCESS，独立 REVIEW `fd099e8` CI #206 12/12 SUCCESS；315 Unit / 16 Architecture、P22 真实 Probe。
- **P22 正式 ACCEPTED（目标 C）**，本次正式状态提交 HEAD CI 尚需独立 12 Job 完整核验。
- P23 — Terrain / Collision / Navigation Ports 是后续可独立展开的候选阶段，直接依赖 P06/P09；P22 不代表 P23 已完成。

## P22 正式验收与归档证据

- #205 稳定实现、#206 独立 REVIEW、#207 正式 ACCEPTED HEAD **均 12/12 SUCCESS**。真实 P22 WorldSpawnProbe 验证防重复、FakeClock/Timer、LinkedRespawn、动态一次生成、Grid 与 Crash Checkpoint RecoveryHold。
- 原始历史 #202 311/312 Unit FAIL（Tick 错误组合返回类型），#203 修复后 12/12 PASS，#204/#205 增强 Faulted/Stopped/Draining 防部分提交后全绿。
- 唯一实体/坐标真相保持 P17/P24/P20，P22 没有增加 DB、网络或后台世界写入口。Checkpoint 只做受信任内存快照与防副本确认安全闸，跨存储原子落盘和 P62 全链路 StateHash 尚未完成。
- 归档提交自身 CI 待运行；下阶段 P23 环境查询契约/参考 Adapter，而不是网络 Packet。

## P23 恢复入口（2026-11-17）

P22 正式 ACCEPTED 后，P23 B+C 已启动（IN_PROGRESS），三种环境查询 Port + 静态平面真实 Adapter、Unit/Fake 和 EnvironmentProbe 已就绪。预计 13 Job，但尚无真实 CI 通过证据；后续依次执行实现验证、独立 REVIEW、正式 ACCEPTED。P28 Motion/AI 生产调用方未实现，不能冒充生产链路已集成。以 [P23](progress/P23.md) 为准。

## P23 稳定实现与 REVIEW（2026-11-17）

P23 B+C 稳定实现 HEAD `cd9e804` CI #213 **13/13 SUCCESS**（322 Unit、16 Architecture、P23 五真实 Probe、全部旧集成）。状态 REVIEW；独立审查 [P23 REVIEW](reviews/P23_ENVIRONMENT_REVIEW.md) 已单独提交，其 HEAD CI 尚需运行。历史 #209/#210/#211 的失败在 [P23](progress/P23.md) 保留。不得称 P28 Movement/AI 已有调用方、不得混淆逻辑截止 Tick 与硬实时超时。

## P23 正式 ACCEPTED，下一阶段 P25（2026-11-17）

P23 加固实现 #213、独立四视角审查 #214 两轮 13/13 SUCCESS（322 Unit、16 Architecture、真实五场景 EnvironmentProbe）。P23 B+C 无必修技术阻塞，现正式 ACCEPTED；**本状态提交 HEAD 自身的 CI 待核验**。下一优先 P25 — Stats / Resources / Modifier（P24/P07 依赖已满足），而非重做已验收 P26。未来 P28/AI 与真实 NavMesh 另验。

## P23 三阶段 CI 完成归档与恢复点（2026-11-17）

P23 **ACCEPTED（B+C）**：稳定实现 #213、独立 REVIEW #214、正式 ACCEPTED HEAD `82851e7` #215 均为 **13/13 SUCCESS**（322 Unit、16 Architecture；五项真实环境 Probe + 前置全部回归）；#215 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43995827389。三阶段已完成，本归档 HEAD CI 仍需单独核验。下一 P25 属性/资源/Modifier（P24/P07 依赖已满足）；P27 需先有 P25。详见 [P23](progress/P23.md)。

## P25 当前安全入口（2026-11-17）

最新完整绿色基线 P23 归档 HEAD `84b43c1`、CI #216 **13/13 SUCCESS**；P00–P23、P24(B)、P26(C) ACCEPTED。P25 Stats/Resources/Modifier 当前 **IN_PROGRESS（目标 C）**，依赖 P24/P07 已满足。P25 增加 Entities 属性资源账本与 WorldInstance 窄写路由，P19 状态 Hash schema v4，Unit + 真实 AttributesProbe 及第 14 个 CI Job。首次实现 HEAD CI 尚未运行，不能 ACCEPTED。恢复顺序：查看 [P25](progress/P25.md) → CI → 修正 → 独立四视角 REVIEW → 正式 ACCEPTED；始终只在 main 推送。

## P25 实现稳定，当前 REVIEW

P25 稳定实现 `a90505a` CI #220 **14/14 SUCCESS**（334 Unit、16 Architecture、真实 P25 Probe + 原有十三项），#217/#218 失败已修复并保留。当前 **REVIEW**：独立四视角审查 [P25](reviews/P25_ENTITY_ATTRIBUTES_REVIEW.md) 另行提交，审查 HEAD 需单独 14 Job 全绿后才正式 ACCEPTED。P19 digest v4 新旧 Hash 不兼容、未来 P27/Combat/Replay/负载为后续范围。

## P25 正式 ACCEPTED，接续 P27（2026-11-17）

P25 稳定实现 #220、独立 REVIEW HEAD `6d66ae9` #221 **各 14/14 SUCCESS**（334 Unit/16 Architecture、真实 P25 Probe 五场景、既有十三 Job 全 PASS）；四视角 C 级必修阻塞 0，现正式 ACCEPTED。**正式状态提交自身 CI 尚未复验**，不能借 #221 代表它。下一 P27 Movement State/Speed/Input，原 World/P18 租约、P20 Spatial 与 P25/P26/P60.F 均为前置。P19 Hash v4 非向后兼容，P62 必须带构建版本比对。

## P25 最终验收与下一安全动作（2026-11-17）

- P25 稳定实现 `a90505a` #220、独立 REVIEW `6d66ae9` #221、正式 ACCEPTED `59d8d55` #222 **各 14/14 SUCCESS**，334/334 Unit、16/16 Architecture，P25 真实五场景与先前十三项集成全 PASS。
- P19 状态摘要已由 v3 升 v4，加入实体属性/派生/资源/来源序号；快照版本需要按构建区分。没有引入 WoW 专属值、数据库事务或不可信入口。
- 下一按 [P27 路线图](DEVELOPMENT_ROADMAP.md) 设计 MovementState、SpeedModel、含主体与序号的 MovementIntent，真正调用 P18 WorldExecutor 内 P20 Relocate，严格 P60.F 授权与 P25/P26 状态读取。P27 未开始，绝不把 P25 的 Probe 说成移动输入已通过。
- 最终归档 HEAD CI 尚需另行检查；实时可信结果以本段和 [P25 阶段记录](progress/P25.md) 为准。

## P27 当前恢复入口（2026-11-17）

- 上一已验证基线 P25 归档 HEAD `bd10dfe75eda9fb3b1608aba1b277f73faaf32d5` CI #223 **14/14 SUCCESS**。P27 正在 main 直接实现（未创建分支）。
- 新增 `WorldMovement` 独立部分与严格 P60.F `BoundIdentity` 依赖、P25 Speed Stat、P26 控制、P20 唯一 Transform/Spatial 重定位。P19 digest schema v5，旧 schema v4 Hash 不可直接比较；P27 新真实 MovementProbe 报告标记 `P27_MOVEMENT_INTEGRATION_PASS`，记录型 Release baseline。
- 新增第十五个 CI Job `world-movement`。首次提交 HEAD 的 CI 尚未验证，不可正式 ACCEPTED。恢复应先检查 [P27 阶段进度](progress/P27.md) → CI → 失败修复 → 独立四视角 REVIEW HEAD CI → 正式 ACCEPTED HEAD CI → 归档 CI。

## P27 进入 REVIEW

P27 可信主体/会话与有序移动实现 #225 **15/15 SUCCESS**（342 Unit），边界加固 #226 **15/15 SUCCESS**（344 Unit/16 Architecture）并通过 P27 六类真实 Probe 与旧十四 Job。#224 CS8602 Build FAIL 已真实修复；四视角审查 [P27_MOVEMENT_REVIEW](reviews/P27_MOVEMENT_REVIEW.md) 已单独提交候选，审查 HEAD 需要自己取得 15/15 SUCCESS 才能正式 ACCEPTED。P60 真网关/P28 Path/P29 反作弊/P62/P64 后续独立。

## P27 正式 ACCEPTED 决议（待验证自身 HEAD）

稳定实现 #226 与独立四视角审查 #227 分别 **15/15 SUCCESS**（344/344 Unit、16/16 Architecture，P27 六场景与旧十四 Job）。P27 C 目标正式 ACCEPTED，正式验收状态提交 HEAD CI 尚待独立复核。下一安全开发 P28 Server Motion / Path，直接依赖 P23 Terrain/Collision/Navigation 与 P27 Movement 均 ACCEPTED。注意 P60 真实生产网关/P29 Violation/P62/P64 另行验收；严禁通过外部反序列化任意 IWorldAction 绕过可信绑定。

## P27 三阶段全绿与下一阶段 P28（2026-11-17）

- P27 加固稳定实现 `9367a9a` CI #226、独立四视角 REVIEW `d40b21b` CI #227、正式 ACCEPTED `df3e192` CI #228，**各 15/15 SUCCESS**（344 Unit、16 Architecture、P27 真 MovementProbe 五功能场景 + 500 Move 记录型 baseline、此前十四 Job 全绿）。
- P27 所拥有的真实 P20 Canonical Move/Spatial、P25 动态速度、P26 控制/死亡、P60.F `BoundIdentity`、顺序 Tick、v5 同 Tick 来源敏感 Hash 均已正式验收；P60 正式生产 Socket/Host 并未实现，不能外部伪称已开放身份输入。
- 参照 [P28 任务卡](DEVELOPMENT_ROADMAP.md) 下一步开始通用 Point/Random/Chase/Follow/Flee/Home/Waypoint、NavigationPort 请求版本与抢占/取消，不修改 P27 唯一 Transform 真相；真实 Spline/Jump/Knockback 在 P28 分批范围中另列。
- 当前最终归档提交自身尚需单独完整 CI 验证，基线历史结果 #226/#227/#228 已确实全绿。禁止不经 CI 直接宣称 P28 ACCEPTED。

## P28 当前恢复入口

- P27 最终归档 HEAD `7b7c84e582f529ddcfba30f146b8e5e2a955f383` CI #229 15/15 SUCCESS（344 Unit/16 Architecture）；P28 直接依赖 P23/P27 ACCEPTED，始终只在 main 更新。
- 新 P28 `WorldMotion.cs` 和 `WorldMotionTickStep`：十类服务器运动状态机；P23 外部只读导航请求+当前实体代际/运动版本/请求序号/环境版本回执；P18 内版本核验与真正 P20 RelocateCore，P27 玩家会话与服务器运动互斥，控制/死亡/目标失效终止。P19 v6 StateHash 含未来导航状态；无第二份当前位置。
- 新 `tests/Unit/.../WorldMotionTests.cs` 与独立真实 `MotionProbe`，固定 Seed/真实 P23 平面网格避障及 NoPath/Deadline；新增第十六个阻断 `server-motion` CI Job，输出 `P28_SERVER_MOTION_INTEGRATION_PASS`。正式结果必须等本 HEAD 16/16 CI；之后独立四视角 REVIEW HEAD CI → ACCEPTED HEAD CI → 归档 CI。
- P28 的 Spline 当前是已验证导航路径段的平滑插值，Jump/Knockback 为有界固定 Tick 可信服务器轨迹；不是完整 3D NavMesh、动态碰撞、多层地图或 P29 位移反作弊/处罚。
