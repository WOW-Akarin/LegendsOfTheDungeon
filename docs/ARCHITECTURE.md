# LegendsOfTheDungeon 核心通用模块化架构设计

> 版本：1.1（优化版）｜修订日期：2026-11-16  
> 技术方向：C# / .NET 10；模块化单体；World Actor；Ports & Adapters  
> 上位规范：[项目设计与开发规范](PROJECT_SPEC.md)  
> 阶段交付：[开发与验收路线图](DEVELOPMENT_ROADMAP.md)  
> 状态：设计基线；未对项目代码、性能或部署能力作完成性认定。

## 1. 目标和质量属性

将 AC 中可泛化的状态机、算法结构和服务器运行机制重新设计为可复用核心。核心不依赖 WoW 客户端、数据格式、数据库表或具体玩法。完整范围及排除项由 PROJECT_SPEC 维护。

架构首先保证状态真相唯一、修改顺序可解释、失败可恢复、模块边界可验证，再优化吞吐和分配。可确定性目标限定为：在相同构建、配置、数值规则、静态数据、初始状态、输入序列和外部结果下，核心模拟输出相同。跨平台位级相同不是未经验证就承诺的能力。

单体是初始部署边界，不意味着全部代码位于一个项目。World Actor 是逻辑串行执行模型，不要求每个世界独占线程。禁止为了形式解耦把每次伤害拆成多次异步消息。

## 2. 模块和工程依赖

### 2.1 L0–L16 是能力分类

保留原有编号用于定位，但不把编号当成严格依赖方向或开发先后。Hosts 是组合根，Tests 是验证代码；它们不属于业务运行时的上游层。真实依赖由项目引用白名单和契约图决定。

| 分类 | 能力域 | 关键交付 |
|---|---|---|
| L0 | Kernel | 强类型 ID、几何、时间、随机、结果、消息基础值类型 |
| L1 | Platform | 配置、日志、指标、诊断、DB 技术、队列、传输、序列化 |
| L2 | Static Data | 通用加载/验证/冻结；各领域拥有自己的定义和 Catalog |
| L3 | Runtime.World | 生命周期、Mailbox、Executor、Tick、Spatial、Visibility、Spawn、环境 Port |
| L4 | Entities | Identity、Transform、Life、Target、Stats、Resources、Control、Ownership、Tags |
| L5 | Movement | 输入、速度、运动策略、寻路编排、校验 |
| L6 | Combat | 战斗关系、自动攻击、命中、伤害/治疗、Threat、死亡编排 |
| L7 | Abilities | 定义、施放、目标、消耗/冷却、Effect、Status、Proc |
| L8 | Items/Inventory/Loot | 实例、容器、原子变更、装备、掉落与领取 |
| L9 | Progression/Objectives | 成长、目标、完成和奖励 |
| L10 | AI | 感知、选择、决策、调度、数据驱动行为 |
| L11 | Interaction/Presence/Grouping | 会话绑定、在线路由、交互、小组聚合 |
| L12 | Scenarios | 参与者、阶段、重置、存档 |
| L13 | Scripting | 稳定 API、Hook、注册、版本、预算 |
| L14 | Security/Administration | 认证 Port、授权、限流、违规事实、审计、管理 |
| L15 | Hosts/Tools | 启动组合、模拟、数据验证、迁移、快照、回放、参考与基准 |
| L16 | Tests/Review | 单元、集成、架构、负例、并发、回放、参考、性能、长稳、验收 |

### 2.2 最小工程形态

```text
src/
├── Kernel/
├── Runtime/{Abstractions,Core}/
├── Entities/{Contracts,Core}/
├── StaticData/{Abstractions,Core}/
├── Modules/<Module>/{Contracts,Core,Infrastructure}/
├── Platform/<TechnicalAdapter>/
├── Scripting/{Api,Core}/
├── Security/{Contracts,Core,Infrastructure}/
├── Administration/
├── Hosts/{ServerHost,SimulationHost,ToolHost}/
└── Tools/
tests/{Unit,Integration,Architecture,Negative,Concurrency,Replay,Reference,Performance,Stability}/
```

目录表示职责，按需要建立项目，不要求为每个小模块建立 Assembly。需要编译期隔离时 Contracts 独立；Core 内的 Domain/Application 可以按命名空间分区并用架构测试验证。Infrastructure 实现领域 Port；Host 只调用其公开注册入口，不能访问内部 SQL 或可变集合。

