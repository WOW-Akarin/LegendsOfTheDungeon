# 当前进度

更新时间：2026-11-16 +08:00
分支：main，用户要求直接更新，不创建分支
阶段：P15 — Static Data Schema / Loader / REVIEW / C
已验收：P00–P14；下一阶段：P16 — Catalog Validator / Freeze / Reload（NOT_STARTED）
P15 起始 HEAD：fa7bb44a5bffc8b03eac406c514a6b46ff4a5c2a
P15 最终实现 HEAD：42b06794e8f852ed599087b74902cd0987d71cc0

## 已交付

- 四个独立生产 Assembly：StaticData.Abstractions、StaticData.Core、StaticData.Json（P15.J）、StaticData.Sql（P15.S）。
- 领域拥有的通用 Schema、类型化规范值/内容版本，严格 JSON 来源、真实 PostgreSQL 18 SQL 来源；同一数据统一 Hash/冻结只读索引。
- JSON/SQL 来源错误明确 `Fault/SourceName/RowIndex/FieldName`，不得含秘密；固定容量和最大记录数；SQL RepeatableRead + P11 真正参数化命令。
- P03 Manifest、.slnx、NuGet 锁图与 `staticdata-postgres` 新 CI Job；不引用 Ability/Item、DBC、World 状态。
- [完整阶段证据](progress/P15.md)、[通用模块契约](modules/STATIC_DATA.md)、[四视角 REVIEW](reviews/P15_STATIC_DATA_REVIEW.md)。

## 实际失败与完整验证

- CI #121–#127 留有 Unit 项目图、CA1720/CA1512/CA1859、CS1503 与 P03-006 失败并已修复，未弱化 Gate。
- 实现 #128/#129/#130/#131 连续 SUCCESS。
- 最终实现 #131：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43847291783 — locked restore/Release；Unit **174/174**、Architecture **16/16**；真实 PostgreSQL 18 P15 **7 项探针**；既有 P11 Persistence、P13 TCP Loopback、P14 Diagnostics 三项 Integration 和全质量 Gate 全部 PASS。
- 四视角实现必需未解决问题 0，当前 REVIEW。独立 REVIEW HEAD 与正式 ACCEPTED HEAD CI 尚未执行，不能提前宣称 P15 已验收。

## 下一安全动作

1. 运行 REVIEW HEAD 五 Job CI，全绿后正式 ACCEPTED。
2. 正式 ACCEPTED HEAD 再执行五 Job CI，归档最终证据。
3. P16 的跨 Catalog 引用/循环验证、热重载、Tick 边界原子切换当前尚未开始；P64 性能与 P66 长稳仍 NOT_RUN。

## 重要限制

P15 的静态数据只读索引已冻结，但未实现 P16 的整包原子替换和版本归档；只有技术示例 Schema/PG 中立表，未接真实 Ability/Item 领域数据。真实生产 DDL 必须经后续模块迁移审查。
