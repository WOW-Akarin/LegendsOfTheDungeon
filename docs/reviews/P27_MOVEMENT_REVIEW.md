# P27 — Movement State / Speed / Input 四视角独立 REVIEW

- 阶段：**REVIEW**；目标 C。直接依赖 P20/P25/P26/P60.F 已 ACCEPTED；始终直接更新 main。
- 稳定实现 HEAD：`9367a9ac29076bdaabc6dc356a900e1bb5ec646c`，CI #226（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010607879）**15/15 SUCCESS**，344/344 Unit、16/16 Architecture，P27 真实六组 Probe 及旧十四 Job PASS。
- 首版实现 Head `ce74f708ed25de1c2731ebf1b53711d7e2982b4c` CI #224 编译 FAIL（CS8602 空主体分支），明确改用 `is not { } actor`；修复 Head `e401899271be3853e4a85ddea70a86669fc81d83` #225 **15/15 SUCCESS**（342 Unit）；#226 补速度缺失/上限/零速和死亡复活会话测试后再次全绿。
- 这份审查文档是独立提交；REVIEW HEAD 自身 CI 初始 **NOT_RUN**，不能以实现 CI 冒充审查通过。

## 视角一：功能和确定性

- 完整 `WorldMovementIntent` 含实体四元组、认证后 `BoundIdentity`、会话版本、严格递增序号、严格递增 Tick、目标 Transform。Kernel `Position` 与 `Angle` 在构造时拒绝 NaN/Infinity；World 写入前再次校验代际与同步租约。
- `WorldMovementLedger` 每个 P17 Entry 只有会话元信息，不含第二份 Position。模式仅 Idle/Moving；速度唯一取自 P25 `movement.speed` EffectiveValue，支持 Flat/Percent 来源叠加和精准撤销，拒绝缺失或超过 P27 100m/s 的配置；静止速度 0 仅允许位置不动。
- 每输入允许 1–4 个 Tick 差，速度预算 `speed(m/s) × tickMs / 1000 × Δticks`，先按轴过滤然后欧氏比较；拒绝输入不推进序号和 Tick 基准。成功后始终通过 P20 `RelocateCore` 内唯一 Transform+Spatial 原子重定位。
- P26 Stunned 拒绝任何移动输入，Rooted/Feared 拒绝位置变化；死亡废止会话，复活不恢复旧票据。普通可信世界传送也作废会话、必须显式重新绑定；旧 Generation/旧 Session/旧 Tick/伪造主体均拒绝。
- P19 确定性状态摘要 schema **v5**，加入 SubjectKey、IdentityOrigin、SessionVersion、LastSequence、LastTick、TickDuration、Mode、EffectiveSpeed；同 Tick/同位置/速度但来源或序号不同的摘要实测不同。跨 schema v4/v5 StateHash 不应当作同版本。

## 视角二：模块化与安全入口

- Runtime.World 内独立 `WorldMovement.cs` 封装 P27 窄能力，复用 P17 canonical Entry。跨模块只增加 **Security.Contracts** 中已验收 P60.F `BoundIdentity`，不依赖生产认证 Adapter、不新增独立生产项目或数据库表。P03 architecture manifest 允许的项目引用与公共 API 引用均明确更新；受影响的 Runtime.World/WorldScheduling/Unit/Integration NuGet 锁图已同步。
- `BoundIdentity` 由 Security.Contracts 私有构造，只有 LocalTest 工厂暴露给测试；生产身份须由可信 P60.F Binder 签发。移动意图不接收“认证字符串”或 Token；World 再比对主体 Key、Origin、SessionVersion，拒绝跨会话声称。
- P18 `WorldExecutor.OfferExternal` 对匿名、LocalTest 或未授权外部行动默认拒绝且执行前二次授权；Unit/Probe 使用可信 Producer 模拟执行，不假装本次交付了真实生产账号认证。**实际网络 Parser/Host 仍必须只构造固定动作，并直接注入会话已绑定 BoundIdentity，绝对不能把任意网络反序列化的 IWorldAction 传给可信 Producer。** 生产 P60 完整入口与鉴权撤销需独立验收。
- P25 Modifier、P26 控制和 P20 Spatial 仍为唯一业务真相；P19 Hash 编码确定性额外状态，不访问墙钟、不从外部浮点/HashSet 迭代顺序写业务状态。

## 视角三：失败与恢复

