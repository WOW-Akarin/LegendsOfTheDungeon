# LegendsOfTheDungeon 核心通用阶段式开发与验收路线图

> 版本：1.1（优化版）｜修订日期：2026-11-16  
> 技术基线：C# / .NET 10；模块化单体；World Actor；Ports & Adapters  
> 配套规范：[项目规范](PROJECT_SPEC.md)、[架构设计](ARCHITECTURE.md)  
> 说明：保留 P00–P67 稳定编号；编号不代表执行顺序。本文是交付计划，未声明任何阶段已经完成。

## 1. 使用方式与阶段状态

先核查任务的真实依赖，再实施。阶段状态、证据和当前停点统一维护在 `docs/PROGRESS.md` 与 `docs/progress/Pxx.md`，不在本文维护另一套完成状态。

状态：NOT_STARTED → IN_PROGRESS → REVIEW → ACCEPTED；阻塞、退回、延期和重新打开规则见 PROJECT_SPEC 第 8 节。DEFERRED 必须有范围依据、影响和恢复计划，不等于 ACCEPTED。必交付范围未完成时不能通过最终验收。

完成度：A=结构，B=模块功能，C=真实依赖集成，D=适用的 AC 参考验证。文档阶段以可执行检查/审阅证据验收，不为凑等级创造空代码。B 不替代 C，D 不替代安全/性能/运维。各阶段目标见第 6 节。

阶段内子任务可以先完成并形成独立证据；父阶段只有所有必需子任务达标后才能 ACCEPTED。真实依赖支持的功能未完成前返回明确的不支持，不用模拟结果冒充集成。

## 2. 全局出口与证据包

所有阶段复用本节，不在 68 个阶段重复相同勾选项：

1. 任务范围与依赖满足；目标完成度达标。
2. 适用的 restore/build/test、架构、边界和失败路径检查通过；文档阶段按适用性执行。
3. 状态所有者、合法执行上下文、成功确认点、重复/取消语义明确。
4. 新增契约及真实调用方同步；没有跨模块 Internal/Infrastructure 访问或依赖环。
5. 中文文档注释、来源/差异记录更新；不引入 WoW 专用依赖。
6. 证据记录环境、代码基线、命令、报告、数据/Seed、结果和已知限制。PASS/FAIL/N/A/NOT_RUN 区分清楚。
7. 自审分别检查功能、架构、失败恢复和证据；未解决的必需项清零后进入 ACCEPTED。
8. PROGRESS 与 CONTEXT 更新，并能按索引恢复真实工作位置。

禁止用删除/Skip/无依据弱化测试、固定成功结果或“以后重构”绕过 Gate。只进行源码推导时不得记作 AC 实际运行验证；测试替身不算真实数据库验收。

每阶段归档 `docs/progress/Pxx.md`：目标、依赖快照、目标/实际完成度、交付物、验证矩阵、命令及报告、差异/风险、自审结论、恢复步骤。下文“出口证据”在这些公共要求之外定义本阶段特有要求。

## 3. 执行批次与早期可运行闭环

| 批次 | 工作包（不是单纯按编号排序） | 可演示出口 |
|---|---|---|
| M0 基线 | P00–P04 | 范围、SDK/数据库决策、工程和检查入口 |
| M1 基础 | P05–P12、P14；P13 先本地传输 | 值类型、可控时间随机、有界队列、真实 DB 事务探针 |
| M2 最小世界 | P15.J、P16.F、P18、P24、P17、P19、P20；P62.F/P64.F/P65.F | 无客户端创建世界、生成实体、移动、回放并比较状态 |
| M3 世界补齐 | P15.S、P16.R、P21–P23、P25–P29 | 可见性、出生/重生、环境查询和移动校验 |
| M4 战斗闭环 | P30–P34 | 自动攻击→伤害→一次死亡→清理，可确定性回归 |
| M5 能力闭环 | P35–P42；P38.I 待 P44 | 施放/消耗→效果→持续状态→取消/死亡清理 |
| M6 经济闭环 | P43–P47、P38.I | 掉落→领取→持久确认→重启，不重复不丢失 |
| M7 目标闭环 | P48–P49、P50.F、P51 | 事实推进目标→奖励→重复/崩溃恢复；交互目标待 P58.F 后补齐 |
| M8 扩展交互 | P52–P59、P56.S；按需 P58.T | AI、脚本、在线路由、小组、场景；未启用转移时明确拒绝 |
| M9 发布验收 | P60–P67 收口，前期证据持续累计 | 完整安全、回放、参考、性能、长稳、部署恢复 |

最小世界闭环先不依赖完整 AI/Ability/Inventory。按已实现能力逐步扩展场景，不到 P62 才开始验证可回放性。

### 3.1 必须提前拆出的子任务

| 子任务 | 何时完成 | 内容与后续收口 |
|---|---|---|
| P15.J / P15.S | M2 / M3 | JSON 最小加载 / SQL 来源；父阶段仍要求两种真实来源 |
| P16.F / P16.R | M2 / M3 | Validate+Freeze / Reload 原子切换与实例版本策略 |
| P60.F | P09/P10 后，任何真实世界网络入口前 | 认证/身份绑定 Port、默认拒绝、命令授权、输入限制；P60 补齐策略 |
| P62.F | P19 后、M2 出口前 | 最小 SimulationHost、接纳输入记录和确定性状态比较；P62 扩展完整范围 |
| P64.F | P14 后，首次模块集成时使用 | 基准清单、环境和负载冻结；每个高频模块增加数据点，P64 汇总 |
| P65.F | P11/P12 后，首次持久写之前 | 可控 DB/队列/回执故障点；各事务阶段直接复用，P65 长稳收口 |
| P38.R / P38.I | M5 / P44 后 | ResourceCost+Cooldown / ItemCost 与真实 Inventory 集成；不能以 Port 已存在为完成 |
| P50.F / P50.I | M7 / P58.F 后 | 已有事件的目标 Handler / Interact 目标 Handler；父阶段最终包含两部分 |
| P56.F / P56.S | 已有能力可用时 / P59 后 | 已存在能力的脚本 API / IScenarioApi 和 Scenario Hook 真集成，避免先定义无调用方接口 |
| P58.F / P58.T | M8 基础路由 / 有明确转移需求时 | 基础交互与路由 / 条件扩展：跨世界转移、fencing 和恢复，禁止双 Owner |

这些子任务必须单独记录依赖和证据。P60/P62/P64/P65 的基础部分前置不代表父阶段提前完成，也不意味着其他阶段需要等待其全部功能。

## 4. 性能、可靠性与运行配置

P00 冻结初始部署环境与验收配置；P14/P64.F 建立可执行测量。以下为启动设计的建议值，不是已有测试结果或无条件性能保证。落实时写入版本化配置并记录采用值、依据；未冻结配置前不能宣称性能通过。

| 项目 | 初始建议与验收方式 |
|---|---|
| 逻辑步长 | 50ms 候选；记录 WorldTick p50/p95/p99、超时比例及追赶行为 |
| 负载档位 | 小：1 World/1,000 实体；中：10 World/共 10,000 实体；大：50 World/共 50,000 实体作为容量探索。各档必须另定活跃比例、AI/Status/命令速率，不能只报实体总数 |
| 发布档位 | P00 选择目标档位与硬件/容器资源限制；P64 前固定成功阈值，不在结果出来后降低阈值 |
| 回归门限 | 初始建议相对已确认基线退化超过 10% 需重复确认和分析；绝对 Tick/队列预算仍需满足 |
| 经济状态 RPO | 已向调用方确认 Committed 的权益变更为 0 丢失；用每个提交边界的崩溃注入证明 |
| 非关键状态/RTO | 初始建议检查点窗口 ≤30s、目标配置恢复 ≤5min；P66 在真实备份体量上实测 |
| 稳定性 | 日常短跑 30min；夜间候选 2h；最终发布配置连续 24h，并检查排空后的内存、句柄、订阅、Timer 和 Pending 事务 |
| Mailbox/任务队列 | 容量、每主体配额、最大延迟、拒绝/重试策略全部配置化，验证满载时不会无限增长 |

基准报告包含 CPU/内存限制、OS、SDK/GC、Release 配置、数据版本/Seed、预热/重复次数、分配/GC、队列峰值、错误率和原始数据。共享 CI 不稳定时专用环境复核；不能只用一次耗时断言判断通过。

