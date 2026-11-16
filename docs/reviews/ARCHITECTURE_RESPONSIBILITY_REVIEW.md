# P03 架构职责聚集审查

> 目的：补充自动依赖检查无法判断的“Common/Utils/中心 Manager 式职责聚集”问题。  
> 原则：**名称不是违规证据**。一个类型叫 Manager 不代表有问题，一个类型不叫 Manager 也不代表职责合理。

## 自动 Gate

`eng/architecture/production-projects.json` 为生产项目显式架构清单。Architecture Tests 会：

1. 精确比较真实 `ProjectReference` 与白名单；
2. 检查 Domain → Platform Adapter/Host；
3. 检查 Module A → Module B.Infrastructure/Internal；
4. 检查 Module → Host；
5. 检查生产项目引用图无环；
6. 扫描真实编译 Assembly 的公共 API，禁止未批准的实现项目类型泄漏；
7. 扫描真实 `InternalsVisibleTo`，只允许显式测试 Assembly；
8. 当一个项目被至少 3 个不同模块引用时，要求清单提供已批准的 `responsibilityReview` 和理由。

第 8 项只强制“必须审查”，不自动宣布高 fan-in 一定错误。

## 人工职责审查

出现高 fan-in 项目、通用工具集合、中心协调类或跨模块服务时，Review 至少回答：

- 它是否拥有明确且唯一的数据/不变量，还是在替其他领域改状态？
- 公共 API 是否按稳定能力设计，还是不断加入与消费者一一对应的便利方法？
- 是否把多个领域的写操作集中到一个对象，形成绕过所有权的万能入口？
- 是否同时依赖多个模块的 Infrastructure/Internal？
- 是否持有跨 World/跨聚合的可变对象引用？
- 是否能拆成 Kernel 值类型、窄 Contract、领域 Port 或组合根 Adapter？
- 删除某个消费者后，该共享职责是否仍有独立存在理由？
- 新增消费者是否迫使共享项目理解消费者内部概念？
- 是否只是因为“大家都能用”而放进 Common/Utils，而没有稳定语义边界？

## 清单记录

当自动 fan-in Gate 触发时，在对应项目清单项增加：

```json
"responsibilityReview": {
  "approved": true,
  "reason": "说明稳定职责、状态所有权、为何不能下沉到具体模块，以及允许的消费者范围"
}
```

`reason` 不能只写“公共工具”“方便复用”“Manager 需要共享”等同义句。

## 非目标

P03 不通过类名长度、方法数量或 Common/Utils/Manager 关键词自动判定“巨型类”。这类信号只能用于人工定位；真正的自动失败依据必须来自项目引用、公共 API、IVT、依赖环或明确的审查元数据。

## P17 引入第三模块引用 Kernel 的共享职责审查（P03-008）

- 触发：新增 `Runtime.World` 后，Kernel 被至少三个独立 Module 引用；CI #168 P03-008 Gate FAIL 并正确阻断。
- 所有权：Kernel 仅包含强类型 ID、不可变基础值、单位、时间和纯值计算，不保存实体可变世界状态；World/Entity 注册归 Runtime.World、实体能力归 Entities。
- API 依赖方向：Kernel 自身零项目引用，不知道调用方、没有跨模块万能 CRUD/Service Locator；不同领域共享 `WorldId` / `WorldEpoch` / `EntityId` / `EntityGeneration`，若散落各模块会破坏强类型契约。
- 审查批准范围：仅批准 Kernel 维持独立、纯净、不涉及业务对象的数据语义；以后新增领域管理逻辑、实体/世界持有者或中心服务仍必须另行审查，不能据此任意扩展 Kernel。
- 已在 `eng/architecture/production-projects.json` 增加 `responsibilityReview.approved + reason` 供 P03 自动 Gate 对照；不更改 fan-in 阈值，不新增例外。
