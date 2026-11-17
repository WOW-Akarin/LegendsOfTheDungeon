# P23 — Terrain / Collision / Navigation Ports 契约

状态：IN_PROGRESS（目标 B+C）；2026-11-17；依赖 P06/P09 ACCEPTED。

## 公共 Port 与边界

- `ITerrainQuery.SampleSurface`：输入 P06 `Position`（右手 XYZ 米、Z 向上）；返回地面高度米、环境数据版本，非覆盖区域返回 `OutsideBounds`。
- `ICollisionQuery.TraceSegment`：米坐标闭线段；仅 `Status=Success` 时 `IsBlocked` 有效，未知区域不能默认放行。
- `INavigationQuery.FindPath`：只读同步有界导航 Port；`NavigationRequest` 传起终点、正数 `RequestVersion`、1–16384 节点预算、`CurrentTick` 和可选绝对 `DeadlineTick`；返回冻结的 `NavigationPathResult`。
- 结果状态：`Unknown`、`Success`、`OutsideBounds`、`Blocked`、`NoPath`、`BudgetExceeded`、`TimedOut`、`Cancelled`。失败路径为空；无路不是“返回直线”。
- `RequestVersion`、`EnvironmentVersion` 由 P28/P18 消费者在回送时核对；Port 不持有同步 World 写租约、实体或 Transform。取消返回 `Cancelled`；截止 Tick 在请求时判断，并**非执行期间硬实时抢占**。节点预算不足为 `BudgetExceeded` 而非超时。
- 无外部持久化、副作用、异步调度。未来真实 Movement/AI 只能依赖 Port，不能引用具体参考 Adapter。

## 真实参考 Adapter

`Runtime.Environment.PlanarGridEnvironment` 使用 P06 Kernel 几何，构造期冻结二维格及障碍，每轴不超过 128 格，CellSize 0.25–64 米，恒定地面 Z，环境版本大于零。负数坐标以 `floor` 定位，静态障碍视为 XY 方格闭合矩形、Z 方向无限高；实际碰撞为线段与方格相交，导航使用固定方向四邻域 BFS（无对角切角），返回起终点及格中心，按值复制路径。无路显式 `NoPath`。

此 Adapter 不具备动态实体障碍、真实 3D NavMesh、斜坡/多层地形、路径平滑，也不绑定 WoW Map/VMap/MMap。Fake 仅作为 Unit 的 Port 测试替身，不伪装为真实环境。

## 验证

- Unit：地面与负边界、障碍线段、绕障路径每段无碰撞、封墙无路、堵点、超界、超时、预算、取消、冻结快照、Fake 返回失败。
- 独立 `EnvironmentProbe`：经三个 Port 调用真实 Adapter，测试绕障、无路、预算/截止/取消、超界拒绝。
- P03 Manifest、Solution、locked graph、新增第 13 个 `environment-query` CI 阻断 Job。代码首次提交尚未获得 CI SUCCESS；验收暂 NOT_RUN。
- 路线图“Movement/AI 只依赖 Port”通过本阶段抽象边界先行保证提供方无反向依赖；P28/P37 真实消费方尚未落地，该调用链不能冒充已验证。
