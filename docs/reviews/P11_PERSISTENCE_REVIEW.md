# P11 Persistence 独立代码审查

- 审查阶段：ACCEPTED（P11 目标 C）
- 审查代码基线：0a092260c20f826b5126e2e83422b125b0b27cc5
- 实际测试证据：GitHub Actions run #72（SUCCESS）、独立 REVIEW HEAD run #73（SUCCESS）
- 时间：2026-11-16 +08:00
- 目标：不降低 P03/P04/Analyzer 或 PostgreSQL C 出口。

## 逐项审查结论

| 维度 | 审查内容 | 结论 |
|---|---|---|
| 构建 | Solution、生产 Assembly、P03 manifest、中央 NuGet、项目锁文件、文档质量 | PASS |
| 依赖 | Platform.Persistence 无具体 Npgsql 引用；Npgsql 只在 Integration Probe | PASS |
| 数据所有权 | 技术层不出现 Game/Item/Inventory/World 业务 SQL；仅存在技术迁移版本账本 | PASS |
| SQL 安全 | DbParameter 绑定，安全参数名、固定 DbType，null 转 DBNull；业务 SQL 文本所有者为调用模块 | PASS（边界明确） |
| 事务 | 提交中结果未知不变成失败/成功；重复提交被生命周期拒绝；回滚和释放 | PASS |
| Migration | 事务锁、SQL SHA-256、连续版本、拒绝冲突与降级；失败 DML 和账本原子回滚 | PASS |
| Bulk | 入库前有界命令和参数预算；失败整事务回滚，调用方单独确认提交 | PASS |
| DB Integration | 真实 PostgreSQL 18，隔离、乐观写、幂等、超时、取消、重连、Pool=1 | PASS |
| 回执丢失 | Commit 已在 PG 实际落库但测试回执丢失时 Unknown；通过独立事务消歧；回滚确认丢失保留 Unknown | PASS |
| 文档复现 | main SHA、run URL、依赖版本、PG digest、真实探针与 artifact | PASS |

## 对风险与后续子任务的明确界限

1. Npgsql 网络中断实际断线没有在本次验证。注入点模拟的是数据库事务已成功、业务端未获回执；业务调用方必须通过自己的幂等业务键消歧，不允许直接重试写入。
2. Migration 脚本必须来自经过审查的模块 Infrastructure；本工具不能自动证明任意模块 SQL 安全或可在事务中执行，禁止自动执行数据破坏性回退。
3. BoundedBulkLoader 不是 COPY，也不宣称达到 P64 性能预算；未来发现性能瓶颈时可在隔离的 Adapter 中扩展 COPY 实现。
4. P65.F 仅覆盖提交/回滚确认丢失的初始注入机制。业务接纳输入、DB 写前后、消息回执与内存应用、崩溃重启的全链路故障矩阵由后续世界和经济阶段累积，P65 父阶段没有提前完成。
5. P11 没有 ServerHost、生产连接配置分发、备份恢复或真实游戏状态集成；这些不是当前目标 C 的必交付模块。
6. 当前没有自动化数据库回滚恢复脚本；迁移异常必须阻止启动并走显式恢复流程。

## 独立 REVIEW 判定

实现范围与路线图 P11 技术层目标一致；实际 PostgreSQL、事务和恢复语义有 CI 证据；自审未发现 P11 必需未解决缺陷（0）。独立 REVIEW HEAD 270022e 的 run #73 两个 Job 全绿，P11 技术层目标 C 的必需未解决项 0，正式转入 ACCEPTED。本次验收提交 HEAD 的最终 CI 仍需再确认。该结论不取代后续业务层、性能或安全验收。
