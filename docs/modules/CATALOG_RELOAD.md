# P16 — Catalog 整包校验、冻结与受控 Reload

## 状态与边界

P16.F 由 `CatalogPackageBuilder` 负责跨 Catalog 规则、全包版本和冻结；P16.R 由 `CatalogReloadCoordinator` 负责已冻结包的异步准备、活动定义政策、受限版本留存和逻辑 Tick 边界原子替换。实现复用现有 `StaticData.Core`，没有新生产程序集、NuGet 包和项目反向依赖；基础模块绝不定义 Ability/Item/World/Entity 类型，也没有世界可变写 API。

此时 **P18 WorldExecutor 与 P19 真实世界 Tick 未交付**。P16 提供了可测试、每个执行域单独拥有的 `CaptureTick/CommitAtBoundary` 技术 API，但不能声称真实 Cast/Item World Host 已经接线或拥有真正领域迁移。

## P16.F：引用规则与整包 Freeze

- 领域通过 `CatalogReferenceRule(sourceCatalog,sourceField,targetCatalog)` 描述强引用；源字段必须为 `StaticFieldType.Text`，值是目标 Catalog 的记录 ID。可选字段未出现时不建立边，出现则必须能找到目标 ID；领域负责全部枚举应验证的引用关系，本库不猜测字段语义。
- `CatalogPackageBuilder.Build(version,catalogs,rules)` 要求 version 正数、每个已冻结 P15 Catalog 的 DataVersion 与整包版本一致；拒绝同名 Catalog、缺少被引用的 Catalog、规则重复/字段错误、目标 ID 缺失、引用环与超预算。**不要求不同 Catalog 的记录 ID 全局唯一**，唯一键是 (Catalog, ID)，各个 P15 Catalog 自行保证内部 ID 唯一。
- 目录名、规则与记录均采用确定性 Ordinal 顺序。建邻接图后采用迭代 DFS（不递归）检测自环与跨 Catalog 环；长达 10,000 行的链不会递归耗尽调用栈。错误采用 `CatalogPackageException` 的 Fault/Catalog/RowIndex/Field，只包含安全技术标识，禁止携带原始定义内容。
- 整包硬限制：最多 **16** 个 Catalog、**64** 条规则、**131,072** 条引用边；单 Catalog 仍受 P15 的 10,000 行、64 字段和输入字节预算约束。
- 全包通过验证后才返回 `CatalogPackageSnapshot`：只读目录序列、不可变索引、版本、确定性 SHA-256。包 Hash 使用长度前缀 UTF-8 编码，包含规范化的目录名、P15 每目录内容 Hash、整包版本与全部排序规则；不同输入/声明顺序不会产生不同包 Hash。
- 与 P15 的关系是 **Load（JSON/SQL）→ P15 Parse/行验证/冻结 → P16 关联验证/整包 Freeze → Stage → Tick 边界 Publish**。不在读取单个 Catalog 后提前更新任一运行时可见目录。

## P16.R：原子性和并发规则

- 每个执行域独立创建 `CatalogReloadCoordinator(initialPackage,maximumHistory)`，历史容量可配置 **2–16** 个整包（包含当前及 pin 的旧包）。
- `CaptureTick(tick)` 必须使用正数、严格递增的逻辑 Tick ID。同一 Tick 可持有多个视图，每个 `CatalogTickView.Package` 均固定整包版本；不同 Tick 不能同时进入这个协调器，最后一个同 Tick 视图 Dispose 时才视为 Tick 完结。**取两个 Catalog 必须从同一个 Tick view 的 Package 查询**，不得通过多次独立的 `Current` 调用建立一致性假设。
- `PrepareAsync(loader)` 的加载/数据库 I/O 在外部完成，成功后 `Stage` 只处理已校验的候选；同时只能有一个 pending Ticket，版本必须大于当前版本。准备失败、取消、重复 Stage 和 `Abandon` 都不改变可见 `Current`。
- `CommitAtBoundary(completedTick,ticket)` 仅接受尚未提交且完全关闭的 Tick、当前唯一票据和真正前进的整包版本；在单个 lock 内复查活动定义与留存容量并原子替换**整个** Bundle 引用，失败保持旧包/票据。下一逻辑 Tick 的所有视图使用新包，之前 Tick view 仍持有旧包。
- 整包切换使用内存同步语义，不提供进程间一致性、发布订阅或真实 World 线程身份鉴别。真实 Tick 边界权限必须由后续 P18/P19 的 World Owner 接入。
- `FindRetained(version,hash)` 与 `RetainedSnapshots()` 提供有限内存旧版回放；历史达到限制时只回收无活动 pin 的较旧包，否则拒绝 Reload 为 `RetentionFull`，不静默删除仍在使用的定义。长期重放的持久化版本归档还需后续 P62/存储支持。

## 活动 Cast/Item 的中立版本策略

领域未来为每个正在使用的定义调用 `RegisterActiveDefinition(usageId,catalog,recordId,policy)`，实例终结时 Dispose 对应 `ActiveDefinitionHandle`。本库仅持有冻结目录引用和一条受限登记事实，不持有 Cast/Item 的可变状态；最多跟踪 **4096** 条活动使用（避免无界 pin）。

- `RejectChange`：原始冻结定义对应的行如果被修改/删除，拒绝新包；保持未变定义允许新增其他条目。
- `PinOldVersion`：允许新包删除/修改定义，但在注册结束前保留原冻结包；容量不足时拒绝，而不是无限增长旧版本。活动实例继续使用其原先被 pin 的定义，不会自动迁移。
- `RequireMigration`：检测到修改/删除时返回明确 `MigrationRequired`。**本阶段不提供虚假的迁移成功回执**；需要后续 Cast/Item Owner 在真实 World 执行上下文完成领域状态迁移之后，才可设计可信迁移授权契约。没有 Owner 接线时只能拒绝，不能让旧引用悬空。
- `Stage` 与 `CommitAtBoundary` 都复查策略；在两者之间新增的活动使用不能绕过实际 Commit 门禁。句柄释放幂等，外部必须保障生命周期与异常路径回收。

## 测试与保留工作

- Unit 验证：相同输入顺序不同包 Hash 一致、重复目录/版本/规则/字段、缺目录/目标、跨目录环/自环、10k 深链、只读包与有限边；不同 Tick 并发视图不混版本、错边界/旧 Ticket、准备取消与失败保持旧包；活动策略、版本 pin、有限历史、Commit 时二次检查。
- PostgreSQL 18 Integration（复用 `staticdata-postgres` Job）：P15 SQL 真实来源构造跨目录包、异步 Stage、同 Tick 双视图、已完成边界整包切换、旧包留存；故意插入目标缺失记录，证明候选在 Validate 阶段拒绝且运行中包保持旧版本。不会在 SQL 事务中修改游戏状态。
- 这些只验证技术契约；P16 不证明全生产 Host、真实世界 Tick、业务 Cast/Item 实例迁移或跨进程 Catalog 原子部署。性能、长稳和持久归档分别在 P64/P66/P62 后续验收。
