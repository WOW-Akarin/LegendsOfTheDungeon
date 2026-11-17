# P25 — Stats / Resources / Modifier 独立四视角审查

- 状态：**ACCEPTED**（目标 C）；主线 main，不建立分支。
- 稳定实现 HEAD：`a90505a1f4ce23dc0ac6e37c945ebb28839d5073`。
- 实现 CI #220：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999035617 — **14/14 SUCCESS**，334/334 Unit、16/16 Architecture、P25 五类真实 AttributesProbe 及旧 13 Job 全部 PASS。
- 前置 P24(B)、P07 均正式 ACCEPTED。审查文档独立提交后必须以自身 HEAD 重跑 14 Job，不能使用稳定实现 CI 代替。

## 1. 功能正确性与确定性

- P25 的唯一实体账本为 `EntityAttributeCapability`：基础 Stat 0–10 亿，单层派生从一个基础有效值 × factor + offset；禁止派生链隐性循环、基础键与派生键冲突。属性与资源总键预算各 64。
- Modifier 由非零业务来源和完整实体四元组+单调序号的撤销句柄组成。Flat/Percent、优先级升序与同优先级序号升序均确定；decimal 每步 AwayFromZero 保留两位小数，负数裁剪到 0，超 10 亿计算失败。最多 256 条活动 Modifier。
- 正式实例源自 P17 `WorldInstance` 唯一 `EntityCapabilities`，而非测试对象的第二份实体；P18 当前租约写、P19 同步 Tick 的状态摘要联动均在独立 Probe 中运行。
- 资源绑定已有 Stat 最大值；当前值 0..Max，最大值降低时裁剪、重新上升时不自动回满；死亡禁止 Set/Change，复活后恢复资源更新许可，P32 Health 与 P34 Death 仍单独定义。
- 新增 Unit 保留 P20 空间重建快照的值相等语义：`EntityAttributeSnapshot.IEquatable` 按属性/派生/修饰/资源/尾序号结构比较，防止引用级回归。

## 2. 契约、单写所有权与权限

- `Entities.Core` 为属性/资源唯一可写所有者；`Runtime.World.WorldAttributes` 只是现有 P17 WorldInstance 的独立窄 API 实现，不额外缓存属性/资源真相。World 已注册 Entry 内产生唯一实体能力，外部只获得冻结结果或 `WorldEntityResult`，不泄露写容器。
- 修改首先要求真实 P18 `IWorldWriteContext` 当前 World/Epoch 与 Entity/Generation，随后 `FindActive` 只允许 Running + Active；旧代际、旧租约、跨 World 不可写。现有 P18/P26 授权、P20 Spatial 唯一 Transform 都保持不变。
- 无新增生产 Assembly、NuGet 包或违反 P03 依赖方向；P03 Architecture 16/16、Public XML/分析器及 P02–P05 正反质量 Gate 成功。
- `AttributeModifierSource` 与 P26 `EntityEffectSource` 是不同能力权能；句柄不能跨实体代际使用。当前没有开放网络/脚本写入口，P27/P38 消费方后续必须通过正式命令并检查授权。
- 公共 `WorldEntitySnapshot` 新增冻结 `Attributes` 值对象；P19 `WorldStateDigest` schema v3→v4，基础/派生公式、有效值、来源与序号、资源当前/最大都加入规范化 Hash。旧 StateHash 与 v4 **不兼容**，需要构建版本区分并留给 P62 完整 Replay 迁移。

## 3. 失败、溢出、资源和恢复