## 5. 跨阶段集成与回归要求

- P17/P18/P24 一起形成实体真实世界集成；P24 的纯能力测试不冒充 World 集成。
- P30 先验证关系与攻击调度，P32 后才验收实际 Health 变化，P34 验收一次死亡。
- P34 清理注册表由后续 Abilities、Status、AI、Scenario 持续补接；新增模块必须回归死亡/移除场景。
- P38 的 ResourceCost 可先完成；ItemCost 在 P44 后完成 P38.I。P39 的 Spawn/Move 效果依赖相应真实运行时。
- P49 不要求尚未实现的所有事件类型；P50 在 Interact 事件可用后补齐对应 Handler，最终该阶段不得只剩接口。
- P56 首先提供已存在能力的 API；P59 后补齐 P56.S。场景核心不反向依赖 Scripting.Core。
- P13 的 Loopback 可以早测；真实外部世界写必须接入 P60.F；管理入口同样不能绕过。
- 模块首次引入持久状态即补齐 Snapshot/SchemaVersion/恢复测试，不能集中等到 P66。

## 6. 详细阶段

以下每阶段同时满足第 2 节公共出口。依赖列是直接关键依赖；“对应子任务”表示只需要该阶段已经形成证据的基础部分。

### P00 — 范围冻结与 AC 研究基线

**直接依赖：** 无。  
**目标完成度：** 文档基线。

冻结“只做通用核心”的范围，固定 AC Commit，创建 UPSTREAM、ADR 与范围基线。

**交付任务**

1. 记录固定 AC 仓库与 Commit。
2. 列出明确排除项：WoW 客户端协议、DBC、具体副本/Boss、LFG/PvP/Warden。
3. 定义 AC Reference 模板，要求写“通用机制”和“WoW 特定部分”。
4. 建立范围变更的需求依据、ADR、迁移与基线同步规则。

- 记录 SDK 固定策略、首个真实数据库选择、部署资源和性能配置决策。
- 记录来源许可核查及复用方式。

**出口证据**

- [ ] 任意任务都能判断属于核心还是 WoW 特定扩展。
- [ ] 文档不存在“必须兼容 3.3.5a 客户端”的目标。
- [ ] 仓库 URL 与完整 AC SHA 可追溯。
- [ ] 未落实决定列为阻塞，不填造版本。
- [ ] 范围与三份基线一致。

### P01 — 开发规范、上下文与断线恢复制度

**直接依赖：** P00。  
**目标完成度：** 文档基线。

落地 PROJECT_SPEC、CONTEXT、PROGRESS、阶段状态和重启恢复规则。

**交付任务**

1. 创建/更新 `docs/PROJECT_SPEC.md`。
2. 定义 `docs/CONTEXT.md` 压缩模板。
3. 定义 `docs/PROGRESS.md` 详细进度模板。
4. 定义 `docs/progress/` 阶段归档。
5. 规定何时必须更新进度、压缩上下文和自优化规范。

- 统一摘要/进度/阶段归档模板和状态迁移。
- 明确恢复索引与代码基线核验。

**出口证据**

- [ ] 恢复索引可定位当前目标、已完成、下一步、阻塞和关键约束。
- [ ] 进度状态只允许 NOT_STARTED/IN_PROGRESS/BLOCKED/REVIEW/ACCEPTED/DEFERRED。
- [ ] 按恢复步骤可定位文件、失败测试、工作区差异及下一成功条件。
- [ ] 检查围栏和相对链接。

### P02 — Solution 与 CI 骨架

**直接依赖：** P01。  
**目标完成度：** B（工程）。

建立 .NET 10 Solution、统一工程设置和 CI。

**交付任务**

1. 创建 Solution、src/tests/docs 目录。
2. 固定 SDK、Nullable、Analyzer、Package 版本。
3. CI 执行 restore/build/test/architecture-test。
4. 构建结果和测试报告可归档。

- 创建 Runtime.Abstractions 等必要基础契约。
- 锁定 SDK/包策略并记录测试调用方式。

**出口证据**

- [ ] 干净环境 restore/build/test 通过。
- [ ] 人为制造编译/测试失败时 CI 必须失败。
- [ ] 干净环境可复现。
- [ ] 故意失败构建和测试会导致流水线失败。
- [ ] 不创建没有调用方的大量空项目。

### P03 — Architecture Tests 第一批

**直接依赖：** P02。  
**目标完成度：** B（架构规则）。

让模块边界由 CI 强制。

**交付任务**

1. 实现 Domain !→ Platform Adapter/Host。
2. 实现 Module A !→ Module B.Infrastructure/Internal。
3. 实现 Modules !→ Hosts。
4. 实现禁止循环 Assembly Reference。
5. 对 Common/Utils 和中心 Manager 式职责聚集建立依赖检查与职责审查。

- 实现真实依赖白名单、契约泄漏和 InternalsVisibleTo 检查。
- 结构性职责问题加入人工审查。

**出口证据**

- [ ] 每条架构规则有正向和故意违规的负向测试。
- [ ] Architecture Test 失败阻止合并。
- [ ] 规则既有正例也有故意违规夹具。
- [ ] 避免仅靠类名/关键词误报。
- [ ] 生产引用图无环。

### P04 — 中文注释与文档质量 Gate

**直接依赖：** P03。  
**目标完成度：** 文档基线+B（检查器）。

把完整详细中文注释和中文文档变成验收项。

**交付任务**

1. 规定 Public 类型/方法 XML 中文注释。
2. 复杂状态机、并发、算法、事务必须写中文设计注释。
3. AC 参考实现需在注释/文档记录 Commit/File/Function。
4. 建立 Review Checklist 检查无意义或缺失注释。

- 明确生成/第三方代码例外。
- 公共 XML 缺失自动检查，中文语义与复杂逻辑人工审查。

**出口证据**

- [ ] 抽查核心代码可仅靠注释理解职责、前置条件、线程上下文和失败语义。
- [ ] 新增说明文档全部为中文。
- [ ] 注释缺失样本能被发现。
- [ ] 有中文但语义错误的注释不能通过自审。
- [ ] 不要求每行重复代码含义。

### P05 — Kernel Strong IDs 与 Result

**直接依赖：** P03。  
**目标完成度：** B。

实现 Strong IDs、Result/Error、基础值对象。

**交付任务**

1. 实现 EntityId/ActorId/WorldId/AbilityId/ItemId/TaskId。
2. 实现 Result/Error/ValidationIssue。
3. 禁止不同 ID 隐式混用。

- 区分实体/主体/物品定义/物品实例标识。
- 定义 Generation、WorldEpoch 和 ID 序列化边界。

**出口证据**

- [ ] 编译期或测试证明 ID 不可混用。
- [ ] Kernel 无高层依赖。
- [ ] default/空 ID、越界和跨类型误用被拒绝。
- [ ] 结果错误码可稳定序列化。

### P06 — Kernel Geometry

**直接依赖：** P05。  
**目标完成度：** B。

实现 Position/Transform/Vector/Distance/Bounds。

**交付任务**

1. 实现纯数学类型。
2. 实现方向角归一、距离、范围判断。
3. 覆盖 NaN/边界输入。

- 固定右手坐标、Z 轴、米/弧度及数值容差。
- 覆盖 Infinity、溢出和零长度向量。

**出口证据**

- [ ] Geometry 不查询 Runtime。
- [ ] 数学单元测试确定性通过。
- [ ] 角度归一、边界距离、非有限数和序列化回读有案例。
- [ ] 纯数学不依赖 World。

### P07 — Kernel Time

**直接依赖：** P05。  
**目标完成度：** B。

实现 IClock/FakeClock/Duration/Tick/Timer。

**交付任务**

1. 所有计时可由 FakeClock 推进。
2. 规定 Domain 不直接依赖真实系统时钟。

- 区分逻辑 Tick、单调经过时间和审计 UTC。
- 定义持久化离线时间策略。

**出口证据**

- [ ] 可控逻辑时间测试无真实 Sleep；真实时延测试采用有界等待。
- [ ] Timer/超时边界可完全复现。
- [ ] 同 Tick 到期、零时长、取消与溢出可复现。
- [ ] 墙上时间调整不改变核心计时。

### P08 — Kernel Random

**直接依赖：** P05。  
**目标完成度：** B。

