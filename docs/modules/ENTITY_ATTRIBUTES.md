# P25 — Stats / Resources / Modifier 技术契约

- 状态：IN_PROGRESS（目标 C），依赖 P24/P07 已 ACCEPTED；当前首次实现尚未经过新 HEAD CI。
- 契约 ID：`P25.ENTITY_ATTRIBUTES.v1`；提供方/唯一写入所有者 `Entities.Core.EntityAttributeCapability`；调用方 `Runtime.World.WorldInstance` 的窄同步命令。无新的中心 Manager。

## 状态与数值

- 仅使用语义键 `AttributeKey`（1–64 字符规范 ASCII），不使用 WoW StatId；每个完整 `EntityIdentity` 的属性是独立账本。
- 基础属性：非负 decimal，界限 0–1,000,000,000，统一保留 2 位小数、MidpointRounding.AwayFromZero。最多 64 个总属性（基础+派生）。
- 派生属性：只可从**一个既有基础属性的有效值**计算 `sourceEffective × factor + offset`；factor 0–100、offset 0–1,000,000,000。禁止派生引用派生，避免隐含循环和重算中途共享第二套真相。
- Modifier：独立业务来源 `AttributeModifierSource`（非零）与绑定完整 World/Epoch/Id/Generation 的 `AttributeModifierHandle`（非复用序号）。按 Priority（-100..100）升序、同优先级按 Sequence 升序依次执行 Flat（±10 亿）/Percent（-100..1000）。**每一步**截断负数为 0、超过上限则拒绝，按两位小数 AwayFromZero 舍入。达到 256 个活动来源拒绝。
- 多个来源可同时叠加，同来源可以有多个句柄；`RemoveModifier` 只删精确句柄，`RemoveSource` 删除指定来源的所有句柄；重复撤销幂等，代际外句柄不可用。重算结果不承诺回到添加前数值，因为其它来源仍可能变化。
- ResourcePool：每实体最多 64 种资源，分别绑定已定义 Stat 为 Max；定义需显式 Current（不自动回满）。Current 始终 0–Max。Max 减少时 `Current=min(Current,Max)`，Max 重新增加时不自动加血/回能；资源越界返回 `OutOfRange` 并保持旧值。死亡拒绝任何资源 Set/Change，P32/P34 后续定义 Health/Death 事务。此处不提供 P38 Cost Reservation 或真实持久化经济提交。
- 所有重算写入复制候选字典、先计算完全部派生、修饰与资源投影，再将原子性可观察的字段替换；出现 Decimal Overflow、配置失败、配额不足时不改变值/来源/句柄序号。P18 同步执行上下文与 P17 canonical 实体四元组仍先校验。

## 快照、Hash、外部一致性

- `EntityAttributeSnapshot` 包含按 Key Ordinal 排序的 Stats、Derived 与 Resources，按序号升序的 Modifiers 和单调末尾序号；集合为防御性冻结 Array.AsReadOnly；公开 World API 只读取这些值，不返回可写能力。
- P19 的 `WorldStateDigest` schema **v4**：新增来源、序号、基础/有效/派生公式、资源当前及最大值的稳定序列化。decimal 使用 InvariantCulture `G29`，避免同数值不同 scale 影响哈希；这是对 v3 **非兼容**快照 Hash 变化，旧版本 replay 比对只能在匹配的构建版本下执行。
- 无外部 DB、跨 Host 或异步 Worker；这些分别归 P11/P62/P60。P26 EffectSource 与 P25 AttributeModifierSource 属于不同权能来源，不允许跨能力误撤销。所有写入处于当前 P18 WorldExecutor 同步租约；旧票据、跨 World 或退休 Generation 不能写。

## 测试与验收

- 单元：优先级与派生重算、并存来源精确撤销与幂等、资源最大值缩减/增长、不越界、死亡保护、溢出不发布部分值、真实旧代际/租约、Round AwayFromZero、冻结快照、Hash 来源敏感。
- 真实 `AttributesProbe`：使用 P17 WorldInstance 中**同一 canonical EntityCapabilities**，在真实 P18 WorldExecutor 内写入与回收、触发 P19 WorldTickPipeline Hash；五组场景（多来源、资源死亡、代际与旧租约、Overflow、确定性 replay）。成功后输出 `P25_ATTRIBUTES_INTEGRATION_PASS`。
- P25 首次实现 CI 尚未运行，13 个原有 Job + 新第 14 Job 必须全部 SUCCESS，之后独立四视角 REVIEW HEAD 与正式 ACCEPTED HEAD 再分别复验。
