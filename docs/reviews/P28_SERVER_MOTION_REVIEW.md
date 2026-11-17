# P28 — Server Motion / Path 独立四视角 REVIEW

- 阶段：**REVIEW**，目标 C，直接依赖 P23 B+C、P27 C 均 ACCEPTED；严格 main 直推，不开分支。
- 稳定代码 HEAD `19ccdf2f97311234c2e158c4f0d97729c8bba6fe`，CI #235（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44016152259）**16/16 SUCCESS**：367/367 Unit、16/16 Architecture、真实 P28 MotionProbe 六大类场景（含十种模式）、全部既有十五 Job PASS。
- 上一个功能完整 Head `9b0dbd5bd53f5bc640f8247d5061ed7c7363546a` CI #234（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44015689359）**16/16 SUCCESS**（363 Unit/16 Architecture）；稳定 HEAD 额外修复 Spline 实际超速风险和 P23 失败回执契约。
- 本审查文件以**独立提交**记录，REVIEW HEAD 自己尚须完成第十六项 CI；不可把 #235 当作 REVIEW HEAD 已验证。

## 视角一：策略功能、状态与可复现性

- `WorldMotionCommand` 按值明确 Kind、Priority、目标完整身份、固定随机 Seed、冻结 Waypoint/Spline 1–32 个航点、可信 Jump/Knockback 持续 1–20 Tick，非法配置在进入 World 之前拒绝。P28 十种模式均使用真实 P17 WorldInstance 与 P18 WorldExecutor 验证：Point、Random、Chase、Follow、Flee、Home、Waypoint、Spline、Jump、Knockback。
- Random 目标由输入 Seed 和 EntityId 的固定运算确定；Home 固定为 Spawn 时的语义出生点，不是当前 Position 的第二份真相；Chase/Follow/Flee 只读取当前活动目标代际与 Transform，并在目标移动超过 0.5m 后生成新请求版本；Follow 1.5m 范围内可完成。路径点与请求冻结，旧目标不能共用新实体代际。
- P23 `INavigationQuery` 实际在 P18 世界 Scope 外计算，使用返回的冻结 `NavigationPathResult` 及 RequestVersion/EnvironmentVersion；世界提交时检查 EntityIdentity(World/Epoch/Id/Generation)、MotionVersion、RequestVersion、环境版本、起点、终点、节点上限及 P20 空间可表示性，失败不会把 Path 当作直线。
- P19 `WorldMotionTickStep` 固定 Simulation Phase、按 EntityId 升序和逻辑 Tick 推进；同一个 Seed/路径/优先级生成一致 v6 SHA256，优先级或未来路径历史不同，Digest 不可碰巧只凭当前位置当作同一 Replay。v5/v6 StateHash 明确不兼容。
- Spline 在已由 P23 确认的路径段内使用 smoothstep 时间形状；固定二分反求参数将每 Tick 实际位移限制在 P25 EffectiveSpeed × TickDuration，2m/s ×50ms、连续16 Tick 的 **≤0.10000001m** 定量回归经过 #235 CI 全绿。Jump/Knockback 只由可信世界服务器发起，线性/抛物线 1–20 Tick、目标 30m 内，沿用 P20 RelocateCore；不是可信客户端指令或免碰撞凭证。

## 视角二：模块职责与信任边界

- 只扩展 `Runtime.World` 生产程序集、新 `WorldMotion.cs`，不引入 `Runtime.Environment` 生产依赖、不添加新的全能 Entity/World 容器；唯一可变 Transform 由 P24 EntityCapabilities 持有，P20 Spatial 只为派生索引。Motion Ledger 仅保存未来计划、请求版本、固定 Home 语义锚点、目标位置和段内进度。
- 全部写操作由 P18 真正同步租约 `IWorldWriteContext` 保护；异步 Worker 只能做 P23 只读 Port 查询并将值回送可信 Producer，不能携租约跨 await。真正 P60 生产 Socket/认证端口没有凭空实现；不允许用户把任意 `IWorldAction` 直接投入可信通道。
- P27 玩家输入会话与服务器 Motion 单一控制权：`StartMotion` 成功废止 P27 旧绑定；`BindMovementSubject` 成功取消服务器 Motion；普通可信 `Relocate`/手工传送同时使两者失效。P25 Modifier 决定实际速度，P26 Rooted/Stunned/Feared 导致暂停，死亡和目标退休安全终止。
- P03 架构 16/16，现有编译锁图继续 `--locked-mode`。P28 `MotionProbe` 才单独引用 P23 `PlanarGridEnvironment` 真实适配器，Production Runtime.World 只依赖既有 `Runtime.Abstractions` 公开导航 Port。

## 视角三：失败、取消和恢复

