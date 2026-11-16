# P24 — Entity Identity / Transform / Life（基础能力）

状态：IN_PROGRESS；目标 B。依赖 P05/P06/P09 已验收；P18 技术写租约已验收。P17 世界注册与生命周期尚未实施。

## 所有权与能力边界

- `Entities.Core` 是领域项目；仅依赖 `Kernel` 与 `Runtime.Abstractions`，不反向依赖 `WorldExecution` 的具体实现、Transport、Host 或 DB。
- `EntityIdentity` 由 `WorldId / WorldEpoch / EntityId / EntityGeneration` 四元组构成；构造拒绝空值；创建后不可改写归属。
- `EntityCapabilities` 以显式类型属性组合 `IdentityCapability`、`TransformCapability`、`LifeCapability`、`TargetCapability`，不提供任意类型查找器、可变字典或巨型 Unit。
- `TransformCapability` 是实体位置/朝向唯一领域存储，读返回按值复制的 Kernel `Transform`；`Relocate` 先通过实体本来拥有的四元组执行 `IWorldWriteContext.Demand`，不从当前租约冒充实体归属。
- `LifeCapability` 的生命标记只读，组合入口 `MarkDead` 将死亡标记与清除目标放在同一同步世界调用中；`Revive` 显式重置标记。P32/P34 才定义 Health、死亡事件、战斗清理与重生规则。
- `TargetCapability` 只保存完整的不可变目标身份快照而非实体引用，拒绝自身、跨 World、旧 Epoch 或死亡后的新目标；`Select(null)` 允许显式取消目标。目标的**实际存在/活动 Generation** 必须由 P17 注册表确认，P24 的静态校验不能替代它。
- 所有公开的写动作必须持有有效同步 `IWorldWriteContext` 并进行 `Demand`。Entity Core 不提供可注入非生产构造器以绕开 Guard。创建尚未注册的实体不等同于附加到运行世界。

## 尚未完成

- P17：世界注册表、唯一活跃归属、附加/移除/销毁、实体注册与 WorldExecutor Bind/Retire 的协调、代际目标解析。
- P20/P27：Transform 更新与 Spatial 原子更新、移动输入及校验；当前 `Relocate` 是实体私有基础能力，不能直接作为已接入 Spatial 的公开世界移动 API。
- P32/P34：生命值、致死效果、完整清理与 EntityKilled 事实。
- P60 生产身份/Host、P62 可复现模拟和 P64 性能均没有本阶段证据。

## 验证入口

`tests/Unit/LegendsOfTheDungeon.UnitTests/EntityCapabilitiesTests.cs` 使用真实 P18 WorldExecutor 检查存活/目标、真实租约失效、跨 World 拒绝、回收代际拒绝以及只读按值快照。编译、架构、锁文件和 CI 实际结果必须以 GitHub Actions 运行为准，不能把代码提交视为 PASS。

## 实现 Gate 证据

- HEAD `52088378c23c320796e669760dfc1a1ffc4f350e`，GitHub Actions run #163 六 Job SUCCESS：216/216 Unit，16/16 Architecture，真实 WorldExecution/Persistence/StaticData/PostgreSQL/Diagnostics/Networking 集成正常；负向构建、测试、架构以及中文文档 Gate 均 PASS。
- 独立四视角审查见 [P24_ENTITIES_REVIEW](../reviews/P24_ENTITIES_REVIEW.md)。P24 的正式 ACCEPTED 仍等待 REVIEW HEAD CI。
