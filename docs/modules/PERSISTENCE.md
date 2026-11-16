# P11 — Platform Persistence 技术契约

状态：P11 REVIEW，代码审查基线 0a092260，真实 PostgreSQL 18 run #72 SUCCESS。本文件只描述技术层已证实的范围，P11 最终 ACCEPTED 仍待独立 HEAD 检查。

## 所有权与依赖

- Platform.Persistence 只依赖 BCL ADO.NET 抽象，不直接依赖 Npgsql，且不引用任何游戏业务模块或游戏表。具体 PostgreSQL Provider 在 Composition/Integration 层选择。
- 模块 Infrastructure 拥有 SQL 模板、持久化业务表及其业务幂等键；技术层只拥有公开技术迁移账本 public.lotd_platform_migrations 和中立执行语义。
- Npgsql 10.0.3 只在 Integration Probe，使用版本锁文件；PostgreSQL 18 CI service 固定 digest sha256:2ea8630df176ad090ec92136dee850397c5af8781e4bcf2e4162593691c61bbc。

## 基础 API 与一致性语义

1. DatabaseConnectionFactory：可注入 DbProviderFactory，异常路径释放连接；BeginTransactionAsync 强制明确 IsolationLevel；调用方以 await using 释放。连接串是秘密，不得进入日志或错误报告。
2. SqlParameterValue / ParameterizedCommand：受检参数名、DbType、重复名拒绝，null 绑定 DBNull，命令文本与 DbParameter 值分离；参数集合冻结为只读列表，但不会深复制参数值对象。SQL 文本只能由可信模块 Infrastructure 提供，不允许在调用方拼接不可信输入。
3. PersistenceTransaction：Active、Committed、RolledBack、OutcomeUnknown、Disposed。仅获得 Provider 提交确认后返回 Committed；提交发出后的 DB 故障/取消/超时返回 Unknown；未知不能直接重试；Active 退出自动回滚，始终释放连接。
4. PostgreSqlMigrationRunner：对事务级 advisory lock 下的连续迁移清单先查账本、比对 checksum，逐步执行 DDL/DML 并同事务写入账本。已执行的版本不可改写，不允许自动降级；故障时回滚。调用方提供静态、审计过、可在事务中运行的 SQL。
5. BoundedBulkLoader：输入为预构造 IReadOnlyList 参数化命令；执行前校验正的 maximumCommands 和 maximumParameters，拒绝超额；执行失败或取消则回滚活跃事务；成功返回命令数但调用方仍需自己 Commit。不是 PostgreSQL COPY，也没有宣称高性能。
6. P65.F 初始注入：BeforeCommit / AfterCommit / AfterRollback。测试显示实际提交后模拟回执超时 -> CommitOutcome.Unknown；新连接查幂等账本确认已提交并禁止重复写。模拟回滚回执丢失也保留 OutcomeUnknown。生产默认路径无故障注入。

## 真实测试与出口

- run #72：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43824023757
- Npgsql 10.0.3、PostgreSQL 18、Ubuntu Linux x64、SDK 10.0.404、locked-mode restore。
- 回滚无残留、未提交作用域回滚、ReadCommitted/Serializable、版本写拒绝旧版本、幂等请求去重均真实 PASS。
- Migration 重放/历史 checksum 变更拒绝/错误迁移整事务回滚；Bulk 命令/参数预算、重复键整批回滚均真实 PASS。
- 提交前取消、statement_timeout、提交成功后注入确认丢失 Unknown、回滚确认丢失、幂等重查和单连接池释放均真实 PASS。
- P11 证据见 [P11 阶段档案](../progress/P11.md) 和 [独立代码审查](../reviews/P11_PERSISTENCE_REVIEW.md)。

## 明确保留的风险

原始 Provider 异常可能携带秘密，实际日志系统不得直接记录 ToString/连接串/参数原文；技术层没有生产业务日志。当前提交回执丢失是实际 PostgreSQL 事务后注入的模拟故障，不是物理断网测试。P65 完整故障矩阵、生产 ServerHost、业务持久化、COPY 性能与 P64 预算均未完成。
