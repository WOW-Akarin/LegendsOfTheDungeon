# P23 — Terrain / Collision / Navigation Ports 独立四视角审查

- 状态：**ACCEPTED**（目标 B+C）；审查与实现分开提交，直接 main。
- 稳定实现 HEAD：`cd9e804b944a3ae2ea0a5103bcede1e593b91e33`。
- 稳定实现 CI #213：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43995006336，**13/13 SUCCESS**，322/322 Unit、16/16 Architecture，新 P23 EnvironmentProbe 五类真实场景 PASS。
- 直接依赖 P06/P09 已 ACCEPTED；没有 WoW Map/VMap/MMap 兼容义务。审查文档 HEAD 必须独立验证 CI，不能把实现 #213 视作审查 HEAD 的结果。

## 1. 功能语义和确定性

- 三 Port 以 P06 `Position` 米坐标为唯一几何单位：地形返回地面位置、碰撞明确报告线段阻挡、导航返回请求版本 + 环境版本 + 只读路径。
- `PlanarGridEnvironment` 是有限平面静态障碍的**真实运行参考 Adapter**，不是 Fake；BFS 四邻域、稳定方向顺序、不可对角切角、障碍线段投影与闭矩形交叉，不能将 `NoPath` 降级为直线成功。
- 环境数据构造时复制障碍格且只读，返回路径点作防御性复制；`NavigationPathResult` 已加固为成功至少两点、请求版本/环境版本大于零、失败路径必须为空。没有在 Port 层偷偷持有世界实体或变更 Transform。
- Unit 覆盖负坐标 floor、地图边界、线段阻挡与绕行每段检测、完全封堵无路、阻挡端点、地图外拒绝、重复路径顺序、结果冻结和 Fake Port 故意返回失败。真实 Probe 经 Port 绑定实际运行五组场景。

## 2. 架构与所有权

- `Runtime.Abstractions` 是提供方，无实现依赖；新 `Runtime.Environment` 仅依赖 Kernel + Runtime.Abstractions，独立技术 Adapter，工程纳入 Solution、P03 Manifest 生产引用白名单、锁文件，P03 Architecture 16/16 PASS。
- `Runtime.World`、Entities、P18 WorldExecutor、P20 Spatial 与 P24 Transform 未修改；Environment 只执行纯只读环境查询，不签发写租约、不接管状态真相，也不创建万能 Manager。
- `Movement/AI 只依赖 Port`：目前 P28/P37 真实消费方尚未开发，该验收项对不存在的消费调用方 **N/A（非 PASS）**。P28/P37 必须复核源码引用只指向 Port，且异步路径回执经 P18 当前身份/版本验证后才能重定位。
- 与 WoW 地图格式和 AC 游戏特化无绑定；只有普通有限二维格参考数据。将来实际地形文件 Adapter 是替换项，不允许改变 Port 语义。

## 3. 失败、恢复与资源边界

- 失败路径与原因明确：`OutsideBounds`、`Blocked`、`NoPath`、`BudgetExceeded`、`TimedOut`、`Cancelled`；未知状态不能当成功。
- DeadlineTick 只在请求开始时对逻辑 Tick 作判断，**不是执行中的墙钟硬超时/自动抢占**。可取消 BFS 每次取节点复验 CancellationToken；MaxExpandedCells 1–16384，平面每轴最多 128 格，资源有限、结果重放确定；更大真实地图性能是 P64 专题。
- 没有外部 IO/持久化事务/异步回执提交，故 DB 重试、网络安全或跨世界恢复 **N/A**；P28 采用后台导航时必须结合 `RequestVersion`、`EnvironmentVersion`、P18 World/Epoch/Generation 去重与旧票据拒绝。
- 现有闭合方格障碍在 XY 投影上高度无限、接触边界即视为阻挡；不是真实 3D 体积、斜坡、多楼层、NavMesh 动态寻路的证明。若未来使用真实地形，须新增相应适配器和环境对照。

## 4. 证据和审查门禁

| 必需审查项 | 实现 CI #213 |
|---|---|
| SDK 10.0.404、Release build、locked restore | PASS |
| Unit | 322/322 PASS |
| Architecture 依赖及生产工程白名单 | 16/16 PASS |
| 文档、XML、负向 build / unit / architecture / Strong ID Gate | PASS |
| P23 Unit 的 Fake、绕障、封墙、边界、取消、版本/冻结 | PASS |
| `environment-query` 真实五类 Probe | PASS，P23_ENVIRONMENT_INTEGRATION_PASS |
| P11–P22 全部既有集成 Job 回归 | PASS |
| 完整实现 CI | 13/13 SUCCESS，#213 |
| 独立审查 HEAD CI | NOT_RUN，提交后独立核验 |
| 正式 ACCEPTED HEAD CI | NOT_RUN，待审查通过后另行提交 |

失败记录：#209 CA1716 接口参数跨语言关键字；#210 CA1512 构造范围检查；#211 CA1859 测试局部具体类型分析；#212 修复后 13/13 全绿；#213 为成功路径构造不变量加固后再次 13/13 全绿。失败没有被豁免或删除测试。

## 当前结论

P23 定义的三 Port、Fake、真实平面障碍 Adapter 和无路/预算/逻辑超时/取消结果均有代码与运行证据，**目前四视角未发现 P23 B+C 范围内未关闭的技术阻塞**。不把 N/A 的未来 Movement/AI 调用方、真实 3D NavMesh、硬实时抢占或 P64 性能冒充完成。审查 HEAD 必须再次 13 Job 全绿才能形成正式 REVIEW 通过证据；之后另起正式 ACCEPTED 状态提交并单独验证。

## 独立审查结束与正式状态决定

- REVIEW HEAD `dc568e4fe317810a544e4bf8895089e2e84e4746`，CI #214（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43995437055）**13/13 SUCCESS**，322/322 Unit、16/16 Architecture、P23 探针和所有原有 CI 全绿。
- 四个审查视角必修阻塞 0；P23 B+C 仅限 Port 与真实二维参考 Adapter、Fake 及失败边界，正式 **ACCEPTED**。正式状态 HEAD CI 尚待单独复核；保留未来 Motion/AI、真实 3D NavMesh 与 P64 性能独立验收。
