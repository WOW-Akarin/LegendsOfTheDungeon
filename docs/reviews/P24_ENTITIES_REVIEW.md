# P24 — Entity Identity / Transform / Life 独立四视角审查

- 审查状态：ACCEPTED（P24 基础能力 B）
- 实现提交：`52088378c23c320796e669760dfc1a1ffc4f350e`
- 实现 CI：#163 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43885627217
- 本审查独立 HEAD：`b5af3d4247645515d6f582110331d43edb9d7bd9`，CI #164 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43886436240，六 Job SUCCESS
- 阶段出口：P24 基础实体薄能力 B；不声称完成 P17 WorldInstance/P20 Spatial/P32 Health

## 1. 功能审查

- `EntityIdentity` 的四个强类型组成项非空才允许构造；CLR default 被 `EntityCapabilities` 拒绝。核心不再以单独 EntityId 充当世界归属版本。
- Transform 的唯一实体持有者是 `TransformCapability`，读返回 Kernel 不可变值对象，写先要求实体真实身份的 `Demand`。
- Life 初始 Alive；重复 MarkDead/Revive 是无副作用返回 false；死亡在同一同步方法中同时清空当前目标。目标仅保存完整四元组快照，拒绝自身/其他 World/其他 Epoch，死亡后不能新选目标。
- `TargetCapability` 无法确认目标是否仍注册与活跃；必须由 P17 Registry 在对外能力暴露前校验目标存在性和 Generation，这是明确后续职责，不是 P24 通过的条件。

## 2. 领域架构与状态所有权

- 新生产 Assembly `Entities.Core` 只有 Kernel、Runtime.Abstractions 两个 ProjectReference；无 Platform/Host/DB/网络依赖，manifest 声明 `Domain / Entities`，且 P03 的 16 个架构测试全部通过。
- 组合容器仅有固定的四个属性，不存在 `Get<T>`、`Dictionary<Type, object>`、可变组件集合或承载其他领域逻辑的巨大 Entity Unit。
- Target 只保存目标身份的按值结构，未暴露其它实体对象/集合；Transform 返回按值对象。P27 世界重定位入口必须统一协调 Transform 和 Spatial，不能因为本阶段允许纯实体 Relocate 而直接跳过空间索引。

## 3. 故障与权限审查

- 已使用真实 `WorldExecutor` 编写新增 6 个 Unit 案例：非空四元组、作用期结束的租约拒绝、跨 World 拒绝、死亡清目标、自身及旧 Epoch 目标拒绝、退休代际拒绝、未 Bind 实体拒绝。
- `IWorldWriteContext` 是应用内可信组合根使用的 Port，领域调用 `Demand`；验证的是 **P18 真实执行器签发的 Scope**，不把自行实现的假的 Port 或任意进程内恶意插件称为可信身份。未开放外部网络 Entity 写。
- Domain 抛出未处理异常时 P18 执行器将故障隔离而非提供跨实体事务回滚；MarkDead/清目标组合当前不执行 I/O。多实体与 Spatial 原子性仍需后续统一世界命令处理。

## 4. 实际可复现验证

| Gate | CI #163 |
|---|---|
| .NET 10.0.404 locked restore / Release build | PASS |
| Unit Tests | **216/216 PASS**（P18 原 210 + 新增 6） |
| P03 Architecture Tests | **16/16 PASS** |
| P02/P03 负向门禁、P04 XML 公共文档、P05 Strong-ID 编译负例 | PASS |
| P18 世界执行器真实 Worker 集成 | `P18_WORLD_EXECUTION_INTEGRATION_PASS` |
| P11 PostgreSQL 18 持久化 | PASS |
| P15/P16 PostgreSQL 静态数据与热重载 | PASS |
| P14 诊断 Worker / P13 TCP Loopback | PASS |
| 六 Job 汇总 | **6/6 SUCCESS** |
| REVIEW HEAD 六 Job | **6/6 SUCCESS**，run #164，216 Unit / 16 Architecture |
| ACCEPTED HEAD 六 Job | NOT_RUN |

## 范围限定与结论

P24 B 技术出口所要求的基础能力、领域模块边界与现有 P18 同步世界写门禁的集成单测均有真实实现 CI 证据。没有把实体创建误说成世界注册、将目标四元组静态匹配误说成活动 Registry 校验、或将死亡标记冒充完整 Health/战斗结算。

独立 REVIEW 文档与实现代码分离；run #164 六 Job 全部 SUCCESS，P24 技术 B 必需未解决项为 0，正式 ACCEPTED。P17 的前置依赖已满足；P17 仍须独立实现与验证，P24 的验收不代表 P17 完成。
