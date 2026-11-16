# P15 — Static Data Schema / Loader 通用契约

状态：P15 IN_PROGRESS，P15.J JSON、P15.S PostgreSQL SQL 已实现，真实验收以 [阶段记录](../progress/P15.md) 为准。

## 领域边界与项目图

- `StaticData.Abstractions`：通用 `StaticCatalogSchema`、`StaticFieldSchema`、`StaticScalar`、`StaticDataRow`、`StaticCatalogException`；不包含 AbilityDefinition、ItemDefinition、具体属性/玩法规则。
- `StaticData.Core`：`StaticCatalogBuilder.Build` 是 JSON/SQL 唯一的字段校验、排序、内容 Hash、冻结索引入口；返回 `FrozenStaticCatalog`，`Records` 是按 ID Ordinal 排序的只读集合、`Find` 是不可变的查找，不持有外部可变数据引用。
- `StaticData.Json`：严格 UTF-8 JSON 解析，逐文档、逐行和逐字段定位错误；没有任何数据库依赖。
- `StaticData.Sql`：P15.S PostgreSQL 来源适配器，可注入 `Func<CancellationToken, Task<DbConnection>>` 的受信任 ADO.NET 开连接 Port。组合根可传入 P11 `DatabaseConnectionFactory.OpenAsync`；具体 Npgsql 仅在 CI Integration Probe。SQL Adapter 生产实现依赖 P11 参数化命令，但公开 API **不暴露**具体 PlatformAdapter 类型，遵守 P03-006。
- `StaticData` 不持有 World/Entity 可变状态，也不定义游戏 DBC 或客户端版本协议；各未来领域模块拥有对应 Schema/目录定义。

## P15.J JSON 输入

固定文件结构（字段类型与合法列由调用者 Schema 定义）：

```json
{
  "catalog": "fixture_catalog",
  "schemaVersion": 3,
  "dataVersion": 7,
  "records": [
    {"id": "a", "name": "Alpha", "amount": 1.00, "active": true},
    {"id": "b", "name": "Beta", "amount": 2.5}
  ]
}
```

- 元数据只允许 `catalog`、`schemaVersion`、`dataVersion`、`records`。JSON `schemaVersion` 与领域 Schema 必须相等，`dataVersion` 与调用方预期版本必须相等；不得静默升级/降级。
- 每条记录必须有非空字符串 `id`（NFC、最多 64 UTF-16 字符、禁止控制字符）；Schema 显式声明其它字段及必填、标量类型与最大文本长度。字段最多 64 个；所有重复字段、重复 ID、未知字段、错误类型/范围和显式 null 均拒绝。可选字段未出现时保持缺失，不隐式填入默认值。
- Text 统一 Unicode NFC；WholeNumber 使用 Int64，FixedPoint 使用 .NET decimal 的文化无关规范字符串（1.00、1e0 → 1）；Boolean 为 true/false。拒绝 JSON 数字串伪装整数，不将浮点近似当作 decimal。
- 整个 JSON 文档最大 4 MiB、Catalog 最多 10,000 条记录；解析嵌套深度固定上限，禁止注释/尾随逗号。加载中任何错误都不会返回部分 Catalog。
- `StaticCatalogException` 的稳定字段：`Fault`、`SourceName`（受检安全标识）、`RowIndex`（1-based，文档级为 0）、`FieldName`。异常 `Message` 只有稳定错误码，不包含输入值、文件路径、SQL/连接串/凭证原文。

## 同源规范化 Hash 与不可变索引

`StaticCatalogBuilder` 对 Schema 名称、SchemaVersion、DataVersion、按 Ordinal 排序的字段声明（名称、类型、Required、最大长度）、按 Ordinal 排序的 record ID 和每个规范化字段序列构造不歧义的长度前缀 UTF-8 二进制流；计算小写 hex SHA-256。不同来源和读入顺序**不参与 Hash**；改变 Schema/版本/字段存在性则改变 Hash。

同一业务数据用 P15.J 文件与 P15.S JSONB 行读入后，得到相同记录顺序、类型化规范值与 Hash。冻结目录不允许外部更改字段或索引；正在运行的旧版本不能被新输入原地修改。**P16 才处理跨目录缺引用/循环引用、完整 Reload、Tick 边界版本原子切换及版本归档。**

## P15.S PostgreSQL 固定技术 Schema

探针用临时技术结构（无游戏规则表或 DBC），未来生产部署 DDL/migration 必须由模块 Infrastructure 经 P11 Migration 机制正式管理：

```sql
CREATE TABLE public.lotd_static_catalog_versions (
  catalog_name text PRIMARY KEY,
  schema_version integer NOT NULL CHECK (schema_version > 0),
  data_version integer NOT NULL CHECK (data_version > 0)
);
CREATE TABLE public.lotd_static_catalog_rows (
  catalog_name text NOT NULL,
  schema_version integer NOT NULL,
  data_version integer NOT NULL,
  record_id text NOT NULL,
  payload_json jsonb NOT NULL,
  PRIMARY KEY (catalog_name, schema_version, data_version, record_id)
);
```

- 使用 P11 的 `ParameterizedCommand/SqlParameterValue` 绑定 `@catalog`、`@schema_version`、`@data_version`、`@max_bytes` 与 `@record_limit`；查询文本固定、排序 `COLLATE "C"`，无用户拼接表名、列名、where SQL。
- 一个 `RepeatableRead` 事务完成版本头和多行内容读取，构建冻结成功且读事务成功提交后才向调用者返回。数据库出错或版本不匹配则不发布部分目录。
- 记录只读取 `最多 10,001` 条哨兵记录，超过 10,000 直接拒绝；SQL 先以 `octet_length(payload_json::text)` 限制超大单行，读取时累计限制整个 JSONB 行 payload 为 4 MiB，避免巨量结果集无限积压。SQL `record_id` 最多读取 64 个 Unicode 字符，先规范化 NFC，再与 JSONB payload 的规范化 `id` 比较；超过硬长度或包含控制字符会被拒绝。
- JSONB 对象交给相同的 `JsonStaticCatalogLoader.ParseRecord`；再经相同 `StaticCatalogBuilder` 冻结。错误定位来源、行号、字段，但不会返回原始 PostgreSQL 异常文本或 JSONB 内容。
- 目前 SQL 表结构由真实 PostgreSQL Integration Probe 建立；本阶段**不**宣称生产业务迁移已接线，不包括跨目录事务重载/P16。

## 出口与限制

Unit：规范化 Hash、不同顺序/Unicode/decimal 版本差异、重复/未知/缺失字段、非法值、不可变、10,001 行拒绝、硬大小与并发查询验证。

真实 PostgreSQL Probe：用 Npgsql 10.0.3 / PostgreSQL 18，从 JSONB 读取与 JSON 文件相同内容并比较 SHA-256，检验版本拒绝、字段来源定位、SQL record ID 不一致、过大行、过多行、取消后单连接池释放。

本阶段没有 DBC 强制格式、Ability/Item 业务依赖、生产 Host、完整 CatalogValidator、跨 Catalog 引用/循环、原子热重载、性能/长稳结论。P16/P64/P66 分别验收。
