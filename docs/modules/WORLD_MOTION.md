# P28 — Server Motion / Path 技术契约

状态：**REVIEW**（目标 C），直接依赖 P23 Terrain/Collision/Navigation B+C 与 P27 Movement C 均 ACCEPTED；首次实现 CI 尚未执行。

## 角色、唯一当前位置及状态机

- `Runtime.World.WorldMotion.cs` 的 `WorldMotionCommand` 是不可变的有界服务器策略：Point、Random、Chase、Follow、Flee、Home、Waypoint、Spline、Jump、Knockback。显式 `Priority`（0–100），同等级后命令可替换、更低等级拒绝；新命令提升 MotionVersion，旧异步导航回执不能再写入。航点数组 1–32 被防御性复制；Random 使用命令 Seed+EntityId 的确定性格点偏移，不依赖进程全局 Random。
- 每个 P17 Entry 独有 `WorldMotionLedger`，保存运动模式、优先级、目标完整 `EntityIdentity`、目标锚点/目标点、冻结航点、已接受导航点与段内进度、出生时静态 Home、绝对截止 Tick、请求/环境版本以及 Jump/Knockback 的可信轨迹起点。**不复制或拥有当前 Position**；当前位置唯一由 P24 `EntityCapabilities.Transform` 持有。P20 Spatial 仍仅是派生索引。
- `WorldInstance.StartMotion`、`ApplyMotionPath`、`CancelMotion`、`AdvanceMotion`、`AdvanceAllMotions` 以及 `WorldMotionTickStep` 必须通过真正 P18 WorldExecutor 当前同步租约。新 `WorldEntitySnapshot.Motion` 是只读值快照，`PathDigest/CommandDigest` 冻结未来路径与指令差异；P19 SHA256 状态 schema 从 v5 升至 **v6**，旧摘要不兼容。
- Point 向给定位置；Random 由固定 Seed 确定目标；Home 去注册时出生静态锚点；Chase/Follow/Flee 验证目标 World/Epoch/Id/Generation 且 Alive+Active，目标移动超过 0.5m 会签发更新版本的路径请求，Follower 进入 1.5m 范围可完成；目标死亡、销毁或退休立即使相关待处理运动变成 TargetLost。
- Waypoint/Spline 逐腿规划并向 P23 端口请求路径。Spline 在已验证的连续路径段上以 `3t²-2t³` 进行确定性平滑插值，仍受 P25 速度预算，**不是完整物理三次曲线或避障 NavMesh**。每 Tick 最多推进当前路径段（避免跳越待验证路径的拐角），不通过 AI 直接写 Position。
- Jump/Knockback 是可信服务器显式请求的 1–20 Tick 有界轨迹，出发点到目的地最长 30m；Jump 使用固定 1.5m 高度的抛物线、Knockback 线性逐 Tick 运动，最终仍逐 Tick 通过 P20 Relocate；它们不是客户端任意声称的例外，碰撞/落脚点验证及 P29 反作弊分类仍须未来明确实现。空间越界/卸载拒绝，不能伪称目标可达。

## P23 NavigationPort 异步隔离和拒绝先行

1. `StartMotion` 在真实 P18 内生成 `WorldMotionPathRequest`（EntityIdentity、MotionVersion、P23 `NavigationRequest` 的 RequestVersion、EnvironmentVersion 与目标身份），**只读 `INavigationQuery.FindPath` 应在 P18 同步租约以外完成**，避免在世界 Actor 内执行重寻路。
2. 外部只读 Worker 返回 `NavigationPathResult` 后，可信世界 Producer 在 P18 当前 Scope 调用 `ApplyMotionPath`；精确比对完整实体 Generation、命令版本、请求版本、环境快照版本和起始当前 Transform。结果过期返回 Stale，不会变更任何 Position/路径。初始/中途计划均不把 P23 对未知地图的 OutsideBounds 当作直线许可。
3. P23 非 Success 必须保留 `NoPath/TimedOut/Blocked/BudgetExceeded/Cancelled` 的显式状态；成功路径至少起终两点，路径点数 ≤4096，起终位置必须与最新请求一致，且每个点可表示于 P20 空间网格。生产异步回执入口仍须受 P18 可信组合根和 P60 授权约束；这只是可核验的路径票据，不是客户端可反序列化的认证票据。
4. `CancelMotion(expectedVersion)`、更高优先级抢占、手工 `WorldInstance.Relocate`、P27 重新绑定的输入会话、死亡/目标注销都会使旧路径失效；旧回执不得复活 Motion。世界为服务器 Motion 开始运动时会废止旧的 P27 客户端输入绑定，避免双写。
5. P19 `WorldMotionTickStep` 在固定 Simulation Phase 对实体 ID 顺序推进，每 Tick 最多当前路径段的 `movement.speed EffectiveValue × tickDuration`；Rooted/Stunned/Feared 暂停位置变化而不吞掉路径。截止 Tick 来自逻辑 Tick 而非机器墙钟。

## 状态与退出证据

- Unit 需要真正 P18 执行全部十类模式；P23 实际封墙 NoPath 和绕障；目标移动重规划/死亡、取消/优先级抢占、旧路/错环境版本/超时/过期 Scope/旧 Generation、P25 Modifier/零速度与 P26 控制、P27 会话交接、P19 v6 同 Tick Hash。
- 真实 `tests/Integration/LegendsOfTheDungeon.MotionProbe` 的六组集成，输出 `P28_SERVER_MOTION_INTEGRATION_PASS`，引用 P23 平面障碍 Adapter 仅用于测试，不将 Runtime.Environment 引入生产 Runtime.World；新增第 16 个 CI 阻断 Job `server-motion`，保留旧十五 Job。
- 完成目标 C 的前提是稳定实现 HEAD、独立四视角 REVIEW HEAD、正式 ACCEPTED HEAD 各自 **16/16 SUCCESS**。当前首轮尚未运行，P28 **NOT_ACCEPTED**。
- P29 更严格速度/障碍/异常跳变与处罚、真实 3D NavMesh、多层高度、动态障碍与 P62/P64 长稳性能、P60 生产 Socket 认证均不在此处宣称完成。

## Spline 速度预算安全补强（待 CI）

- smoothstep 对参数的导数中点最高为 1.5，不能简单用 `speed×Δtick` 除以段长更新 `t`，否则中点实际位移可超速。P28 补强先计算候选平滑位移比例，再以 `旧平滑比例 + speed×Δtick/段长` 为硬上限，使用固定 40 次二分反解参数，确保真正 Transform 位移符合 P25 速度预算且 Replay 确定性。
- 真实 P23 BudgetExceeded、Cancelled 结果终结为 Blocked（保留原状态）、非法路径仍保留待处理票据，严禁穿越未知/非法点；补强实现及测试需要新的完整十六 Job CI 通过，当前 NOT_RUN。

## 稳定实现与独立审查

- 首次全绿 #234 16/16 SUCCESS（363 Unit），Spline 实际位移上限和 P23 BudgetExceeded/Cancelled/恶意路径加固后的实现 HEAD `19ccdf2f97311234c2e158c4f0d97729c8bba6fe` CI #235 **16/16 SUCCESS**（367 Unit /16 Architecture）。
- [P28 四视角 REVIEW](../reviews/P28_SERVER_MOTION_REVIEW.md) 单独提交，REVIEW HEAD 和正式 ACCEPTED HEAD 仍须各自完整 16 Job 验证。