### 2.3 依赖规则

1. Kernel 仅依赖批准的基础库，不依赖 Runtime、Platform、Gameplay。
2. Runtime.Abstractions、StaticData.Abstractions 和基础 Entities.Contracts 依赖 Kernel；不依赖模块实现。
3. Runtime.Core 依赖自身 Abstractions、Kernel 和需要的基础实体契约。Entities.Core 可依赖 Runtime.Abstractions 的执行上下文契约，不引用 Runtime.Core，避免循环。
4. 领域 Contracts 优先仅依赖 Kernel 和窄契约；任何新增跨领域契约引用都进入显式白名单并检查无环。
5. 模块 Core 依赖本模块 Contracts、所需其他模块 Contracts，以及适用的 Runtime/StaticData Abstractions；不依赖任何其他 Core/Infrastructure 或具体 Platform Adapter。
6. 模块 Infrastructure 依赖本模块 Port 和具体技术 Adapter；Platform 通用库不反向依赖游戏模块。
7. Host 可以装配 Core 和 Infrastructure。其他模块不得依赖 Host。
8. 测试可装配真实实现，但生产项目不得因测试便利增加反向引用。`InternalsVisibleTo` 仅限明确的测试 Assembly，不向其他生产模块开放。

示例：Abilities 的伤害 Handler 调用 Combat.Contracts；Combat 不引用 Abilities。Combat 如需外部状态修饰，在自己拥有的窄 Port 上定义需求，由组合根适配到 Status 的公开查询。ItemCost 通过 Abilities 自有消费 Port 适配 Inventory，避免 Abilities↔Items 双向依赖。运行时调用可以往返，但编译期项目图必须无环。

静态数据基础库只认识通用 Schema/索引/生命周期，不引用 AbilityDefinition、ItemDefinition 等领域类型。领域定义与 Catalog 契约由所属模块拥有，加载器由 Adapter 装配。

### 2.4 架构自动检查

检查 Assembly 引用、类型/命名空间依赖、公共 API 泄漏、允许的注册入口和依赖环。对每条规则提供合法样本和故意违规样本，证明检查能失败。名称/关键词扫描只辅助人工审查，不能用禁止 `Boss` 等单词替代真实依赖分析，也不能单靠类名判断是否“巨型”。

P03 首批自动 Gate 已落地：生产项目显式清单位于 `eng/architecture/production-projects.json`；测试从真实 `.slnx/.csproj` 与 Release Assembly 读取 ProjectReference、公共 API 和 `InternalsVisibleTo`。P03-001～P03-008 的规则与正/负例位于 ArchitectureTests；Common/Utils/中心 Manager 风险使用跨模块 fan-in 触发显式职责审查，人工标准见 [架构职责聚集审查](reviews/ARCHITECTURE_RESPONSIBILITY_REVIEW.md)。

P10 进一步将技术模块 Options、启动期门禁、安全配置指纹与结构化日志留在 Platform；Security.Contracts 只定义窄身份绑定/命令授权边界及默认拒绝的安全基线。Platform 和 Security.Contracts 都仅引用 Kernel，不接收业务 SQL、玩法规则或 World 可变状态。敏感原文不得写入日志/诊断/公开指纹；真实生产认证适配器与 Host 接线必须在后续阶段验证，不能以默认拒绝 Port 的存在替代生产认证证据。

## 3. 标识、单位、时间和随机

- EntityId 标识运行时实体；ActorId 标识可持续关联的行动主体；ItemInstanceId 与 ItemDefinitionId 分离；WorldId 与 WorldEpoch 分离。实体复用时带 EntityGeneration，防止旧异步结果命中新实例。
- 世界坐标采用右手坐标，Z 轴向上；距离单位为米，角度为弧度，速度为米/秒。格式转换在 Adapter 完成。若项目已有其他约定，通过 ADR 一次性替换，不混用。
- 业务持续时间使用显式 Duration，模拟时间用整数 Tick/逻辑时间；墙上时间只用于审计、运维及明确的离线经过时间规则。持久化绝对时间采用 UTC，并在展示时转换。
- 逻辑定时器归 WorldScheduler/世界计时队列统一推进，不为每个实体创建系统 Timer。
- 随机算法标识、算法版本、Seed/状态必须可保存。Combat、Loot、AI 使用独立且稳定命名的随机流，避免新增一次 AI 抽样改变掉落结果。不得将运行时 HashCode 作为稳定种子。
- 拒绝 NaN、Infinity、负持续时间、溢出、非法概率与权重；规定舍入、夹紧和相等比较规则。浮点结果跨平台有差异时限定回放支持矩阵，不能用过大容差掩盖逻辑错误。

