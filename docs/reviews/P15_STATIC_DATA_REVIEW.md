# P15 — JSON / SQL Static Catalog 独立四视角审查

- 状态：ACCEPTED
- 实现基线：42b06794e8f852ed599087b74902cd0987d71cc0
- 最终实现验证：run #131 SUCCESS（五 Job，174 Unit / 16 Architecture / 7 PG 静态数据探针）
- REVIEW 文档 HEAD：ce1993310a949db6a540e7b9f08e323d8e1225c6，run #132 五 Job SUCCESS
- 日期：2026-11-16 +08:00

## 功能

独立通用 Schema、严格 JSON 文档和真实 SQL 来源在同一个 CatalogBuilder 中完成类型/必填/重复/来源校验、Unicode NFC + Decimal 规范化、固定版本、确定性 ID 排序和 SHA-256 Hash。不信任加载顺序或 JSONB 对象键顺序；同一数据不受来源差异影响。返回的 FrozenStaticCatalog 不能被调用方修改，也不能通过旧输入集合重新写入。版本不同或字段差异必改变 Hash。

## 架构与状态所有权

四个 Assembly 的 ProjectReference/P03 manifest/Solution/锁文件一致。Schema 类型仍然是领域 Owned 的通用构造，未包含 Ability/Item、World/Entity、DBC 或特定游戏表。SQL Adapter 通过 BCL `Func<CancellationToken,Task<DbConnection>>` 与组合根接线，内部复用 P11 参数化命令；公共 API 无 Platform.Persistence 具体类型。P03-006 曾抓出错误构造函数，已以真实 Port 修复，不关闭 Architecture Rule。

## 失败/恢复

JSON 与 SQL 都有显式版本守卫、大小与行数上限、严格字段来源定位；错误输入不会生成/发布部分 Catalog。PG 使用 RepeatableRead 同事务读取头部和行，固定 SQL 模板带真实 DbParameter，不允许用户拼接表名/SQL。SQL JSONB 解析路径与 JSON 文件共享；数据库 Provider 错误不暴露 SQL/连接串正文。保留 JSONB 原始重复键不可恢复的输入预校验要求，以及 P16 跨 Catalog 与原子 Reload 的未来工作。

## 证据复现

- 首轮 #121/#122：Unit 先于 Solution 引用注册导致失败。
- #123：CA1720、CA1512。#124/#125：CA1859。#126：CS1503 数组 Length。#127：P03-006 公共技术实现泄漏。对应修复全部保留审查记录。
- #128/#129/#130/#131：全部五 Job SUCCESS；最终 #131 Ubuntu .NET 10.0.404 locked restore、Release、174 Unit、16 Architecture、7 项独立真实 PostgreSQL 18 探针，并保持 P11/P13/P14 既有集成全绿。
- PostgreSQL 与 JSON 的 SHA256 一致、NFC ID 和 Decimal 词法归一、版本错误、安全字段定位、超大 JSONB、超 10k 记录、取消后连接池释放均有可执行验证。
- 未声称 P16 Reload、生产业务 Schema、P64 性能或完整数据迁移已完成。

## 结论

本阶段 P15.J/P15.S 技术 C 的必需未解决实现项：**0**；独立 REVIEW HEAD run #132 全五 Job SUCCESS，P15 正式 ACCEPTED；最终 ACCEPTED HEAD CI 尚待复核。