实现 IRandom/DeterministicRandom/Probability/WeightedSelector。

**交付任务**

1. 固定 Seed。
2. 覆盖 0/1 概率与非法权重。

- 固定算法与版本并可导出状态。
- 按 Combat/Loot/AI 拆稳定随机流。

**出口证据**

- [ ] 相同 Seed 得到相同序列。
- [ ] 后续 Combat/Loot/AI 可注入 Random。
- [ ] 状态保存后继续序列一致。
- [ ] 插入一次 AI 抽样不改变 Loot 流。
- [ ] 非法权重和边界概率明确。

### P09 — Command / Query / Event

**直接依赖：** P05。  
**目标完成度：** B。

建立跨模块协作基础。

**交付任务**

1. 实现 ICommand/Handler/Result。
2. 实现 IQuery/Handler，Query 原则只读。
3. 实现 DomainEvent/DeferredEventQueue。
4. 定义异常/取消/重复注册策略。

- 定义消息版本、CommandId、事件因果/序号和提交后发布。
- Handler 注册不进入 Kernel。

**出口证据**

- [ ] 示范模块完成 Command→Result、Query、DeferredEvent。
- [ ] 不存在全局 Service Locator。
- [ ] 查询不泄漏可变对象。
- [ ] 事件派生队列有数量/深度上限。
- [ ] 重复注册和未知命令失败。

### P10 — Platform Configuration / Logging

**直接依赖：** P09。  
**目标完成度：** B。

实现配置、结构化日志、审计/性能日志。

**交付任务**

1. 模块独立 Options。
2. 日志上下文包含 Module/World/Entity/Correlation。
3. 配置启动期验证。

- 日志脱敏、模块 Options 验证、配置 Hash。
- 启动 P60.F 的身份/授权契约与默认拒绝。

**出口证据**

- [ ] 错误配置启动失败并给出中文诊断。
- [ ] Platform 不包含业务规则。
- [ ] Token/连接串不进入报告。
- [ ] 配置无效时不进入 Running。
- [ ] 本地测试身份与生产身份分开。

### P11 — Platform Persistence

**直接依赖：** P10。  
**目标完成度：** C。

实现 DB 技术层和事务。

**交付任务**

1. ConnectionFactory/Transaction/ParameterizedCommand。
2. Migration/BulkLoader。
3. 取消和超时。
4. 业务 SQL 必须留在模块 Infrastructure。

- 在选定真实数据库上验证隔离级别、乐观并发、版本写入和幂等账本。
- 启动 P65.F 故障点。

**出口证据**

- [ ] 事务 Rollback 无残留。
- [ ] Platform.Persistence 不出现业务表语义。
- [ ] 提交/回滚、超时结果未知、旧版本覆盖和连接释放有真实集成报告。
- [ ] 内存替身不算 C。

### P12 — Platform Concurrency

**直接依赖：** P09,P10。  
**目标完成度：** B+C。

实现 WorkQueue、Mailbox、Scheduler、BackgroundWorker 基础。

**交付任务**

1. 定义取消、异常隔离和优雅关闭。
2. 建立有界队列/Backpressure。

- 定义有界队列、公平配额、关键回执预留通道和关闭顺序。

**出口证据**

- [ ] 异常任务不终止 Worker。
- [ ] 队列不会无限增长。
- [ ] 满队列返回可识别拒绝。
- [ ] 取消/异常不泄漏任务。
- [ ] 关闭超时有未完成清单。

### P13 — 通用 Networking.Transport

**直接依赖：** P12,P60.F。  
**目标完成度：** C。

提供可选通用连接层，不实现 WoW 协议。

**交付任务**

1. 实现 TCP/可插拔 Transport 接口。
2. Receive/Send Buffer。
3. Backpressure/RateLimit。
4. Loopback Harness。

- 明确通用帧协议与尺寸上限。
- 先 Loopback，真实世界入口另接认证/授权和主体绑定。

**出口证据**

- [ ] 半包/粘包/慢连接等传输测试通过。
- [ ] 没有 Opcode、WorldPacket、3.3.5a 依赖。
- [ ] 半包/粘包、超大帧、慢发送、断线释放和队满均可验证。
- [ ] 未授权连接不能执行世界写。

### P14 — Metrics / Diagnostics

**直接依赖：** P10,P12。  
**目标完成度：** C。

实现 Metrics、Health、Tracing、SlowOperationDetector。

**交付任务**

1. MapTick/DB/AI/Script/Queue 指标。
2. CrashContext。
3. 慢操作日志。

- 记录 Tick/Queue/DB/AI/Script 指标和高基数限制。
- 建立 P64.F 环境/负载清单。

**出口证据**

- [ ] 可触发并观测慢任务。
- [ ] 诊断不改变业务状态。
- [ ] 慢操作可定位。
- [ ] 遥测失败不破坏业务。
- [ ] 指标没有无限实体标签，基准配置可复用。

### P15 — Static Data Schema / Loader

**直接依赖：** P05,P09,P11。  
**目标完成度：** C。

实现通用 Schema、Loader、Index。

**交付任务**

1. 分 P15.J/P15.S 交付 JSON 与 SQL 来源 Adapter。
2. 构建只读索引。
3. 定义数据版本。

- 拆 P15.J：领域拥有 Schema/定义，JSON→冻结索引。
- P15.S：真实 SQL 来源，统一规范化。

**出口证据**

- [ ] 相同数据构建相同 Catalog。
- [ ] 不使用 DBC 作为强制格式。
- [ ] JSON/SQL 相同数据生成相同规范 Hash。
- [ ] 基础库不引用 Ability/Item 业务定义。
- [ ] 错误定位到字段和来源。

### P16 — Catalog Validator / Freeze / Reload

**直接依赖：** P15.J。  
**目标完成度：** C。

实现验证、Freeze、受控 Reload。

**交付任务**

1. 重复 ID、缺引用、循环引用检测。
2. Load→Validate→Freeze。
3. Reload 采用新快照替换，不原地乱改。

- 拆 P16.F 验证/冻结与 P16.R 热重载。
- 关联 Catalog 整包校验，运行实例固定版本或显式迁移。

**出口证据**

- [ ] 故意错误数据被拒绝。
- [ ] 运行期查询只读。
- [ ] 错误新版本保持旧版。
- [ ] 同 Tick 不观察混合版本。
- [ ] 活动 Cast/Item 的删除/迁移政策可测。
- [ ] 旧版本回放可取。

### P17 — WorldInstance / Registry / Lifecycle

**直接依赖：** P15.J,P18,P24。  
**目标完成度：** C。

建立世界实例和实体生命周期。

**交付任务**

1. WorldInstance/Registry。
2. Spawn/Add/Remove/Despawn/Relocate 状态机。
3. 重复 EntityId 检查。

- 生命周期使用 Epoch/Generation。
- 销毁清理订阅、Timer 和请求，注册不复制实体状态。

**出口证据**

- [ ] 两个世界实体隔离。
- [ ] 非法生命周期顺序失败。
- [ ] 重复 Add/Remove/Despawn、销毁后回执、两个世界归属冲突被正确处理。
- [ ] 不支持转移时明确拒绝。

### P18 — WorldMailbox / WorldExecutor

**直接依赖：** P09,P12。  
**目标完成度：** C。

建立世界唯一写入上下文。

**交付任务**

1. 实现 Mailbox。
2. 实现 Executor。
3. 为实体写操作增加上下文 Guard。
4. 定义外部线程回写流程。

- 实现有效执行租约/非重入 Guard，不只判断线程 ID/AsyncLocal。
- 接入 P60.F 写前校验。

**出口证据**

- [ ] 非 WorldExecutor 写 Entity 的负向测试必须失败。
- [ ] DB/Network Worker 不直接写世界状态。
- [ ] Task.Run 继承上下文也不能写。
- [ ] 过期 Epoch/Generation 回执无效。
- [ ] 同 World 无重入，DB 回执不被普通流量饿死。

### P19 — WorldTick / Scheduler

**直接依赖：** P07,P08,P14,P17。  
**目标完成度：** C。

建立确定性 Tick 和多世界调度。

**交付任务**

1. 定义 Tick Phase。
2. FakeClock 驱动。
3. 多个 World 分配有限 Worker。
4. 慢 Tick 监测。