## 4. 状态所有权和一致性边界

领域所有权回答“谁定义并维护不变量”；执行上下文回答“何时、在哪里可以修改”。进入 World 的可变状态由所属 WorldExecutor 执行修改，但不因此全部变成 Runtime 领域的数据。

| 状态 | 唯一领域所有者 | 修改方式/约束 |
|---|---|---|
| 世界与实体注册/成员关系、Generation | Runtime.World | 生命周期命令；同一实体只能属于一个活动 World |
| Position/Orientation/Transform | Entities.Transform | 通过重定位协调操作修改；Movement 计算意图与轨迹，不另存位置真相 |
| 空间索引、可见集合 | Runtime.Spatial/Visibility | Transform 的派生索引；支持重建，不能反写位置真相 |
| LifeState、Health/Resources | Entities.Life/Resources | 窄写契约；Combat 编排伤害/死亡规则 |
| Base/DerivedStats、Modifier 句柄 | Entities.Stats | 所有变更经资源/属性契约，临时修饰可撤销 |
| Control/Tags、Ownership、Target | 对应 Entities 能力 | 来源句柄/关系校验；AI 不直接赋值 |
| 运动模式、路径、速度、Motion 状态 | Movement | 移动命令和 Tick；不复制 Transform |
| CombatRelation、Threat、攻击时钟 | Combat | 战斗命令/同步能力 |
| Cast、Status、Cooldown、Proc 状态 | Abilities 各子域 | 施放/状态事务与世界 Tick |
| 物品实例数量/实例属性 | Items | 通过受控物品变更计划 |
| 物品所在容器/槽位及归属索引 | Inventory | 唯一位置真相；Item 的 Owner/Slot 只能是派生只读视图 |
| 掉落实例与领取标记 | Loot | 与 Inventory 一起参与领取提交 |
| 经验/等级、目标进度/奖励领取 | Progression、Objectives | 各自契约；奖励领取账本防重复 |
| AI 运行状态、Scenario 阶段 | AI、Scenarios | 归属世界内执行 |
| 在线路由/会话绑定 | Presence | 独立串行协调上下文；世界状态仍通过世界命令修改 |
| 小组成员与 Leader | Group 聚合 | 独立串行协调上下文；各 Actor 只读其投影 |
| 静态定义/版本 | 所属领域 Catalog | 冻结快照；切换走显式生命周期 |

Presence/Group 等跨世界协调状态不是共享的自由可写字典。初始实现可在应用内使用独立串行执行器；通过带版本消息与 World 通信，不能持有跨世界实体引用。

### 4.1 重定位一致性

统一入口负责校验实体、WorldEpoch、Generation 和新坐标，准备 Transform 与 Spatial 更新，完成后再发布位置事实/可见性更新。单个操作不跨 `await`，不允许其他动作看到更新一半的状态。Spatial 失败时恢复旧状态或将世界置为故障并从可信状态重建；不能捕获异常后带着损坏索引继续运行。

### 4.2 跨模块原子操作

同一 World 串行并不自动提供事务：若先扣物品、再加奖励失败，仍会部分成功。需要原子的操作采用 Validate → Prepare → Commit → Publish：

1. 各所有者验证并返回变更计划，Prepare 不发布事件和外部副作用。
2. 校验计划使用的聚合版本、资源预留和执行上下文。
3. 在不让出执行权的提交段内应用已验证计划；提交段尽量无分配、无外部 I/O、无可执行脚本。
4. 预期业务错误在提交前返回。不可预期的部分提交异常触发世界/聚合故障隔离和恢复，不能假定一般异常捕获能够回滚。
5. 仅成功后发布事实；需要持久化的经济操作还须满足第 8 节。

变更计划不是允许任意模块写任意状态的通用 Patch。各模块只验证和应用自己拥有的状态；协调器只组合明确的一类业务操作。

## 5. World Actor 执行模型

### 5.1 生命周期

World：Created → Loading → Running → Draining → Stopped；不可恢复错误进入 Faulted。Entity：Created → Attached → Active → Removing → Detached/Disposed。跨世界变化必须走转移协议。