- 下级 Priority 拒绝不改变旧运行态，同级可替换且提升 MotionVersion，高级可抢占；`CancelMotion(expectedVersion)` 只有精确活动版本有效，同时令所有旧导航请求与后续 Tick 失效。
- `ApplyMotionPath` 检查待处理版本、源实体代际、环境版本、当前 Transform 与申请起点、冻结 Waypoints 首尾/数量/可表示性。旧请求一律 Stale；伪造/非法路径 InvalidPath，不消费正确待处理请求，可在同一请求版本上接纳正确结果。不存在穿墙“直线失败后回退成功”。
- P23 NoPath/Blocked/OutsideBounds 明确终结 NoPath；BudgetExceeded、Cancelled 终结 Blocked 且保留原始 NavigationStatus；TimedOut 与逻辑 Deadline 明确 TimedOut。目标实体死亡/Remove，已等待和已接受的 Chase/Follow/Flee 都变为 TargetLost，过期 Generation 不能重用。时间上限只读逻辑 Tick，不使用壁钟超时推导。
- 逐 Tick 仅沿当前已确认路径段执行一次 P20 RelocateCore；目标 Cell Unloaded/越界时明确 Blocked，当前 Transform/Spatial 不因拒绝而改变；异常沿 P20 WorldFaulted 语义失败封闭，不能谎称失败自动回滚多条操作。
- 主体/路径优先级规则和配额已测，但动态地图更新、跨层 3D NavMesh、Jump/Knockback 与障碍体积的物理接触、P29 检测/处罚不是此 P28 目标 C 验收中完成的组件。生产使用需由 P60/后续 Motion adapters 建立可信来源和地图版本更新流程。

## 视角四：真实 CI、反例与退出矩阵

| 必修项 | 真实 CI 证据 |
|---|---|
| .NET SDK 10.0.404、locked restore、Release Build、分析器警告即错误 | #235 PASS |
| 全部 Unit（含各运动状态机及速度/超时/非法路径） | **367/367 PASS** |
| P03 Architecture/P02–P05 质量/负向 Gate | **16/16 Architecture PASS**、门禁成功 |
| 10 类 Server Motion 与真实 P18 内位移 | MotionProbe 10 MODE PASS |
| P23 真避障、封墙 NoPath、BudgetExceeded/Cancelled、起终点/环境版本拒绝 | Unit + MotionProbe PASS |
| 取消/抢占/旧请求、目标移动重规划与销毁、超时 | Unit + MotionProbe PASS |
| P20 Spatial 唯一真相、P25 速度 Modifier、P26 控制、P27 输入会话隔离 | Unit + MotionProbe PASS |
| P19 v6 同 Tick Replay 与有序 Tick Step | Unit + MotionProbe PASS |
| 既有 15 Job 集成不回归 | #235 **16/16 SUCCESS** |
| 稳定实现 Head CI | #235 **16/16 SUCCESS** |
| 独立 REVIEW Head CI | **NOT_RUN**（本次提交后核验） |
| 正式 ACCEPTED Head CI | **NOT_RUN** |

## 历史真实反例（未减弱门禁）

- #230 实现 Head `3ec961b`：生产 CS0136 同名变量、CA1512 参数异常辅助方法错误，build-test FAIL；固定语义后重验。
- #231 `cbb2f22`：Probe nullable CS8629 和 Unit CA1859，build-test FAIL；改用显式可空取值和具体类型工厂，不 suppress 分析器。
- #232 `136fee2`：Unit 362/363 PASS，真实 P23 起点→格中心为合法零长度首段，测试错误要求第一 Tick 必移；改为首 Tick 仅更新段号、第二 Tick 验证空间实际移动。
- #233 `a3ef8be`：363 Unit/16 Architecture PASS，Probe Chase 与目标同位置时错误断言 Spatial 总计数为1；修为指定 EntityIdentity 恰好一次，不改变空间一致性目标。
- #234 `9b0dbd5`：**16/16 SUCCESS**，363 Unit；随后自主检查发现 smoothstep 中点可能越过 P25 实际位移预算，主动修复并补三类真实负向案例，形成 #235 稳定 Head。
- #235 `19ccdf2`：**16/16 SUCCESS**，367 Unit /16 Architecture，所有旧十五 Job 回归与十类新 Motion 正常。

## 独立 REVIEW 结论（自身 CI 尚待验证）

按 P28 C 已指定的十类策略状态机、P23 PathPort 真消费/版本核验、抢占/取消/超时/实体退休、P20 唯一 Transform/Spatial 和 P19 固定 Tick 与拒绝优先约束，稳定实现 #235 范围内**尚无必修阻塞**。审查承认二维平面适配器不等于真实生产动态 3D Navigation、P29 反作弊、P60 完整网络安全或 P64 性能，留待各阶段专项验收。REVIEW HEAD 必须单独 **16/16 SUCCESS** 才能进入正式 ACCEPTED。

## 独立 REVIEW HEAD 与正式 ACCEPTED 决议

- 独立 REVIEW HEAD `37cc14cf389b81e6246234a459a89cced10494c5`，CI #236（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44016560072）**16/16 SUCCESS**，367/367 Unit、16/16 Architecture、十模式 P28 六类真 Probe + 历史十五 Job 全 PASS。
- 实现 HEAD #235 与 REVIEW HEAD #236 均独立全绿，确认四视角 P28 目标 C 范围必修阻塞为零，正式 **ACCEPTED**。状态决议 HEAD CI 必须另外执行，不能将 #236 作为它的证明。

## 正式 ACCEPTED HEAD 自身 CI

- P28 正式 ACCEPTED 决议 HEAD `dc76c99a7369a24680953f34ca216553fb12f7a2`，GitHub Actions #237（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44016970168）**16/16 SUCCESS**，367 Unit /16 Architecture、十类 MotionProbe 和全部旧十五项 CI。
- 实现 #235、独立 REVIEW #236、正式 ACCEPTED #237 各拥有独立全绿 GitHub CI，四视角必修阻塞 0，P28 C 完成三阶段正式验收。最终归档 HEAD CI 另行复核；P29/P60/P62/P64 不属于本次结论。
