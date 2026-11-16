# P21 — Visibility / Relevance 技术契约

状态：ACCEPTED（目标 C）。直接依赖 P20 Spatial 和 P26 Control/Tags 均已正式 ACCEPTED。实现 #197、独立 REVIEW HEAD `fe790ec26a11699e38e2b54f917268faf3dd5c98` CI #198 均 11/11 SUCCESS；正式 ACCEPTED HEAD CI 待核验。

## WorldVisibilitySet 的读取边界

- `Runtime.World.WorldVisibilitySet` 绑定一个不可变的完整 `EntityIdentity`（World/Epoch/EntityId/Generation），不会被新 Generation 复用或自动转给另一个观察者。
- 领域状态唯一真相仍来自 P17 `WorldInstance`、P20 `QueryNearby`（P24 唯一 Transform）和 P26 的 Life/Tags/Control 快照。P21 仅保存**每观察者的派生可见集合**，不提供实体写入口、复刻 Position 真相、附加可变 EntityCapabilities 或网络 Packet。
- `WorldVisibilityFilter` 一次构造后不可变：三维闭区间距离 0–4096 m、RequiredTags（全部必须有）、ExcludedTags（任意有则排除）、RequireAlive、ExcludedControls。标签必须遵守 P26 ASCII 小写规则且进行 Ordinal 排序去重。最多 64 个配置标签、1024 个可见实体；超额抛出明确错误并**不发布半成品视图/版本**。
- 观察者只有在同 World/Epoch/Generation 的 P17 Registry 中处于 Active 且 Alive、世界状态 Running 时才拥有视野。无效/退休/死亡观察者下一次有效 Refresh 清空可见集合，输出一次 Leave；之前版本的静态视图允许可信读取，但必须按版本标记为历史数据。
- 每次 `Refresh(context)` 必须持有本 World 当期真正的 P18 `IWorldWriteContext.DemandWorld` 同步租约；只有 Sync World 作用域能发布一次新视图，后台线程、租约过期、其它 World Scope 均无法更新。无 P60 生产网络授权的公开 Refresh 权能仅交给可信 Host/WorldTick 组合根，不交给外部输入。
- 错误/故障世界必须 fail-closed：`WorldLifecycle.Faulted` 禁止发放新的 Delta。容量溢出/过滤无效等可恢复输入错误不得把世界伪装成 Faulted；调用端应在可信同步 Action 内捕获并修正规则，否则未捕获异常仍由 P18 fail-closed。

## Enter/Leave Delta 与不可变 View Model

- 视图 `WorldVisibilityView` 记录 `World`、`Epoch`、`Observer`、每个独立 VisibilitySet 的 `SnapshotVersion`（初始 0）、`ObserverActive`、按 EntityId 升序的 `WorldVisibilityMember` 快照。它**不是**世界状态变更的全局提交版本：必须使用 World/Epoch/Observer+SnapshotVersion 联合判断消息顺序。
- Delta `WorldVisibilityDelta` 记录 `PreviousVersion`、`SnapshotVersion`、`Entered` 的只读实体视图、`Left` 的完整四元组身份和最新完整 View。正常每次成功重计算版本 +1，包括无集合变化的一次重计算；版本溢出拒绝，不回卷。失败维持原版本与成员。
- 进入定义：旧视图没有该完整身份、当前 P20 距离和 P26 过滤均允许；离开定义：旧有身份不再满足（包括超出边界、标签/控制/生命状态改变、实体销毁、Observer 死亡、Epoch/Generation 失效）。重复 Refresh 同一集合不会重复 Enter 或 Leave，短时间反复进出边界按真实变更次数分别输出。
- 当已可见实体只是移动位置但仍满足过滤，更新完整 View 的 Transform，而不会产生重复 Enter/Leave。旧 View/Delta 内的 Tags 列表通过拷贝与 `Array.AsReadOnly` 冻结，旧客户端快照不会随 P24/P26 状态变化原地修改。
- P20 Grid 的 QueryNearby 提供候选排序和精确距离计算；P21 过滤只读取世界快照，成员 Identity 含 Generation。旧实体销毁后同 EntityId 新 Generation 在**一次** Refresh 中可同时得到旧身份 Leave 与新身份 Enter。
- P21 不创建 WoW 3.3.5a UpdateObject、UpdateMask、Packet、广播服务、Player Session。P23 网络编码以及 P60 Host 的授权/协议设计必须另行实现。

## 出口和证据要求

1. 单元：距离恰好边界、32 次重复出入、相同快照幂等、死亡/复活、Tag/Control 来源变化、销毁与同 ID 新代际、旧 Observer Generation 失效、历史 View 冻结、World/Epoch/版本连续性、双观察者不同可见性、容量拒绝不修改版本、非法过滤配置、旧 P18 租约及其它 World Guard。
2. 独立 `WorldVisibilityProbe` 通过真实 P18 WorldExecutor、P17 Lifecycle、P20 Spatial、P26 Tags/Control 测试 5 个端到端场景；成功输出 `P21_WORLD_VISIBILITY_INTEGRATION_PASS`。
3. GitHub Actions 从 10 个阻断 Job 扩展为 **11 个 Job**（新增 `world-visibility` 的 .NET 10.0.404 locked restore/Release Probe/Artifact），必须持续通过原有 10 Job、P02–P05、275+ 单元与 16 Architecture。实现 HEAD、独立 REVIEW HEAD、正式 ACCEPTED HEAD 各自 11/11 SUCCESS 后方可正式 C 验收。

## 风险与尚未完成

- 当前 View/Delta 是 **Pull + Refresh**，并非实体移动或销毁时立即推送。必须由未来世界 Tick/Visibility 编排在生命周期变更后调用 Refresh，才能将 Enter/Leave 输出给下一层；未建立真正的连接会话或带宽调度，不能称为网络广播方案。
- 视图按观察者缓存，不带全局世界 SnapshotRevision；将来引入网络包流水序号时需显式组合 World/Epoch/Observer/Version 和收敛逻辑。单次 Refresh 的 P17/P20/P26 快照在 P18 单写范围内采集，不跨多个并发世界。
- P21 C 没有 P23 真正网络消息、P25 Stats、P27 Movement、P58/P60 Host、P62 全链路 Replay 或 P64 多观察者性能预算验收。1024 可见实体和 4096 m 属于当前有界策略，不应伪称生产扩展性已验证。

## 初次真实验证

- #196 因 CS0165（观察者快照初始化）和 CA1859（私有集合返回类型）在 Release 编译阶段 FAIL；保留原记录并遵守分析门禁修复。
- #197（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43916141338）**11/11 SUCCESS**，290 Unit / 16 Architecture；P21 五类真实边界/状态/退休/观察者隔离/背压探针成功，原有十 Job 全绿。
- 独立四视角 REVIEW 文件：[P21_VISIBILITY_REVIEW](../reviews/P21_VISIBILITY_REVIEW.md)；当前独立 REVIEW HEAD 及 ACCEPTED HEAD CI 未执行，不提前验收。

## 独立审查与正式 C 验收

[P21 四视角审查](../reviews/P21_VISIBILITY_REVIEW.md) 的独立 REVIEW HEAD `fe790ec26a11699e38e2b54f917268faf3dd5c98` CI #198 十一 Job 全绿，确认当前 C 必需阻塞 0，P21 正式 ACCEPTED。状态提交本身仍要独立 11 Job 核验。P23 网络广播、P60 Host 和 P64 规模负载不在此结论内。