生命周期重复调用有明确结果；销毁撤销定时器、AI/脚本订阅、路径请求和 Capability 句柄。对象引用离开世界后失效；标识重新分配或 Generation 变化后旧请求不能生效。

### 5.2 Mailbox 信封和结果

外部命令包含：CommandId、目标 WorldId/Epoch、Actor/Entity 标识及 Generation（适用时）、主体身份、会话/授权版本、CorrelationId、截止时间、负载版本。World 接纳后分配递增序号；业务执行再次核验会话和实体归属，不能只信任网络层传入的 ActorId。

Mailbox 必须有容量、每 Tick 配额、公平策略、最大负载和指标。队满返回 Busy/RetryAfter；停止接纳返回 WorldUnavailable。不得静默丢弃物品/奖励命令；只允许对已声明可合并的移动意图按主体合并并记录最终接纳输入。DB 回执/控制消息使用预留容量或独立有界通道，避免普通流量阻塞恢复回执。

结果区分 Rejected、Accepted/Pending、Committed 和 Failed。Accepted 只说明排队，不说明业务成功。取消在提交前可阻止执行；提交后只取消等待，不能撤销已提交事实。超时结果未知时按 CommandId 查询或重试相同幂等键，不生成新经济请求。

### 5.3 执行上下文 Guard

所有世界写 API 验证 WorldId、Epoch、当前有效执行租约和非重入状态。不能只通过线程 ID 判断，也不能仅依赖可随异步流传播的 AsyncLocal；普通 `Task.Run` 即使继承上下文也没有合法执行权。世界轮转到另一线程仍保持同一逻辑所有权。

World Tick 和同步 Capability 不执行外部 I/O，不跨越 `await` 暴露可变状态。异步任务拿不可变请求快照，结果经 Mailbox 返回并再次验证 Epoch/Generation/请求版本；过期或重复结果被丢弃并计数。

### 5.4 Tick 顺序

初始顺序如下；模块通过注册阶段处理器加入，Runtime.Core 不引用具体 Gameplay 实现：

| 阶段 | 工作 |
|---|---|
| 1 接纳 | 按已分配序号处理有界输入、异步回执和到期控制请求 |
| 2 逻辑时间 | 推进一个固定步长，提取到期 Timer/Respawn/延迟动作 |
| 3 AI | 在只读状态上决策，产生可排序的动作请求 |
| 4 Movement | 推进 Motion，通过重定位入口更新 Transform/Spatial |
| 5 Ability/Combat/Status | 固定子阶段与排序执行施放、攻击和周期效果；规则版本记入回放 |
| 6 事实派发 | 按事务/事件序号派发成功事实；处理有界派生更新 |
| 7 生命周期/可见性 | 收尾移除、Spawn、Visibility Delta，禁止残留悬空引用 |
| 8 输出 | 生成不可变读快照、保存计划和观测数据 |

即时业务不变量（死亡、目标有效性、资源不足）必须在相应提交操作中成立，不能等到阶段 7 才保证“死者不能再攻击”。子阶段确切顺序在 P19/P32/P36/P41 固定并用边界案例验证。相同阶段用明确优先级、稳定实体 ID 和序号排序，禁止依赖 Dictionary/HashSet 遍历顺序。

逻辑固定步长由版本化配置提供。可用 50ms 作为初始测量候选，最终由负载验证确定。慢 Tick 限制追赶数量并报警；不为追赶随意扩大 dt 或跳过已经接纳的命令。单次步骤、事件和 Proc 有确定的数量/深度预算；墙上耗时只用于观测或故障策略，不能悄悄影响回放分支。

### 5.5 故障与关闭

业务拒绝返回结构化结果；隔离范围内的脚本异常记失败；核心不变量损坏将世界置为 Faulted，拒绝后续写并保留诊断。不能将“任意异常都捕获后继续 Tick”作为健壮性。

优雅关闭：停止接纳外部写 → 排空已接纳动作和关键 DB 回执 → 完成/标记 Pending 事务 → 保存及确认 → 注销 Presence → 释放 Adapter。设置总超时；超过时留下可恢复账本，不能声称所有写入已经完成。

### 5.6 跨世界转移

跨世界转移不作为基础交付的隐含要求；默认返回 TransferNotSupported，禁止通过复制实体或同时注册两个 World 实现“临时转移”。有明确需求启用时，按条件任务 P58.T 实现：