- SetBase、DefineDerived、Add/RemoveModifier、RemoveSource 在独占锁内先用候选字典全部重算，再一次性发布；非法 factor/金额、容量/Decimal 溢出、未知键均不改变来源、当前值或尾序号。最近新增 256 源上限、第 257 源拒绝和 decimal.MaxValue Delta 溢出测试，CI #220 PASS。
- Resource Set/Delta 越界、未知资源、死亡和重复设置由明确结果区分；失败不消费、不回满、不默默截断玩家输入。最大值改变导致的内部裁剪是与 Stat 成功重算同步的明确策略。
- 同一 Modifier 句柄撤销重复返回幂等，RemoveSource 只移除指定业务来源下的活动句柄；其它来源重新计算并继续有效，旧句柄序号不重用。
- 属性 Snapshot 的排序/防御性数组与 `IEquatable` 避免在多次读取中产生可变引用或破坏已有 P20 世界快照比较；P19 Hash 使用 InvariantCulture 的 decimal `G29` 格式并包含来源序号，不把两个等效不同来源账本误当同一状态。
- 当前只证明 WorldExecutor 同进程单 World 的同步提交；没有 PostgreSQL 落盘、崩溃恢复数据库事务、跨世界转移、P62 全链路回放或 P64 量化性能，不把这些列入 P25 C 的成功范围。

## 4. 实测证据及历史失败透明性

| 证据 | 实现 CI #220 |
|---|---|
| SDK 10.0.404 / locked restore / Release build | PASS |
| Unit 新增和旧回归 | **334/334 PASS** |
| Architecture 依赖和职责 Gate | **16/16 PASS** |
| 原有构建/测试/架构负向门禁、P04 XML 文档、P05 Strong ID | PASS |
| Priority/Derived/Source/Handle/资源裁剪/死亡/过期租约/数值溢出/快照 | PASS |
| 256 Modifier 预算与 257 拒绝、Decimal.MaxValue 资源 Delta | PASS |
| 真实 P17/P18/P19 的 P25 五组 AttributesProbe | PASS，`P25_ATTRIBUTES_INTEGRATION_PASS` |
| 旧 13 个独立 Integration Job | PASS |
| 稳定实现 HEAD CI #220 | **14/14 SUCCESS** |
| 独立 REVIEW HEAD CI | NOT_RUN，独立提交后再核验 |
| 正式 ACCEPTED HEAD CI | NOT_RUN，审查通过后再做 |

失败历史保留：#217 生产全部可编译但 Unit 测试 xUnit2031 违反分析器而 Build FAIL；#218 Unit 330/331 PASS，旧 P20 快照重建结构相等 FAIL；#219 修复后 14/14 PASS（332 Unit）；#220 容量/溢出加固后 14/14 PASS（334 Unit）。未删除/Skip/弱化旧测试与质量 Gate。

## 当前独立审查结论

功能正确性、架构所有权、错误和恢复边界、证据可复现性四个视角下，P25 目标 C 所需的同步世界属性/资源/Modifier 最小真实集成范围没有尚待修复的必修阻塞；未来 P27、P32/P34、P38、P62/P64 仍需独立验收。**本 REVIEW 文件提交自身的 CI 未运行；只有再拿到 14/14 SUCCESS 后才能正式接受该审查结论。**

## 独立 REVIEW HEAD 真实验收和正式决定

- REVIEW HEAD `6d66ae91c0c0dec713d36214869bcccaf2d7aef7`，CI #221（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999422655）**14/14 SUCCESS**，334 Unit、16 Architecture、新 P25 真实五场景和全部原有 13 Job 通过。
- 功能、架构、失败和恢复、可复现证据四视角必修阻塞 0；P25 目标 C **正式 ACCEPTED**。正式状态 HEAD 的 CI 尚需单独验证，结论仍仅限本阶段。

## 正式 ACCEPTED HEAD CI 复核

- P25 正式 ACCEPTED HEAD `59d8d55943e5a70d340e4d04d34ff1f3834d19d5`，CI #222（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43999787227）**14/14 SUCCESS**，334/334 Unit、16/16 Architecture，P25 五组真实 Probe 和全部旧集成继续成功。
- 因此实现 #220、独立 REVIEW #221、正式 ACCEPTED #222 均各自全绿，P25 目标 C 已收口。P27/P62/P64 的验收范围独立。
