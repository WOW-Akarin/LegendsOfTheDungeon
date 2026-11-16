# P11 — Platform Persistence 契约（进行中）

## 范围与依赖

P11 直接依赖 P10（已 ACCEPTED），目标完成度 C：在真实 PostgreSQL 18 上提供数据库基础技术设施。新项目 LegendsOfTheDungeon.Platform.Persistence 使用 ADO.NET 抽象，不拥有游戏业务表、领域 SQL 或游戏状态；业务 SQL 必须位于各业务模块 Infrastructure。已新增专用 Npgsql 10.0.3 Integration Probe（非生产依赖），在 postgres:18 service 实测；真实 NuGet lock 已从 runner 生成并入库。PostgreSQL image digest 尚待固定。

## 已实现基础契约

- DatabaseConnectionFactory：通过注入的 DbProviderFactory 创建连接，打开失败主动释放；连接串仅在私有字段中保存，不写入日志或失败报告。
- SqlParameterValue / ParameterizedCommand：固定命令和参数快照，校验参数名与类型，拒绝重复名；以 DbParameter 真正绑定值，null 转为 DBNull.Value。
- SQL 文本只能由模块 Infrastructure 静态持有。此 API 不能在运行时自动识别调用方是否在 SQL 字符串中拼接了不可信值，后续需要审查规则和使用方测试。
- PersistenceTransaction：显式隔离级别；Active 状态下离开作用域尝试回滚；提交成功才返回 Committed。提交过程中发生 DbException、TimeoutException 或 OperationCanceledException 则返回 Unknown，禁止将结果视为已回滚并盲目重试。
- 上述 API 已在 GitHub Actions run #67 使用真实 PostgreSQL 18 + Npgsql 验证连接、Rollback、隔离、条件版本写、幂等键、取消与 statement_timeout。但 Migration/BulkLoader、提交 Unknown 故障点及 Provider 失联测试未完成，不意味着 P11 已达到 C。

## 目标完成度 C 的真实出口

1. 在 PostgreSQL 18 上实际建立连接、记录 server_version 及镜像 digest，验证连接释放和 Provider 故障。
2. 完成 Migration：模块贡献有序、不可变版本清单；技术层只管理版本账本、互斥及失败中止；重复执行不得破坏数据；不可自动对已上线破坏性迁移做假回滚。
3. 完成 BulkLoader：限定批次、参数上限、取消和事务归属；写入和事务提交的确认要分开。
4. 在真实选定 DB 上验证事务 Rollback 无残留、隔离级别、乐观条件版本写入、幂等账本和重复请求。
5. 在提交前取消、提交中超时、回执丢失、连接断开等情形区分失败与 Unknown，演示查询账本后的消歧行为。
6. 启动 P65.F：按 prepare、数据库变更后、commit 前、commit 回执后、内存应用前设置可控故障点，复用后续经济事务。

## 安全和禁止项

- Platform.Persistence 不创建或解释任何游戏业务表。
- 连接串、认证信息、参数原值和 Provider 原始异常文本不能进入日志及持久构建 artifact。
- 不能用内存替身、静态推断或仅编译成功替代真实 PostgreSQL 集成。
- Unknown 状态必须通过业务幂等键或版本查询消歧；不能默认重试、推断回滚或报告成功。