- TransferId 幂等；源世界冻结该实体并导出带版本快照，目的世界预留但不激活。
- 单独的归属目录/转移日志记录状态，用版本比较更新活动 Owner 与新的 fencing epoch。
- 目的世界只有在归属切换确认后才能激活，源世界失去写权并保留去重墓碑；未提交切换可取消预留并恢复源。
- 切换后失败走日志恢复/重试，不能重新开放旧 Owner。对冻结、预留、切换、确认各点注入崩溃，证明任何时刻最多一个有效写入 Owner。
- 转移期间针对旧路由的命令返回 Redirect/Retry；快照内不携带运行时引用。涉及持久化经济状态时使用同一持久归属协议。

此协议不要求引入分布式服务；应用内也需要明确交接语义。

## 6. 模块通信与事件

| 方式 | 用途 | 约束 |
|---|---|---|
| Query | 读取状态/视图 | World 内同步查询；外部读不可变快照并带版本/新鲜度 |
| Command | 请求改变状态 | 外部走 Mailbox；Tick 内可受控同步调用，不递归重入消息循环 |
| Domain Event | 已提交事实 | 明确发布时机、顺序、重试和去重，不隐式当事务协调器 |
| Runtime Capability | 高频同步读/写 | 所有者提供窄接口，写操作验证有效世界上下文 |

事件信封包含 EventId、WorldId/Epoch、Tick、事务/因果 ID、稳定序号和 SchemaVersion。World 内事件按稳定顺序处理，处理器产生的派生事件进入下一轮有限队列，不能无限递归。涉及玩法状态的消费者异常不允许静默吞掉；应重试、标记聚合失败或 Faulted，策略按事件类别明确。

持久可靠通知通过与领域提交同事务的 Outbox 保存；消费者使用持久 Inbox/事件游标去重。传输按至少一次建模，不承诺整个系统“恰好一次”。日志和指标可丢失时也须有明确策略，不能让遥测失败阻塞世界。

Kill/Collect/Ability 目标消费公开事实；Combat、Items 不反向知道 Objectives。事件 ID 去重窗口的清理必须与检查点/保留期限协同，否则旧事件可能再次发奖。Collect 目标要区分“当前持有数量”与“累计获得数量”，使用不同规则，不能一个计数器混用。

## 7. 静态数据与热重载

生命周期：Load → Parse → Validate → BuildIndex → Freeze → Publish。校验重复 ID、缺引用、循环、单位/范围、未知 Handler/脚本绑定及版本。多个相关 Catalog 形成同一版本包，跨 Catalog 引用统一校验，不逐个替换制造混合版本。

基础库提供通用流程；Ability/Item/Actor/Objective/Loot/World Catalog 由领域拥有。JSON 作为早期最小数据入口，SQL 来源按 P15.S 补齐；CSV 等额外格式按真实需求扩展，不要求首版全做。

Reload 在后台构建新冻结快照，在 Tick 边界原子切换。每个 Cast、Status、Item/Scenario 必须声明：固定旧版本至结束、明确迁移，或拒绝运行期切换。默认运行中实例固定创建时的定义版本；长期存在对象需要迁移计划，不能无限累积旧 Catalog。校验/迁移失败保持旧版本，记录差异与失败原因。

回放和存档记录 Catalog 版本及内容 Hash，并能取得对应版本。移除正在引用的模板/效果必须被拒绝或提供显式迁移，不能变成空引用后继续运行。

## 8. Persistence、经济操作与恢复

### 8.1 Port 与存储模型

Repository Port 属领域，DB Row/ORM 模型属于该模块 Infrastructure。Platform.Persistence 仅提供连接、参数化命令、事务、迁移和超时等技术能力。选定一种真实关系数据库作为首个验收目标，P00 记录决定，P11 验证其实际隔离级别及版本；本文不虚构项目已有数据库。

聚合快照带 AggregateId、Owner/Epoch、ExpectedVersion、新版本、SchemaVersion、CatalogVersion、CommandId。持久化采用乐观并发和版本比较；旧 SaveSnapshot 不能覆盖更新版本。重复或乱序 DB 回执不得把内存版本回退。

### 8.2 两类保存与确认点