- 冻结 Tick/子阶段顺序、稳定排序和确定动作预算。
- 提供最小模拟与测量入口；P62.F 在 P19/P20 核心完成后验收，P64.F 提供基准框架。

**出口证据**

- [ ] 同 World 不并发写，多 World 可并行。
- [ ] 相同输入 Tick 顺序稳定。
- [ ] 相同接纳序列得到相同状态 Hash。
- [ ] 慢 Tick 有界追赶。
- [ ] 多世界并行不产生同世界并发写。

### P20 — Spatial / Cell / Grid

**直接依赖：** P19,P24。  
**目标完成度：** C。

实现空间索引和分块生命周期。

**交付任务**

1. Add/Move/Remove。
2. Nearby/Range Query。
3. 跨 Cell 更新。
4. Grid 活跃/卸载。

- 统一重定位 Prepare/Commit。
- 明确索引可重建与 Cell 卸载约束。

**出口证据**

- [ ] 随机实体移动后与暴力扫描结果一致。
- [ ] 性能基线记录。
- [ ] 跨 Cell/边界、失败重定位、移除期间查询与暴力扫描一致。
- [ ] Transform 与 Spatial 不出现半更新。
- [ ] 保存首版基准。

### P21 — Visibility / Relevance

**直接依赖：** P20,P26。  
**目标完成度：** C。

实现通用可见/相关集合，而非客户端 UpdateObject。

**交付任务**

1. VisibilitySet。
2. Distance/Tag/State Filter。
3. Enter/Leave Delta。
4. 只读 View Model。

- View/Delta 带世界/快照版本。
- 只读模型不共享可变集合。

**出口证据**

- [ ] 进入只产生一次 Enter，离开一次 Leave。
- [ ] 没有 WoW UpdateMask/Packet。
- [ ] 边界反复进出、目标销毁、状态/Tag 变更产生正确 Enter/Leave。
- [ ] 外部快照不会因世界变化原地改变。

### P22 — Spawn / Respawn

**直接依赖：** P16.F,P19,P20,P24。  
**目标完成度：** C。

实现静态和动态 Spawn。

**交付任务**

1. SpawnRegistry。
2. RespawnTimer。
3. LinkedRespawn。
4. DynamicSpawn。
5. Grid 加载/卸载集成。

- Spawn 唯一键/Generation 与 Respawn Timer 绑定。
- LinkedRespawn 循环校验。

**出口证据**

- [ ] 重载不重复 Spawn。
- [ ] FakeClock 可推进 Respawn。
- [ ] 重复加载、不活动 Grid、先销毁后定时器到期不重复生成。
- [ ] 崩溃恢复后已确认实例不会重生为副本。

### P23 — Terrain / Collision / Navigation Ports

**直接依赖：** P06,P09。  
**目标完成度：** B+C。

定义通用环境查询契约和 Fake/Adapter。

**交付任务**

1. ITerrainQuery。
2. ICollisionQuery。
3. INavigationQuery。
4. Fake 实现用于测试。

- Fake 仅用于 Port 测试。
- 提供简单真实平面/障碍参考 Adapter，并定义无路/超时/取消结果。

**出口证据**

- [ ] Movement/AI 只依赖 Port。
- [ ] 不绑定 WoW Map/VMap/MMap 文件格式。
- [ ] 查询接口单位一致。
- [ ] 无路不是固定成功直线。
- [ ] 为 P28 提供可重复的实际环境场景。

### P24 — Entity Identity / Transform / Life

**直接依赖：** P05,P06,P09。  
**目标完成度：** B（World 集成由 P17 验收）。

构建薄实体和基础能力。

**交付任务**

1. IdentityCapability。
2. TransformCapability。
3. LifeState/TargetState。
4. Capability 组合容器。

- Transform 唯一归 Entities。
- Life/Target 只通过窄能力暴露。
- 禁止所有模块都能写的公共实体。

**出口证据**

- [ ] 不存在巨型 Unit。
- [ ] 外部模块不能访问内部可变集合。
- [ ] 本阶段验收基础能力；真实世界集成和写 Guard 在 P17/P18 验收。
- [ ] 宿主无巨型全能方法。
- [ ] 组合容器不作万能服务定位。

### P25 — Stats / Resources / Modifier

**直接依赖：** P24,P07。  
**目标完成度：** C。

实现属性、资源和可撤销 Modifier。

**交付任务**

1. Base/Derived Stats。
2. 通用 ResourcePool。
3. ModifierHandle。
4. 重算策略。

- 规定 Modifier 优先级、舍入、来源句柄、重算和最大资源变化策略。

**出口证据**

- [ ] 添加/移除 Modifier 可逆。
- [ ] 资源上下界正确。
- [ ] 重复移除、多个来源交错增减、资源超界/溢出可测。
- [ ] 移除一个来源不撤销其他来源。

### P26 — Control / Ownership / Tags

**直接依赖：** P24。  
**目标完成度：** C。

实现控制状态、所有权和标签。

**交付任务**

1. Control Tags。
2. Owner/Creator/Controller/Summoner。
3. Tag Query。

- 临时 Control/Tag 使用来源句柄。
- Ownership 关系校验与销毁清理。

**出口证据**

- [ ] 不同 Ownership 关系不混用。
- [ ] 临时控制可正确撤销。
- [ ] 两个 Stun 来源移除一个仍受控。
- [ ] 关系循环/目标销毁无悬空可写引用。

### P27 — Movement State / Speed / Input

**直接依赖：** P20,P25,P26,P60.F。  
**目标完成度：** C。

实现通用移动输入和状态更新。

**交付任务**

1. MovementState。
2. SpeedModel。
3. MovementIntent。
4. WorldExecutor 内更新 Transform/Spatial。

- Movement 只计算意图/轨迹，通过重定位入口更新唯一 Transform。
- 输入包含主体和序号。

**出口证据**

- [ ] 外部输入不能直接写 Position。
- [ ] Speed Modifier 可叠加/撤销。
- [ ] 伪造主体、乱序旧意图、非有限坐标拒绝。
- [ ] 移动后空间索引与位置一致。
- [ ] 保存移动基准。

### P28 — Server Motion / Path

**直接依赖：** P23,P27。  
**目标完成度：** C。

实现 Point/Random/Chase/Follow/Flee/Home/Waypoint。

**交付任务**

1. Motion 策略。
2. NavigationPort 集成。
3. 切换/取消优先级。

- 分批实现 Motion，包含 Spline/Jump/Knockback。
- 路径结果带请求版本，明确优先级。

**出口证据**

- [ ] 每类 Motion 有状态机测试。
- [ ] AI 不直接改 Position。
- [ ] 取消后旧路径不生效。
- [ ] 无路、目标销毁、模式抢占、超时及每类 Motion 都有真实小场景。

### P29 — Movement Validation

**直接依赖：** P27,P28。  
**目标完成度：** C。

实现输入校验和 Violation。

**交付任务**

1. 速度/跳变/非法区域基础规则。
2. Accept/Correct/Reject/Flag。
3. 检测与处罚分离。

- 在固定合法/非法数据集上定义误判度量，校验检测与处罚分离。

**出口证据**

- [ ] 合法输入低误判。
- [ ] 异常输入生成结构化 Violation。
- [ ] 超速/跳变与合法击退/传送能区分。
- [ ] Accept/Correct/Reject/Flag 每种输出可追溯，不能仅写低误判。

### P30 — Combat Relation / AutoAttack

**直接依赖：** P19,P25,P26,P27。  
**目标完成度：** B（真实自动攻击闭环由 P32 验收）。

建立战斗关系与攻击时钟。

**交付任务**

1. CombatReference。
2. Engage/Disengage。
3. AttackTimer。
4. AutoAttack Start/Stop。

- 先交付攻击关系/计时与攻击请求，真实伤害在 P32 接入。
- 生命周期清理不等到全模块完成。

**出口证据**

- [ ] 多对多战斗关系正确。
- [ ] 目标死亡/移除时清理。
- [ ] 多对多解除不影响其他关系。
- [ ] 停止/目标移除取消调度。
- [ ] P32 后补实际自动攻击闭环。

### P31 — Hit Resolution

**直接依赖：** P08,P25,P30。  
**目标完成度：** B+C。

实现可配置命中结果 Pipeline。

**交付任务**

1. Hit/Miss/Dodge/Block/Crit 等通用结果。
2. 固定 Seed。
3. 公式策略可替换。