- 旧/重复 Sequence 与 Tick、超过四 Tick 的跨度、无效/过期会话、不匹配主体、错误实体代际、超速、无效速度、控制状态、死亡、目标 Cell 卸载都明确拒绝，并保留最后成功的 Sequence/Tick 与当前 Transform/Spatial。失败不会消费会话。
- `WorldInstance.RelocateCore` 同锁完成空间目标合法性验证→Grid Move→实体唯一 Transform；异常沿 P20 fail-closed 将世界标记 Faulted，不声称异常自动回滚已部分更新。普通可信 Relocate 只有成功才令移动会话失效。
- 重新绑定推进不可复用 SessionVersion，原会话 Seq 失效；实体退休/Generation 变更不会将旧 Ledger 带入新的 Entry。身份来源既不是裸字符串，也没有可写 Entity 对象从 API 泄漏。
- 当前 P27 的 100m/s 最大值和 ≤4 Tick 是基础输入预算，不是 P29 完整反作弊/跳变豁免；击退、传送、跳跃和客户端预测等必须有 P28/P29 业务明示方案才能放开，不能默认为合法。

## 视角四：真实证据、质量 Gate 和性能基准

| 验收项 | 稳定实现 CI #226 |
|---|---|
| .NET SDK 10.0.404 locked restore / Release Build | PASS |
| 单元（新增 P27 + 全部旧 Unit） | **344/344 PASS** |
| P03 项目职责/引用及架构测试 | **16/16 PASS** |
| P02/P03 负向构建/测试/架构 Gate、P04 中文 XML、P05 Strong ID、分析器 | PASS |
| P60.F 默认拒绝/主体冒用、旧会话/序号/Tick 拒绝 | PASS |
| P25 速度 Modifier 双来源与回滚、零速度/非法速度、P26 控制与死亡、手工传送 | PASS |
| P20 真实 Transform/Spatial 跨 Cell 与无效 Cell 原子一致 | PASS |
| P19 同 Tick 不同绑定主体/序号状态哈希 | PASS |
| P27 MovementProbe 五组功能 + 真实移动基准 | `P27_MOVEMENT_INTEGRATION_PASS` |
| 原有 14 Job 集成回归 | PASS |
| 完整实现 HEAD #226 | **15/15 SUCCESS** |
| 本 REVIEW HEAD CI | NOT_RUN；本提交独立验证 |
| 正式 ACCEPTED HEAD CI | NOT_RUN |

记录型 Release baseline：Ubuntu 26.04.1 LTS、4 CPU、.NET Runtime 10.0.15、固定 Tick 50ms、100 实体、5 Tick、500 次 `WorldInstance.SubmitMovement`；#225 原始报告 p50 **0.0034ms**、p95 **0.0079ms**、p99 **0.0190ms**，无性能合格阈值，**不代表 P64 性能目标**。完整报告存在 CI 的 `world-movement` Artifact。

## 独立 REVIEW 结论（自身 CI 尚待跑）

P27 目标 C 的基础身份绑定、移动状态与速度、序号/逻辑时基、真实 P20 原子位移、P25/P26 联动及首版可复现度量已具备通过 #226 的独立证据，四个视角下当前范围内必修阻塞为 0。P28/P29 完整运动/反作弊、P60 生产网络绑定、P62 回放与 P64 负载留给后续单独验收。**不得在本 REVIEW HEAD 自身 15/15 SUCCESS 前宣布正式 ACCEPTED。**

## 独立 REVIEW HEAD 全绿与正式决定

- REVIEW HEAD `d40b21b1d2759df5b0ba1dc23d7e420d67e55895`，CI #227（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44010992018）**15/15 SUCCESS**，344/344 Unit、16/16 Architecture，P27 六组真实 Probe + 全部旧十四 Job 继续 PASS。
- 四视角范围 C 的必修阻塞 0，P27 **正式 ACCEPTED**。状态提交 HEAD 自身的 CI 尚待核验，不能以 REVIEW HEAD #227 替代；其余 P60/P28/P29/P62/P64 分别进行后续阶段验收。

## 正式 ACCEPTED HEAD 自身验证

- 正式 ACCEPTED Head `df3e192471c4ec9ebe976c42e5c8bb9994e0c203` CI #228（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44011308289）**15/15 SUCCESS**，344 Unit、16 Architecture，P27 六类真实 Probe 与全部旧十四 Job 完整 PASS。
- 因此实现 #226、独立 REVIEW #227、正式 ACCEPTED #228 各有独立绿色 CI，P27 目标 C 正式收口。仅保留 P60 正式网关/P28/P29/P62/P64 的未来风险与单独验收范围。
