# 中文注释与文档质量 Review Checklist

> 关联阶段：P04。  
> 目的：自动 Gate 只负责“缺失”和“非法例外”等机械事实；中文语义是否正确、复杂逻辑是否解释充分，必须由 Review 判断。

## 1. 自动 Gate 与适用范围

所有项目由中央 `Directory.Build.props` 默认启用 XML documentation file；Test 项目在自身项目文件显式关闭，Generated/ThirdParty 例外项目也必须显式关闭并同时声明可审计例外。项目已经全局启用 `TreatWarningsAsErrors=true`，因此 C# 编译器的 `CS1591` 会让缺失公开 XML 注释的构建失败。

自动 Gate 覆盖公开可见类型和成员，不只检查方法。Public 类型、构造函数、方法、属性、事件、字段等只要触发 `CS1591`，都必须补齐有意义的 XML 注释。

Test Assembly 不构成生产公共 API，因此不要求为每个测试类/测试方法生成公开 XML 文档；测试本身仍需通过清晰的测试名和断言表达意图。

## 2. Generated / ThirdParty 例外

只允许**项目级**显式例外，且只能用于：

- `Generated`：由工具/Source Generator/协议生成器生成，正常编辑流程不应手工维护的代码；
- `ThirdParty`：按许可和来源规则原样纳入、由上游维护的第三方代码。

例外项目必须同时设置（其中关闭 XML 文档只对合法例外允许）：

```xml
<LegendsOfTheDungeonDocumentationGateExempt>true</LegendsOfTheDungeonDocumentationGateExempt>
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<LegendsOfTheDungeonDocumentationGateExemptionKind>Generated</LegendsOfTheDungeonDocumentationGateExemptionKind>
<LegendsOfTheDungeonDocumentationGateExemptionReason>具体说明生成器/第三方来源以及为什么不应由本项目维护 XML 注释。</LegendsOfTheDungeonDocumentationGateExemptionReason>
```

`Kind` 只允许 `Generated` 或 `ThirdParty`。理由不能为空。普通业务代码不得以“历史代码”“临时”“太多注释”等理由申请例外。

非例外生产项目禁止通过 `NoWarn`、`WarningsNotAsErrors` 或直接设置 `GenerateDocumentationFile=false` 绕过 Gate。

## 3. 中文 XML 注释语义

自动化不使用“是否包含汉字”来判断质量。Review 必须确认注释主要使用中文说明真实语义；代码标识符、协议名、异常名、上游路径等可保留英文。

Public API 的 XML 注释按适用情况回答：

- **职责**：该类型/方法负责什么，不负责什么；
- **前置条件**：输入范围、对象状态、生命周期要求；
- **线程/执行上下文**：是否只能在 WorldExecutor、特定 Actor/串行上下文调用，是否线程安全；
- **状态影响**：读取/修改谁拥有的状态，是否有外部副作用；
- **失败语义**：返回错误、抛出异常、拒绝、重试、Faulted 等分别意味着什么；
- **单位/时序**：米、弧度、Tick、UTC、排序/提交阶段等容易误用的信息；
- **参数/返回值**：不能只重复参数名，需说明边界和语义。

以下内容即使“有中文”也不能通过 Review：

- “执行方法”“获取数据”“设置值”这类只重述名字的句子；
- 注释声称线程安全，但实现要求 WorldExecutor；
- 注释声称失败不修改状态，但实现可能部分提交；
- 复制旧行为说明，却没有同步已经变化的前置条件或错误语义。

## 4. 复杂逻辑设计注释

以下代码在实现附近必须有中文设计注释，必要时链接模块设计文档：

- 状态机：状态、允许迁移、非法迁移和恢复入口；
- 并发/Actor：唯一写上下文、排序、重入、取消、过期结果处理；
- 算法：关键不变量、复杂度/预算、确定性排序和数值边界；
- 事务/跨模块原子操作：Validate → Prepare → Commit → Publish、失败/恢复、幂等键和版本校验。

不要求每一行重复代码。注释应解释“为什么这样做、哪些不变量不能破坏”，而不是把代码翻译成中文。

## 5. AzerothCore 参考来源

如果某段算法、状态机、数值语义或测试向量直接参考 AzerothCore，附近注释或链接的研究文档必须能定位：

- Commit：精确 SHA；
- File：上游仓库相对路径；
- Function / Type / Symbol：具体函数、类型或符号；
- 采用/不采用的语义和已知差异；
- 许可/复用方式（涉及直接复制、改写、数据/测试向量时）。

当前固定研究基线为 `17f9f9d5003708136c798ab4ef8c1b9762251bad`。完整记录使用 [AzerothCore 研究记录模板](../research/azerothcore/REFERENCE_TEMPLATE.md)。

## 6. 新增说明文档

新增设计、研究、运维、验收等说明文档必须以中文为主要叙述语言。允许保留代码、命令、API 名、文件路径、上游原文短语和行业专有名词。

不建立“汉字比例”自动 Gate，因为它无法证明文档可理解或语义正确；Review 必须实际阅读新增说明文档。

## 7. Review Checklist

- [ ] 新增/修改 Public API 均有 XML 注释，且 CI 未出现 `CS1591`。
- [ ] 注释说明真实职责，不只重复类型/方法名称。
- [ ] 适用时写明前置条件、执行/线程上下文、状态影响和失败语义。
- [ ] 参数、返回值、异常、单位与边界没有遗漏关键语义。
- [ ] 状态机/并发/算法/事务有“为什么”和不变量说明，而不是逐行翻译。
- [ ] 注释与当前实现一致；发现“中文但错误”的注释必须退回修正。
- [ ] Generated/ThirdParty 例外类型和理由具体、可审计，没有把普通业务代码伪装成例外。
- [ ] 未使用 `NoWarn` / `WarningsNotAsErrors` 绕过生产 `CS1591`。
- [ ] 涉及 AC 参考时能定位 Commit / File / Function（或 Type/Symbol），并记录差异与复用方式。
- [ ] 新增说明文档以中文为主要叙述语言。
- [ ] 没有为了满足 Gate 增加无业务价值的 Public API 或无意义注释。