- 固定策略输入单位、顺序、夹紧和舍入。
- 不同命中策略通过配置替换。

**出口证据**

- [ ] 固定输入输出稳定。
- [ ] 核心无 WoW SpellId/Class 特判。
- [ ] 0/1 概率和无效目标有测试。
- [ ] 随机结果有来源流和版本。
- [ ] 公式差异记录而非强行复制 AC。

### P32 — Damage / Healing / Mitigation

**直接依赖：** P25,P30,P31。  
**目标完成度：** C。

实现伤害、治疗、减免、吸收 Pipeline。

**交付任务**

1. 分阶段处理。
2. Modifier Contract。
3. 最终 Health 应用。

- 固定 Damage/Heal 各阶段。
- 区分有效量/过量。
- Health 与 Life 不变量同次提交。

**出口证据**

- [ ] 每 Stage 单测。
- [ ] 最终世界状态写入仍在 WorldExecutor。
- [ ] 并发来源同 Tick 结算顺序稳定。
- [ ] 零/负/极大伤害、吸收耗尽、过量治疗有案例。
- [ ] 自动攻击真正改变 Health。

### P33 — Threat

**直接依赖：** P32。  
**目标完成度：** C。

实现威胁表和计算。

**交付任务**

1. ThreatTable。
2. Damage/Healing Threat。
3. Taunt/Redirect。
4. 清理生命周期。

- Threat 通过公开事实/窄同步 Port 更新，定义稳定并列排序与 Taunt 期限。

**出口证据**

- [ ] AI 只读查询 Threat。
- [ ] 无悬空跨世界引用。
- [ ] 目标离开/死亡、重定向失效、治疗威胁和并列目标选择可复现。
- [ ] AI 不持有可变 ThreatTable。

### P34 — Death / Combat Cleanup

**直接依赖：** P32,P33。  
**目标完成度：** C。

实现死亡和战斗清理。

**交付任务**

1. 停止攻击/施放/Threat。
2. 发布 EntityKilled。
3. 死亡只触发一次。

- 死亡确认与 EntityKilled 只一次。
- 使用公开生命周期清理注册点接后续模块。

**出口证据**

- [ ] 重复伤害不重复死亡事件。
- [ ] 其他模块通过 Event 消费。
- [ ] 致死后同 Tick 的攻击/施放不会继续生效。
- [ ] 已接入模块清理完整。
- [ ] 后续 P36/P40/P52 必须重跑此场景。

### P35 — Ability Definition / Catalog

**直接依赖：** P15,P16。  
**目标完成度：** C。

实现通用能力定义。

**交付任务**

1. AbilityDefinition/EffectDefinition/Tags。
2. Relationship/Upgrade 可选关系。
3. Data Validator。

- AbilityDefinition/EffectDefinition 归 Abilities，Catalog 基础设施不反向引用。
- 关系图和效果绑定可验证。

**出口证据**

- [ ] Definition 只读。
- [ ] 没有 Spell.dbc/WoW 属性位硬编码。
- [ ] 未知 Effect、循环关系、非法消耗/范围在加载时拒绝。
- [ ] 定义版本可由运行实例固定。

### P36 — Casting State Machine

**直接依赖：** P19,P25,P26,P34,P35。  
**目标完成度：** C。

实现施放准备、进行、完成、中断、取消。

**交付任务**

1. CastRequest/Context。
2. Validation Pipeline。
3. CastTime。
4. Interrupt/Cancel。

- 状态补全 Requested/Reserved/Resolving/Failed。
- 先固定消耗时点接口，P38 完成实际资源预留。

**出口证据**

- [ ] FakeClock 驱动施放。
- [ ] 状态机边界测试。
- [ ] 死亡/移除、施放完成同 Tick 中断、重复 Cancel、目标失效可复现。
- [ ] 本阶段验收状态机与真实 World 计时；消耗和效果完整闭环分别在 P38/P39 验收。

### P37 — Ability Targeting

**直接依赖：** P20,P21,P23,P35。  
**目标完成度：** C。

实现通用目标选择。

**交付任务**

1. Explicit/Area/Cone/Chain/Nearest/Random。
2. Tag/Alive/Relation/LOS Filter。
3. TargetLimit。

- 候选稳定排序再限制数量/随机选择。
- 明确 Cone/Chain 边界、关系与 LOS。

**出口证据**

- [ ] 使用 Spatial Query。
- [ ] Targeting 不执行 Effect。
- [ ] 相同候选不同容器插入顺序仍得到同样目标。
- [ ] 失效目标和数量上限可测，选择不执行 Effect。

### P38 — Cost / Cooldown

**直接依赖：** P25,P36。  
**目标完成度：** C；ItemCost 待 P38.I。

实现通用资源消耗和冷却。

**交付任务**

1. ResourceCost。
2. ItemCost Port。
3. Ability/Category/Global Cooldown。
4. 预留/扣除/回滚时机。

- ResourceCost 先完成。
- ItemCost 仅定义需求 Port，P44 后完成 P38.I。
- 明确冷却持久/离线规则。

**出口证据**

- [ ] 失败不错误扣费。
- [ ] Cooldown 可持久/快照（如配置要求）。
- [ ] 提交前失败释放预留。
- [ ] 中断退款依据策略。
- [ ] 重复请求不重复扣费。
- [ ] 没有真实 Inventory 不验收 ItemCost。

### P39 — Effect Registry

**直接依赖：** P22,P27,P32,P36,P37。  
**目标完成度：** C。

实现独立 Effect Handler。

**交付任务**

1. Damage/Heal/Resource/Move/Spawn 基础 Effect。
2. Registry 与未知 Effect 诊断。
3. Trigger recursion guard。

- Registry 明确未知/重复 Handler。
- 多 Effect 声明逐个提交或受支持的整体原子策略。

**出口证据**

- [ ] 新增 Effect 不修改巨大 switch。
- [ ] 跨模块只用 Port。
- [ ] 第二效果失败不错误回滚已提交伤害。
- [ ] 触发链有深度/总数界限。
- [ ] Move/Spawn/Damage 通过真实 Port。

### P40 — StatusEffect Core

**直接依赖：** P25,P26,P36,P39。  
**目标完成度：** C。

实现持续状态生命周期。

**交付任务**

1. StatusInstance/Application。
2. Duration/Stack/Charges。
3. RemovalMode。
4. StatusHost。

- 定义同来源/异来源叠加、Refresh/Replace、Charge 与 RemovalMode。
- 接入死亡清理。

**出口证据**

- [ ] Apply/Refresh/Stack/Expire/Cancel 测试。
- [ ] 不直接写其他模块私有字段。
- [ ] 同 Tick 过期与重复移除、替换来源、死亡清理和快照恢复有案例。
- [ ] 移除不泄漏句柄。

### P41 — Status Modifier / Periodic

**直接依赖：** P19,P25,P27,P32,P40。  
**目标完成度：** C。

实现 Modifier 和周期效果。

**交付任务**

1. Stats/Movement/Combat Modifier。
2. Periodic Damage/Heal/Resource/Trigger。
3. 统一 Tick。

- 固定 Periodic 与 Expire 同 Tick 顺序、首跳及补跳策略。
- 不使用真实 Timer。

**出口证据**

- [ ] 移除 Status 后仅撤销该来源 Modifier，并保留其他来源影响。
- [ ] 周期 Tick 数可确定。
- [ ] 精确 Tick 数、刷新边界、堆叠和掉帧追赶可复现。
- [ ] 其他来源仍存在时移除只撤销当前 Modifier。

### P42 — Proc / Immunity / Dispel / Interrupt

**直接依赖：** P08,P38.R,P39,P41。  
**目标完成度：** C。

实现高级通用能力机制。

**交付任务**

1. ProcEvent/Rule/Chance/Cooldown/ChainGuard。
2. Immunity。
3. Dispel/Cleanse。
4. Interrupt。

- Proc 携因果链和去重键，确定触发配额。
- Immunity/Dispel/Interrupt 有优先级。

**出口证据**

- [ ] Proc 不无限递归。
- [ ] 固定 Seed 可重复。
- [ ] 循环触发被终止并诊断。
- [ ] 充能、驱散数量、部分免疫、同时打断可复现。
- [ ] 保存能力/Status 基准。

### P43 — Item Definition / Instance

