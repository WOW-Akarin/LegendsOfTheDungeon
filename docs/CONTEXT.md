# 当前上下文

更新时间：2026-11-17；main 直推，无分支。
项目：通用 C#/.NET10 游戏服务器，非 WoW 3.3.5a 客户端兼容；上游 AzerothCore 固定 SHA `17f9f9d5003708136c798ab4ef8c1b9762251bad` 只作通用机制参考。
架构：模块化单体、P18 WorldExecutor 独占同步写租约、P17 WorldInstance 唯一 Registry、P24 Transform 唯一空间真相、P20 派生 Spatial/Cell/Grid。

## 当前阶段

P00–P20 与 P24(B) 已 ACCEPTED。P20 归档 HEAD `ec80ea7a7ce2755a70807f750c0a4fe9d4f01f0a`，CI #188 九 Job SUCCESS（257 Unit/16 Architecture）。

正在实施 **P26 Control / Ownership / Tags（ACCEPTED，目标 C）**，尚无本次 CI PASS。P21 Visibility 要求 P20 + P26，不能跳过依赖。

## P26 边界

- 领域 Entities.Core 明确 Control/Tags/Relationships 单一写所有者；临时来源带 World/Epoch/EntityId/Generation + 序号与种类；四类 Owner/Creator/Controller/Summoner 不共享角色槽位。
- WorldInstance 负责 P18 Scope、活跃代际校验、禁止跨角色关系环、销毁全部入向引用；只导出冻结的 Tag、Control 与关系快照，供未来 P21 Visibility。
- P19 状态 Hash 升级到 schema v3，记录来源分布及序号，不把双来源 Stun 当成单来源 Stun。
- CI 从 9 Job 扩为 10 Job，新增真实 `WorldControlProbe`。实现 #192、独立 REVIEW #193、正式 ACCEPTED #194 均十 Job SUCCESS，P26 C 完整验收。
- 生产 Host/网络授权 P60、P21 Visibility Enter/Leave、P25 Stats、P62.F 完整回放、P64 性能仍未完成；未预设通过结论。

## 下一安全动作

P26 实现、独立审查、正式验收已全部 10/10 SUCCESS；开始下一独立阶段 P21 Visibility / Relevance（P20 + P26 前置已经满足），需独立实现 Enter/Leave Delta、Snapshot Version 与过滤测试，不提前标记 P21 PASS。入口：[PROGRESS](PROGRESS.md)、[P26](progress/P26.md)、[WORLD_CONTROL](modules/WORLD_CONTROL.md)、[ROADMAP](DEVELOPMENT_ROADMAP.md)、[ARCHITECTURE](ARCHITECTURE.md)、[恢复规范](RECOVERY_WORKFLOW.md)。