| 类别 | 示例 | 确认点/恢复要求 |
|---|---|---|
| 可检查点状态 | AI 中间状态、可重建空间索引、允许窗口内回滚的位置 | 允许异步快照；明确 RPO；可合并保存但不能覆盖新版本 |
| 经济/权益状态 | 物品领取、消耗、奖励、交易式转移 | 成功确认前完成持久事务与幂等账本；不以排队代替成功 |

非关键状态可以从最近检查点恢复，但不能让旧位置快照顺带覆盖已经确认的物品奖励。快照必须声明包含的状态域及各自版本，禁止“整个人物行覆盖所有字段”。

经济操作推荐流程：

1. World 内验证并建立不可变计划，以 CommandId/业务键登记 Pending，预留涉及的资源/槽位；仅冻结相冲突的聚合动作，不阻塞整个 Tick。
2. DB Adapter 在一个事务中完成各领域持久变更、版本比较、幂等结果和 Outbox。协调范围必须明确，不能让各 Repository 自行打开连接后声称是同一个事务。
3. DB 结果经 Mailbox 回来，World 依据 Pending ID、Epoch 和版本应用已提交计划，之后返回 Committed。事务提交后的内存应用不能再执行可失败的业务校验。
4. DB 提交前失败可释放预留；取消等待不能撤销已提交事务。提交成功但回执丢失时查询账本，不能再次生成新掉落/奖励。
5. DB 已提交而内存应用前崩溃，重启从持久状态/账本恢复；内存应用异常时隔离聚合/World 并恢复，不能重复发放。

同库事务协调器只协调当前业务的持久计划，不承担所有领域查询。跨库或跨 Owner 操作必须单独设计可恢复流程和补偿，不能承诺一般意义上的原子回滚。初始实现只接纳已支持的同 Owner、同库范围，超范围操作明确拒绝。

### 8.3 Inventory、Loot 和 Reward

- Inventory 负责位置唯一性、容器容量、嵌套无环、叠加上限和数量守恒；Create/Destroy 需记录原因，不与 Move/Split/Merge 混淆。
- MutationSet 先完整验证，提交所有变更或无变更；配套保存位置约束和 ItemInstanceId 唯一性。
- 掉落首次生成后保存 LootInstance、随机结果和权属；重试领取不重新抽奖。幂等键绑定 LootInstanceId、LootEntryId、领取主体，不能仅依赖调用方随机生成的 CommandId。
- Reward 使用 ObjectiveInstanceId + RewardVersion + 领取主体作为业务幂等键。先确定完整奖励，再与领取标记共同提交；重复完成或重复事件不重复发奖。
- “补偿”必须写清可见窗口、补偿失败重试及最终对账，不能用“可补偿”代替不复制物品的证明。

### 8.4 迁移、备份和恢复

数据库和快照 Schema 有版本；测试旧版本读入/迁移及不支持版本的拒绝行为。大版本迁移采用可回退方案或恢复旧备份，不能假定所有 DDL 能事务回滚。备份保存恢复所需 Catalog、幂等账本、Outbox/Inbox 和 Schema 信息。

恢复按持久事实重建派生索引，检查 Pending 事务与归属，确认对账后进入 Running。RPO/RTO、保留期限、迁移和恢复演练按 P66 的部署配置验收。

## 9. 各能力域的职责与关键规则