**直接依赖：** P11,P15,P16。  
**目标完成度：** C。

实现物品定义和实例。

**交付任务**

1. ItemDefinition。
2. ItemInstance。
3. Count/Owner/InstanceState。
4. Persistence Snapshot。

- ItemDefinitionId/ItemInstanceId 分离。
- Slot/Owner 不作为另一份可写真相。
- 版本化快照。

**出口证据**

- [ ] 模板与实例分离。
- [ ] Roundtrip 状态一致。
- [ ] 真实 DB Roundtrip、旧 Schema/非法数量处理、重复 ItemInstanceId 拒绝。
- [ ] 没有 Inventory 时物品明确为未安置状态。

### P44 — Inventory / Container / Atomic Mutation

**直接依赖：** P17,P18,P43。  
**目标完成度：** C。

实现容器和原子物品变更。

**交付任务**

1. Container/Slot。
2. Move/Split/Merge/Create/Destroy。
3. MutationSet。
4. Rollback。

- 采用完整验证计划和同 Owner 提交。
- 存储位置约束/幂等账本，接入 P65.F。

**出口证据**

- [ ] 失败不复制/丢失物品。
- [ ] 重复命令幂等策略明确。
- [ ] Move/Split/Merge 守恒。
- [ ] Create/Destroy 有原因。
- [ ] 满包/非法嵌套/重复命令/提交后回执丢失不复制。
- [ ] 提供真实 Inventory 消耗接口；ItemCost 集成由后续 P38.I 验收，不作为 P44 反向依赖。

### P45 — Equipment / Item Modifier

**直接依赖：** P25,P38,P44。  
**目标完成度：** C。

实现可配置装备与属性影响。

**交付任务**

1. Equipment Slots 配置化。
2. Requirements。
3. Stats/Ability Modifier。
4. Equip/Unequip 原子流程，明确准备、提交和持久确认点。

- 装备与属性变更组合计划，Requirements 失败无副作用。
- Durability/Charges 明确是否启用。

**出口证据**

- [ ] 装备/卸下 Modifier 可逆。
- [ ] 不硬编码 WoW 槽位/职业。
- [ ] Equip/Unequip 中途失败仍一致。
- [ ] 恢复重建 Modifier 不重复叠加。
- [ ] 能力解锁与物品快照一致。

### P46 — Loot Generation / Eligibility

**直接依赖：** P08,P34,P43。  
**目标完成度：** C。

实现通用掉落实例。

**交付任务**

1. LootDefinition。
2. Weighted Generation。
3. Eligibility。
4. 生命周期。

- 生成结果落为唯一 LootInstance，重试不重新随机。
- 资格规则和过期状态持久化。

**出口证据**

- [ ] 固定 Seed 可复现。
- [ ] 无资格 Actor 不能领取。
- [ ] 重启前后同一次掉落内容一致。
- [ ] 无资格、已过期、来源重复死亡不能重复生成权益。

### P47 — Loot → Inventory Transaction

**直接依赖：** P11,P44,P46。  
**目标完成度：** C。

完成掉落转移原子闭环。

**交付任务**

1. TakeItem。
2. Inventory 容量校验。
3. 失败回滚。
4. 幂等领取。

- Loot 标记、Inventory、幂等结果和 Outbox 同事务。
- 定义 Pending/Committed 查询。

**出口证据**

- [ ] 故障注入不复制物品。
- [ ] 同一 Loot 只能成功转移一次。
- [ ] 在准备、DB 提交前后、回执丢失、内存应用前逐点崩溃。
- [ ] 多主体抢同项只一方成功。
- [ ] 成功确认后恢复不丢物品。

### P48 — Progression

**直接依赖：** P11,P25。  
**目标完成度：** C。

实现通用等级/经验/技能成长。

**交付任务**

1. ProgressionState。
2. Experience Strategy。
3. LevelChanged Event。
4. Skill Progression。

- 定义溢出、最大等级、多级跃迁、技能上限与持久版本。

**出口证据**

- [ ] 曲线来自配置/策略。
- [ ] 不硬编码 WoW 等级公式。
- [ ] 跨多等级一次更新、非法曲线、重复经验来源与恢复测试。
- [ ] 增长事件只代表已提交事实。

### P49 — Objective Framework

**直接依赖：** P11,P15,P16,P48。  
**目标完成度：** C。

实现目标定义、状态和 Tracker。

**交付任务**

1. ObjectiveDefinition/State。
2. Accept/Abandon（可选）。
3. Progress Tracker。
4. Completion。

- 目标实例版本、事件游标/去重与快照一起保存。
- 区分完成和奖励已领取。

**出口证据**

- [ ] 目标状态可持久恢复。
- [ ] Tracker 只消费事实。
- [ ] 重复/乱序事件、重启中断与 Abandon/重新接取语义清晰。
- [ ] 完成状态不等于自动重复发奖。

### P50 — Objective Handlers

**直接依赖：** P34,P39,P44,P49；Interact 子任务依赖 P58.F。  
**目标完成度：** C。

实现 Kill/Collect/Visit/Interact/Ability 等通用目标。

**交付任务**

1. 每种 Handler 独立。
2. 条件/上限/重复事件处理。

- P50.F 先 Kill/Collect/Visit/Ability。
- P50.I 在 P58.F 后补 Interact。
- 区分累计获得和当前持有。

**出口证据**

- [ ] 每类至少一个自动场景。
- [ ] Combat/Items 不直接知道 Objective。
- [ ] 每类真实事件集成，重复事件不超计。
- [ ] 当前持有型目标会因消耗减少。
- [ ] 最终不能用空 Handler 代替 Interact。

### P51 — Reward Pipeline

**直接依赖：** P11,P44,P48,P49,P50.F。  
**目标完成度：** C。

实现跨模块奖励事务。

**交付任务**

1. XP/Item/Resource/Ability Reward Ports。
2. Reward Transaction。
3. 幂等键。

- ObjectiveInstance/RewardVersion/主体形成业务键。
- 领取标记与各奖励同事务或明确可恢复流程。

**出口证据**

- [ ] 部分失败整体回滚或有明确补偿。
- [ ] 重复完成不重复发奖。
- [ ] 重复完成、不同 CommandId 同业务键、部分失败、提交后崩溃都不重复发奖。
- [ ] 关键状态成功确认后 RPO 为零。

### P52 — AI Core / Context / Scheduler

**直接依赖：** P19,P26,P34。  
**目标完成度：** C。

建立 AI 运行框架。

**交付任务**

1. IAgentAI/AIContext。
2. AIScheduler/预算。
3. 异常隔离。

- AI 操作使用确定的动作配额。
- 可信 C# 的慢执行检测不宣称可强制抢占。

**出口证据**

- [ ] 每个 AI 不创建独立线程。
- [ ] AIContext 不暴露内部 Entity。
- [ ] Idle/忙碌 AI、异常和生命周期清理可测。
- [ ] 无私有实体/线程/Timer。
- [ ] 重跑死亡与移除回归。

### P53 — AI Perception / Target Selection

**直接依赖：** P20,P21,P33,P52。  
**目标完成度：** C。

实现感知和目标选择。

**交付任务**

1. Spatial/Visibility/Tags 查询。
2. 评分/过滤。
3. Assistance 基础。

- 稳定评分与并列排序。
- 感知结果/缓存带版本及失效条件。

**出口证据**

- [ ] 大量 Idle AI 有基准。
- [ ] TargetSelection 可独立测试。
- [ ] 目标离开/遮挡/变更 Tag 不保留悬空引用。
- [ ] Idle 与活跃比例固定后测量感知基准。

### P54 — AI Decision / Actions

**直接依赖：** P27,P28,P32,P39,P53。  
**目标完成度：** C。

实现 Move/Fight/Ability/Flee/Evade 决策。

**交付任务**

1. Decision State Machine。
2. Movement/Combat/Ability Bridge。
3. Evade/Home。

- Bridge 只调窄命令/能力。
- 目标失效切换、Evade/Home 和动作仲裁有状态图。

**出口证据**

- [ ] AI 只发 Command/Capability 请求。
- [ ] 异常 AI 不终止 WorldTick。
- [ ] 同输入动作序列稳定。
- [ ] 移动/施法抢占有规则。
- [ ] 死亡或销毁取消所有 AI 动作和订阅。

### P55 — Data-driven AI

