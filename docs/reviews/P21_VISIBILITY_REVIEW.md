# P21 — Visibility / Relevance 独立四视角审查

- 状态：REVIEW，目标 C，**尚未正式 ACCEPTED**
- 稳定实现 HEAD：`0af03e5a96b743e2ecce85dcc6335e9d95fa2901`
- 实现 CI #197：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43916141338，11/11 SUCCESS
- 独立 REVIEW HEAD：本次审查提交，CI NOT_RUN；正式 ACCEPTED HEAD：NOT_RUN
- 直接依赖 P20 Spatial / P26 Control+Tags 已 ACCEPTED；P17 世界生命周期、P18 同步租约、P19 Tick 亦已验收。保持 main 直推、无分支。

## 1. 功能与确定性视图

- `WorldVisibilitySet` 只绑定一个完整 World/Epoch/ObserverId/Generation，不把同 EntityId 新代际当成原观察者。按 P20 `QueryNearby` 先三维精确闭区间距离筛选，后以 P26 状态值检查 RequiredTags、ExcludedTags、RequireAlive、ExcludedControls，永远不把观察者本人加入 Visible。
- `WorldVisibilityView` 冻结观察者、World/Epoch 和 per-Set SnapshotVersion；列表按 EntityId 升序，`WorldVisibilityMember` 只含完整不可变身份、按值 Transform、Life、控制标志、拷贝后只读的有效标签，不包含 EntityCapabilities 或其他可变对象。
- `WorldVisibilityDelta` 包含 PreviousVersion、SnapshotVersion、Entered 成员新视图、Left 全代际身份及最新完整 View。若成员集合不变、只移动或 Tag 改变且仍被过滤允许，只有 View 更新、没有虚假重复 Enter；若真实条件翻转则一次 Enter/Leave。成功计算版本 +1，无变化刷新仍 +1，失败不发布版本。
- 真实 Unit 检查闭区间正好在 32m 的进入、12 次越界进出；P21 Probe 在 32 次反复进出中严格检查每一次且不重复。Tag 永久/来源、多个 Stun 控制来源、死亡/复活、观察者死亡/移除、Despawn 和同 EntityId 新 Generation 都验证正确 Enter/Leave。
- 历史 View、Delta 与 Member 的字符串标签只读集合独立于世界，旧快照不会随新位置、Tag、控制改变原地变化。双观察者有独立的快照版本与距离领域。

## 2. 架构与唯一状态真相

- 新生产代码仅在 `Runtime.World` 现有项目内加入一个窄派生索引类，无新的生产项目或跨层引用。空间与 Transform 单一真相仍由 P17/P20/P24 管理，P26 保存标签/控制，P21 **只缓存重算后的 observer view**，不会自行重定位或改写 Tag/Life。
- `Refresh` 在发布前调用当前世界的 P18 `IWorldWriteContext.DemandWorld`，持有有效同步租约；外部异步/过期或错误 World/Epoch 的作用域均拒绝。构造器也校验 Observer 与 World/Epoch 匹配，Mutation 不能来自其他线程或客户端 socket 直接调用。
- 独立 ViewSet 的写入通过 P18 Actor Scope 串行，私有锁仅防止读者看见 `_current` 替换的中途状态。当前后续 Host 必须明确只把 Refresh 能力交给可信 WorldTick 或生命周期命令，不开放给不可信网络。
- 没有加入 WoW UpdateMask、UpdateObject、Packet、广播适配器、隐藏 Host/DB 引用或增大 P03 架构白名单；16/16 Architecture PASS。

## 3. 安全、资源与生命周期风险

- 每 Set 一次最多发布 1024 个 Visible 成员，最大查询半径 4096m，最多 64 个配置 Tag，永久/临时 Tag 匹配遵循 P26 ASCII lowercase 与 Ordinal。错误配置构造时拒绝；单次超额在构建后发布前 throw，Current 与版本保持不变，不谎报部分 Delta。未捕获异常的 P18 Action 仍按 P18 fail-closed，Unit 在可信命令内捕获可恢复容量拒绝。
- WorldInstance 进入 Faulted 时拒绝新的视图计算；旧 View 可供诊断但**必须视为历史版本**。观察者/目标死亡或销毁后在**下一次合法 Refresh** 发出 Leave，而不是立即从所有观察者 View 里原位删除。该 Pull + Refresh 语义需要 P19 Tick/后续 Host 定期驱动；未接入真实推送和流量规划，不能承诺即时网络广播。
- WorldVisibilitySet 的 SnapshotVersion 是**每观察者/每 Set**私有单调序号，必须与 World/Epoch/Observer 联合使用；它**不**是 P19 Tick、P17 世界写入次数或全世界全局 SnapshotRevision。未来如果实现网络重连、视图持久化、Packet 序号需另起阶段设计。
- 重算每次需要 P20 QueryNearby 候选与集合差分；P20 当前可能扫描已分配 Cell 桶，因此很多观察者/大范围时 CPU/分配压力尚未经过 P64。不能从功能 Unit 成功推导 10,000 Entities × N observers/50ms 达标。
- 后续 P23 网络协议、P27 Movement 输入与事件采样、P60 Host 认证、P62 全链路回放、P64 性能/长稳仍各有单独验收范围。

## 4. 真实验证和证据留存

| 检查 | 稳定实现 CI #197 |
|---|---|
| Linux .NET 10.0.404 Release + locked restore | PASS |
| Unit（新增 P21 15 项） | **290/290 PASS** |
| P03 Architecture | **16/16 PASS** |
| P02/P03/P04/P05 文档/正反构建、架构门禁 | PASS |
| P21 边界 32 次往返 + 幂等 Enter/Leave Probe | PASS |
| P21 Tag/Life/Stun 变化 + 历史 Snapshot 冻结 Probe | PASS |
| P21 Despawn/代际复用/观察者失效 Probe | PASS |
| P21 双观察者独立 World/Epoch/Version Probe | PASS |
| P21 容量拒绝不发布/旧 Scope 拒绝 Probe | PASS |
| 原有 10 Job（P17–P20/P26 + 网络/DB/诊断） | PASS |
| 实现 HEAD 完整 CI | **11/11 SUCCESS** |
| 独立 REVIEW HEAD | NOT_RUN |
| 正式 ACCEPTED HEAD | NOT_RUN |

失败透明保留：#196 初始实现 CS0165 观察者 definite assignment、CA1859 具体返回类型要求，构建 FAIL；严格修复后 #197 十一 Job 全绿，没有关闭或放宽任何 Gate。

## 审查结论与下一动作

P21 C 路线图要求的 VisibilitySet、Distance/Tag/State Filter、Enter/Leave Delta、只读 View、World/Epoch/快照版本与边界重复/状态变化/实体销毁/快照不原位变更均由真实 CI 验证，未发现当前 C 出口必需修复的代码阻塞。

**但是独立 REVIEW HEAD 尚未执行自己的 CI，当前仍是 REVIEW。** 待本审查 HEAD 11 Job 全绿，再提交正式 ACCEPTED 并独立核验 ACCEPTED HEAD；P21 完成不代表客户端协议、推送或 P64 性能已实现。