| 能力 | 子模块和规则 | 关键失败/边界 |
|---|---|---|
| Kernel | StrongIds、Geometry、Time、Random、Result、Command/Query/Event 值契约；Handler 注册实现放应用/运行设施 | ID 混用、溢出、非有限数、时间回退、随机算法版本变化 |
| Platform | 模块 Options、结构化日志、Metrics、Diagnostics、WorkQueue、DB 技术、Transport、Serialization | 队列有界、超时、连接/帧/解压后尺寸限制、资源释放；不含业务 SQL |
| Runtime | 生命周期、Tick、Scheduler、WorldState、Spatial、Grid、Visibility、Spawn/Respawn、Terrain/Collision/Navigation Port | 重复 Spawn、卸载后 Timer、跨 Cell 一致性、过期寻路结果 |
| Entities | 薄宿主和小能力；Stats 固定 Modifier 顺序及舍入；Health 转换与 Life 不变量协同 | 临时修饰来源句柄、重复撤销、最大值变化、死亡后资源操作 |
| Movement | Speed、Input、Validation、Spline；Point/Random/Chase/Follow/Flee/Home/Waypoint、Jump/Knockback | 明确中断/优先级；寻路异步结果含请求版本；无路/超时返回明确状态 |
| Combat | Engage/Disengage、AttackTimer、AutoAttack、Hit、Damage/Heal/Mitigation/Absorb、Threat/Taunt/Redirect、Death | 伤害/治疗有效量与过量、目标移除、同时致死、稳定命中策略 |
| Abilities | Definition/Relationships、Cast、Validation、Targeting、Cost/Cooldown、EffectRegistry | 预留/扣费时点、取消/中断退款、目标失效、未知效果拒绝 |
| Status/Proc | Application、Duration/Stack/Charges、Modifier、Periodic、RemovalMode、Immunity/Dispel/Interrupt | 周期与到期同 Tick 的顺序；来源/层数策略；链深度和事件数上限 |
| Items | Definition/Instance、Container/Inventory/MutationSet、Equipment、Modifier、Durability/Charges、Loot | 数量守恒、槽位唯一、非法嵌套、重复领取、持久提交后断线 |
| Progression/Objectives | 可配置经验/技能曲线、目标 Handler、完成判定、Reward | 一次跨多等级、经验溢出、重复/乱序事件、恢复后不重复奖励 |
| AI | IAgentAI/Context、Scheduler、Perception/TargetSelection、Decision、Movement/Combat/Ability Bridge、Evade/Home、数据驱动解释器 | 无实体私有引用；Idle 预算；未知 Action 拒绝；确定性动作配额 |
| Interaction | 身份绑定、Presence、距离/状态/权限验证、Group 聚合 | 重连旧会话失效、Leader 离开、跨世界路由版本、转移期间重复输入 |
| Scenarios | Definition/Instance、ParticipantSet、Stage、ResetPolicy、Save、Events | 重复重置、清场 Timer、保存版本、参与者断线/退出 |
| Scripting | Api、Registration、Events/Hooks、Commands、ReadModels、Versioning、Budget | Hook 顺序、异常副作用、过期版本、能力边界和可信度 |
| Security/Admin | AuthPort、RBAC、RateLimit、Violation、Policy、Audit、管理命令、ServerControl、受控 Reload | 默认拒绝、命令执行时复验、审计持久失败、管理入口同样走正式业务链 |
| Hosts/Tools | 薄组合根、SimulationHost、DataValidator、Migration/Snapshot/Replay/Reference/Benchmark/Fault 工具 | 不含业务规则；不依赖机器绝对路径；Replay 不直接改状态 |

### 9.1 战斗、死亡与施放的补充语义

Damage：Base → AttackerModifiers → TargetModifiers → Mitigation → Absorb → Final → HealthApply。Healing 分开建模，区分请求量、有效量、过量和吸收。所有阶段顺序、夹紧和舍入有测试；公式是可替换策略，不内置 WoW 职业/SpellId 特判。

Health 从正数到零时，在同一提交操作中完成 LifeState 转换和防止重复死亡；Combat 编排关系/Threat 清理，Abilities/AI 等通过公开生命周期契约注册清理。P34 先覆盖已有模块，后续模块必须补接清理并重跑死亡场景。EntityKilled 仅发布一次，不靠可能延迟或失败的事件消费者维持核心死亡不变量。

Cast 状态：Requested → Validated/Reserved → Casting → Resolving → Completed；可在允许阶段进入 Rejected/Interrupted/Cancelled/Failed。P36 先定义状态语义，P38 明确资源和冷却的预留、扣除、退款时点。包含多个 Effect 的技能必须声明“整体原子”或“按顺序部分成功”；默认运行期效果按顺序结算，已提交伤害不因后续无目标而自动回滚。需要整体原子时只能使用支持准备/提交的效果集合。

Status 规定同来源/异来源、Refresh/Replace/Stack、Charge 消耗及 RemovalMode。Modifier 是来源句柄驱动，移除后恢复的是扣除该来源后的重新计算值，不承诺属性数值总回到旧快照（其他来源可能已变化）。

## 10. 脚本、AI 与安全边界

核心脚本 API 返回不可变读模型，写操作转正式命令或受控同步请求。IGame/IWorld/IActor/IAbility/IItem/IScenario API 按真实调用方逐步增加，不建立无需求的巨型门面。