**直接依赖：** P54。  
**目标完成度：** C。

实现 Event/Condition/Action Interpreter。

**交付任务**

1. Event。
2. Condition。
3. Action。
4. Target Selector。
5. 不支持类型明确诊断。

- 解释器用指令/深度/事件数量预算，未知 Action 加载失败。
- 行为树仅为可选 Adapter。

**出口证据**

- [ ] 解释器只使用公开 Contract。
- [ ] 固定输入得到固定结果。
- [ ] 数据循环、超预算、错误条件、异常动作不造成无限执行。
- [ ] 固定输入和规则版本得到相同输出。

### P56 — Scripting API

**直接依赖：** P39,P44,P49；Scenario 子任务依赖 P59。  
**目标完成度：** C。

建立稳定脚本 API。

**交付任务**

1. IWorldApi/IActorApi/IAbilityApi/IItemApi/IScenarioApi。
2. 只读 View。
3. 写操作转 Command。
4. API 版本策略。

- P56.F 交付已存在能力 API、只读 View 和写命令。
- P56.S 再补 IScenarioApi。
- API 兼容/弃用有版本。

**出口证据**

- [ ] 脚本 API 不暴露可变内部 Entity；内嵌 C# 可信度边界另行声明。
- [ ] Script !→ DB/Socket/Internal 架构测试。
- [ ] 真实示例扩展仅引用 Scripting.Api。
- [ ] 无 DB/Socket/Internal。
- [ ] 移除具体内容插件后 Core 可启动。

### P57 — Script Hooks / Registration

**直接依赖：** P40,P42,P51,P56.F。  
**目标完成度：** C。

实现分类 Hook、注册和执行预算。

**交付任务**

1. Actor/Ability/Status/Objective/Scenario Hook。
2. 注册/发现。
3. 异常隔离。
4. 慢脚本检测。

- 分类 Hook 稳定排序。
- 可信 C# 与数据解释器预算分开。
- 加载/卸载撤销订阅。

**出口证据**

- [ ] 没有巨型 ScriptMgr。
- [ ] Hook 顺序和异常策略可预测。
- [ ] 异常前暂存写无泄漏，提交后异常不逆转事实。
- [ ] 慢 Hook 可观测。
- [ ] 不把取消令牌当同步死循环沙箱。

### P58 — Presence / Interaction / Group

**直接依赖：** P13,P17,P21,P26,P60.F。  
**目标完成度：** C。

实现通用会话绑定、Presence、交互和轻量组队。

**交付任务**

1. SessionIdentity。
2. Presence Registry。
3. Interaction Validation。
4. Group Membership/Leader/Events。

- Presence/Group 各自串行聚合。
- 重连使用会话版本。
- 提供 Interact 事实与可测场景，供 P50.I 后续验收。
- 转移若启用则另执行条件任务 P58.T，基础交付不隐含支持转移。

**出口证据**

- [ ] Session 不业务化。
- [ ] Presence 有唯一真相。
- [ ] 不实现 WoW Chat/Guild/LFG。
- [ ] 旧会话/伪造主体无权执行。
- [ ] Leader 离开与重复加入确定。
- [ ] P58.F 达标可验收基础交互；未启用或未完成 P58.T 时返回 TransferNotSupported。

### P59 — Scenario Runtime

**直接依赖：** P11,P17,P22,P49,P58.F。  
**目标完成度：** C。

实现通用场景实例、阶段、重置和保存。

**交付任务**

1. ScenarioInstance。
2. ParticipantSet。
3. StageState。
4. ResetPolicy。
5. SaveSnapshot。

- Scenario 核心提供公开扩展点，不依赖 Scripting.Core。
- 保存阶段版本/Timer/参与者策略。

**出口证据**

- [ ] 多个场景状态隔离。
- [ ] 重启后必要状态可恢复。
- [ ] 重复 Reset、无人超时、断线恢复及多场景隔离。
- [ ] 场景公开契约可驱动真实测试；脚本驱动场景由后续 P56.S 验收，不作为 P59 反向依赖。
- [ ] 旧场景定义有迁移/固定政策。

### P60 — Security / Permissions / RateLimit

**直接依赖：** P60.F,P13,P18,P29,P58.F。  
**目标完成度：** C。

实现通用 RBAC、限流和 Violation。

**交付任务**

1. Role/Permission。
2. RateLimit。
3. Violation Event。
4. Policy。
5. Audit。

- 汇总认证/主体绑定、RBAC、速率与配额、Violation/Policy、持久审计。
- 复核所有入口。

**出口证据**

- [ ] 权限绕过负向测试。
- [ ] 检测与处罚分离。
- [ ] 网络/工具/脚本/Admin 权限不能绕过。
- [ ] 排队后权限撤销也被执行时检查。
- [ ] 日志脱敏和限流过载可测。

### P61 — Administration

**直接依赖：** P60,P57,P59。  
**目标完成度：** C。

实现复用正式业务入口的管理命令。

**交付任务**

1. CommandParser/Dispatcher。
2. PermissionCheck。
3. Entity/World 调试命令。
4. ServerControl/Reload。

- 管理命令复用正式业务流程。
- Reload 使用新快照。
- 关闭复用 Draining 流程。

**出口证据**

- [ ] Admin 不直接写 Entity Internal。
- [ ] 敏感操作有 Audit。
- [ ] 低权限/无审计存储失败策略明确。
- [ ] Admin 不直改 Internal。
- [ ] 敏感操作有主体/原因/结果。

### P62 — SimulationHost / Command Replay

**直接依赖：** P62.F,P47,P51,P54,P59,P56.S；启用转移时增加 P58.T。  
**目标完成度：** C。

建立无需客户端的仿真与重放。

**交付任务**

1. SimulationHost。
2. Scenario Driver。
3. Command/Event Capture。
4. Deterministic Replay。

- 扩展捕获构建/配置/Catalog/随机状态/接纳顺序/外部结果。
- 跨世界记录交接顺序。

**出口证据**

- [ ] 同一 Capture 重放结果稳定。
- [ ] 核心验收完全不依赖 WoW 客户端。
- [ ] 规范 Hash/事件序列一致。
- [ ] 首次分歧定位到 Tick/Command。
- [ ] 离线回放隔离外部副作用，不双放命令和其派生事件。

### P63 — AC 通用参考对照 Runner

**直接依赖：** P00,P31,P32,P41,P46。  
**目标完成度：** D（选定机制）。

对选定 AC 核心机制做参考验证。

**交付任务**

1. 定义 Reference Scenario。
2. 运行 AC 参考计算/数据提取工具或固定向量。
3. 运行 Legends。
4. 比较通用状态/算法结果。
5. 记录 KnownDifference。

- 按源码分析/固定向量/实际运行分别记录证据。
- 用至少命中、周期边界、掉落权重三类可泛化案例。

**出口证据**

- [ ] 报告包含 AC Commit/File/Function。
- [ ] 不比较 WoW Packet/客户端协议。
- [ ] 每类输入/预期可追溯，差异与容差有依据。
- [ ] 实际运行对照未做时如实标记，不能虚报等价 AC。

### P64 — Performance Baseline

**直接依赖：** P64.F,P42,P45,P54,P62。  
**目标完成度：** C+性能配置达标。

建立核心高频路径性能基线。

**交付任务**

1. WorldTick/Spatial/Movement/Combat/Ability/AI/Inventory Benchmarks。
2. 记录 Allocation/GC。
3. 固定数据规模。

- 汇总各模块首集成基线。
- 固定目标档位、环境、活跃率和命令速率。
- 同时满足绝对预算与退化门限。

**出口证据**

- [ ] 重复运行波动可控。
- [ ] 明显退化可检测。
- [ ] 提供原始数据和重复运行分布。
- [ ] Tick p99/分配/GC/队列均有结论。
- [ ] 无法测量不写性能已通过。

### P65 — Fault Injection / Stability

**直接依赖：** P65.F,P47,P51,P62,P64；启用转移时增加 P58.T。  
**目标完成度：** C+稳定性配置达标。

验证 DB 失败、取消、异常、重复命令和长运行稳定性。

**交付任务**

1. DB 故障恢复。
2. World 创建销毁。
3. 大量 AI/Status。
4. 队列压力。
5. 物品复制专项。

- 统一故障矩阵，覆盖保存乱序、关闭超时、长期 Catalog 引用、生成/销毁和事务账本恢复。

