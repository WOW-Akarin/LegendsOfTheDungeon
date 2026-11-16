# 当前上下文

更新时间：2026-11-16 +08:00；分支：main
范围：通用 C#/.NET10 游戏服务器，不兼容 WoW 3.3.5a 客户端；上游 AzerothCore 固定 SHA `17f9f9d5003708136c798ab4ef8c1b9762251bad` 仅作机制参考。
基线：SDK 10.0.404、PostgreSQL 18、Linux x64；10 World/10,000 Entity/50ms Tick 仍是未验证的目标档位。

## 架构不变量

模块化单体 / World Actor / Ports & Adapters；WorldExecutor 是唯一世界同步写租约，DB/Network Worker 只能入队，进出消息必须有界并二次检查身份、Epoch、Generation、RequestSeq。领域核心不依赖具体 DB/Host/Transport。不可通过万能 Manager/Service Locator、假成功和跳过门禁完成任务。

## 阶段真相

P00–P16、P18、P24(B)、P17(C) ACCEPTED。P24 归档 HEAD `94329e2c2e98d35eef8d7afc2614d9dc1629f597` 的 CI #166 六 Job SUCCESS，现进入 P17（ACCEPTED，目标 C）。P17 直接依赖 P15.J/P18/P24，已满足。P19 仍依赖 P17 的正式验收。

P17 当前开发：`Runtime.World` 世界与实体 Registry、显式共享跨世界唯一归属、状态机、P18 世界级租约、同步清理接口、读快照和目标活跃代际校验，配合新的 `WorldLifecycleProbe` 七 Job CI。实现 HEAD `15d974a` CI #172、独立 REVIEW HEAD `28dcac5` CI #173 均七 Job SUCCESS（230 Unit、16 Architecture、P17 四类真实 Probe PASS），P17(C) 已正式 ACCEPTED。

风险：清理异常保留 Claim 交由未来 Host 恢复，不声称世界状态原子回滚；TransferNotSupported；真实 Timer/AI/Spatial/Tick、生产身份、性能等仍未完成。

下一安全动作：核验 P17 正式 ACCEPTED HEAD 七 Job；完成后推进 P19 WorldTick/Scheduler。P19 是下一阶段的独立工作，不得预先宣称通过。

入口：[PROGRESS](PROGRESS.md)、[P17 记录](progress/P17.md)、[世界生命周期契约](modules/WORLD_LIFECYCLE.md)、[ROADMAP](DEVELOPMENT_ROADMAP.md)、[ARCHITECTURE](ARCHITECTURE.md)、[恢复规则](RECOVERY_WORKFLOW.md)。