初始 C# 插件仅接受受信任代码。接口限制和 AssemblyLoadContext 不是安全沙箱；取消令牌与 Stopwatch 不能强制终止同步死循环。不宣称一个耗时预算即可保证任意脚本不阻塞 Tick。

数据驱动 AI/脚本解释器使用确定的指令、深度、事件和动作配额。受信任 C# Hook 要求短、同步、无阻塞 I/O；异常发生前的写操作通过准备计划隔离，提交后回调异常不逆转已提交事实。慢 Hook 记录与停用策略明确；对恶意或不可信代码，需要独立隔离执行环境与消息协议，超出初始内嵌插件范围。

外部入口启用认证与 Actor 绑定、命令级授权、长度/速率/配额限制；不得在 P60 之前让未验证的真实网络入口直接执行世界写。P13 先交付 Loopback 与传输限制，P18/P27/P61 逐步接入授权，P60 汇总完整策略。敏感日志脱敏，不记录 Token、连接串或完整凭证；Metrics 避免用无限 EntityId 等标签造成高基数。

## 11. 回放、参考验证与性能

### 11.1 回放输入

回放清单记录：构建/规则版本、配置 Hash、Schema/Catalog 版本、初始快照、随机流状态、接纳后命令序列及 Tick、Timer/外部结果、跨世界交接顺序和支持平台。仅记录相同 Seed 或网络原始到达顺序不够。

Replay 走正常命令和 Runtime 入口；日志中的领域事件用于验证输出，不能同时把输入命令和其已产生事件再各执行一遍。离线回放使用隔离存储或记录的 Adapter 结果，禁止再次发出生产奖励/外部副作用。

状态 Hash 排除墙上时间、日志、对象地址及可重建缓存，使用规范化字段、稳定集合排序和明确数值编码。比较最终状态、事件序列和关键事务结果，首次分歧定位到 Tick/CommandId。跨世界回放先支持各世界独立，再加入确定的交接序列，不承诺任意并发调度天然可重放。

### 11.2 AC 参考证据

每个对照案例记录 Commit/File/Function、输入/前置条件、提取方式、预期、差异及容差依据。源码分析、固定向量和实际运行对照明确区分。选择可泛化机制，不比较 WoW Packet 或把 AC 的游戏公式变成不可替换规则。

### 11.3 性能与稳定性

WorldTick、Spatial、Movement、Combat、Abilities、Inventory 和 AI 在首次集成时建立基线。记录硬件/CPU 限制、系统、SDK/GC、构建模式、数据规模、活跃比例、命令速率、Seed、预热、重复次数、Tick p50/p95/p99、分配、GC、队列峰值、内存平台期和错误数。

固定负载与阈值在优化前冻结；共享 CI 的微基准波动不直接等同退化，使用专用环境或重复确认。性能目标、稳定性时长和 RPO/RTO 按路线图配置矩阵落实，不把未经测量的并发人数写成保证。

## 12. 验收矩阵和迁移顺序

| 检查域 | 必须证明 | 对应阶段 |
|---|---|---|
| 范围与来源 | 通用能力、固定 AC 基线、来源可追溯 | P00、P63、P67 |
| 依赖 | 引用无环、内部不泄漏、负例确实失败 | P02–P04、持续回归 |
| 单写与生命周期 | 非法上下文拒绝、跨世界无共享可变实体、旧回执失效 | P17–P24、P58.T |
| 数据与重载 | 同版本只读、原子切换、旧实例政策、回放版本可取 | P15–P16 |
| 玩法规则 | 真实调用闭环、边界和死亡清理、无专有特判 | P25–P59 |
| 经济一致性 | 失败不复制/丢失、持久确认点、去重和崩溃恢复 | P11、P43–P51、P65 |
| 脚本与安全 | 可信度边界、预算能力准确、授权不能绕过 | P13、P18、P56–P61 |
| 可复现 | 输入/版本/随机/外部结果齐全，首次分歧可定位 | P19、P62、持续回归 |
| 运维 | 指标、过载、关闭、迁移和备份恢复 | P14、P64–P66 |
| 可维护性 | 中文文档注释、阶段证据、恢复索引准确 | 所有阶段 |

本版落库时先同步契约依赖图、所有权表及阶段依赖；已有代码须核查 Transform 双写、Item Owner/Slot 重复真相、后台直接回写、异步保存覆盖和确认时机。再按影响添加回归测试并迁移。本文没有检查实际代码，不能把上述风险认定为仓库已存在的缺陷。