**出口证据**

- [ ] 无不可恢复泄漏。
- [ ] 关键事务不复制/丢失状态。
- [ ] 发布配置完成 24h 长稳或正式变更后的既定目标。
- [ ] 排空后对象/句柄/订阅/Timer 无持续增长。
- [ ] 经济边界不重复不丢失。

### P66 — 部署与运维

**直接依赖：** P11,P60,P61,P64,P65。  
**目标完成度：** C+运维达标。

完成通用 ServerHost 部署、配置、Migration、备份恢复。

**交付任务**

1. 中文部署文档。
2. 配置模板。
3. Migration/Backup/Restore。
4. Health/Metrics。
5. Graceful Shutdown。

- 备份包含 Catalog/Schema/幂等账本/Outbox。
- 迁移前恢复方案与灰度/回退策略可执行。

**出口证据**

- [ ] 干净机器可从零部署。
- [ ] 备份→破坏→恢复成功。
- [ ] 真实干净环境部署与备份破坏恢复。
- [ ] 实测 RPO/RTO。
- [ ] 关闭超时后可恢复 Pending，不谎称全部落库。

### P67 — 最终架构与范围审计

**直接依赖：** P00–P66（全部必交付子任务）。  
**目标完成度：** C+适用 D+发布配置验收。

确认项目仍然是“AC 通用核心”，没有悄悄回到 WoW 服务端路线。

**交付任务**

1. 跑全套 Architecture Tests。
2. 扫描 WoW 特定依赖关键词/Assembly。
3. 复核 Data Ownership。
4. 复核 Progress/Context/规范自更新记录。
5. 执行完整 Simulation E2E。

- 对照范围、契约图、所有权表和全部阶段证据。
- 检查延期依据，执行第 8 节 E2E。

**出口证据**

- [ ] 没有 3.3.5a/Opcode/Boss/LFG/Warden 等核心依赖。
- [ ] 全部必交付核心阶段 ACCEPTED；正式范围调整的延期项列明影响。
- [ ] 按恢复索引能够核对真实基线并继续维护。
- [ ] 必交付能力全部 ACCEPTED。
- [ ] 只有正式移出范围的项可单独延期并说明影响。
- [ ] 恢复索引真实可用，不凭关键词扫描替代架构审计。

## 7. 任务、契约与依赖变更模板

任务描述目标、依赖、修改范围和证据；实施、功能检查、架构自审、失败恢复检查与最终验收按顺序完成。

```text
任务 ID / 所属 Pxx 或子任务：
结果与成功条件：
直接依赖（必须具体到已形成证据的子任务）：
修改范围 / 已有工作区差异：
状态所有者 / 合法执行上下文：
契约变更及真实调用方：
失败、取消、重复、过载与持久化确认语义：
适用检查及验收数据：
AC 参考与通用性说明（适用时）：
当前停点 / 阻塞 / 下一安全动作：
```

公共契约的完整字段使用 PROJECT_SPEC 第 4 节模板，避免维护另一套不一致表单。新增依赖时同时更新当前阶段和受影响调用方；区分完整阶段与基础子任务，不能制造“父阶段依赖未来集成，未来集成又依赖父阶段”的循环。

关键子任务依赖展开如下，完整阶段包含其必交付子任务但不作为子任务的前置条件。P58.T 是条件扩展，不计入默认必交付；一旦启用，其专项回放、故障和最终 E2E 随之成为必需：

| 子任务 | 直接依赖 | 完成后可供使用的能力 |
|---|---|---|
| P15.J | P05、P09 | JSON Catalog |
| P15.S | P15.J、P11 | 同规范 SQL Catalog |
| P16.F | P15.J | 校验与冻结 |
| P16.R | P16.F、P19 | Tick 边界版本切换 |
| P60.F | P09、P10 | 身份、默认拒绝与授权基础 |
| P62.F | P19、P20 | 最小世界模拟与输入回放 |
| P64.F | P14 | 冻结环境/负载的基准框架 |
| P65.F | P11、P12 | 故障注入基础 |
| P38.R | P25、P36 | 资源消耗与冷却 |
| P38.I | P38.R、P44 | 真实物品消耗 |
| P50.F | P34、P39、P44、P49 | Kill/Collect/Visit/Ability 目标 |
| P50.I | P50.F、P58.F | Interact 目标 |
| P56.F | P39、P44、P49 | 已有能力的脚本 API |
| P56.S | P56.F、P59、P57 | 场景 API 与 Hook 集成 |
| P58.F | P13、P17、P21、P26、P60.F | 会话、Presence、交互和小组基础 |
| P58.T（条件扩展） | P58.F、P11、P62.F | 启用时交付受控世界转移与恢复，否则明确不支持 |

P19 完成核心 Tick 验收后，可执行 P62.F；M2 整体出口才要求其回放证据。不能反过来把 P62.F 作为 P19 核心实现的入口。P30 的关系/调度本身达到 B 后由 P32 补实际伤害；P24 的基础能力达到 B 后由 P17 补 World 集成。

## 8. 最终端到端验收

使用独立测试存储、固定数据和已知 Seed，不依赖 WoW 客户端。场景输入与断言至少包含：

| 场景 | 路径 | 必须断言 |
|---|---|---|
| 世界基础 | Create World→Spawn→Move/Path→Spatial/Visibility | 位置唯一、索引一致、Enter/Leave 正确、无跨世界引用 |
| 战斗能力 | AI 感知→Engage→Hit/Damage→Cast→Status→Death | 状态/伤害/消耗顺序稳定；死亡只一次；清理与 Modifier 撤销完整 |
| 掉落领取 | 生成 Loot→资格校验→Inventory→DB 确认→重复领取 | 内容不重新随机；仅一次成功；满包/失败不复制或丢失 |
| 目标奖励 | 消费事实→更新目标→完成→Reward→重复事件 | 幂等业务键有效；奖励与领取标记一致；当前持有/累计目标语义不同 |
| 持久恢复 | Snapshot/账本→强制中断→Restart→Restore | 已确认经济状态不丢失；版本不回退；Pending 可判定并恢复 |
| 世界转移（启用时） | 冻结源→目的预留→归属切换→确认；各点崩溃 | 最多一个有效 Owner；旧 Epoch/会话输入不能修改；无实体复制；未启用时断言明确拒绝 |
| 扩展边界 | 移除具体脚本→Core 启动；错误/超预算数据行为 | 核心可启动；未知必需绑定明确拒绝；不声称不可信 C# 有沙箱 |
| 安全过载 | 伪造身份/撤权→满队列→慢客户端→管理命令 | 无绕过授权；资源有界；关键回执可处理；敏感操作有审计 |
| 确定性 | 从同初态回放同接纳输入/外部结果 | 最终状态 Hash、事件序列与经济结果一致；不重复外部副作用 |
| 备份运维 | 新环境部署→迁移→备份→破坏→恢复→关闭 | 达到冻结的 RPO/RTO；未完成写入可列出；报告与真实环境对应 |

E2E 通过不能替代单独的架构负例、AC 参考对照、性能及 24h 长稳报告。最终证据包同时汇总这些专项结论与已知限制。

## 9. 从原路线图迁移

保留全部 P00–P67 编号和原能力范围，避免重编号导致历史任务失联。重复的公共审查/恢复/否决项合并至第 2 节；各阶段保留目标、任务和特有验收证据，并新增依赖和失败边界。

落库顺序：

1. 核对实际代码与旧阶段证据，不依据本文预填 ACCEPTED。
2. 采用第 2、7 节的实施、检查、自审、验收顺序，形成完整任务证据。
3. 把已有状态映射到新阶段/子任务，保留历史证据与代码基线；补建 P60.F/P62.F/P64.F/P65.F 的缺失部分。
4. 核查 P24/P17、P30/P32、P38/P44、P50/P58、P56/P59 的跨阶段集成缺口，不能因基础接口存在而记全阶段完成。
5. 先补已有高风险实现的所有权、经济确认、崩溃恢复与回放证据，再继续扩展新功能。

本版未获取项目仓库、AC 实际 Commit、SDK 补丁版本、数据库版本、硬件配置和测试报告；这些信息分别在 P00/P02/P11/P64.F 落实。本文给出的负载和运行预算是待在目标环境冻结的建议，不作为已验证结论。

