# 当前上下文

更新时间：2026-11-17；GitHub `WOW-Akarin/LegendsOfTheDungeon`；main 直推、不创建分支。
项目：通用 .NET 10 游戏服务器；非 WoW 3.3.5a 客户端兼容。架构基线：模块化单体、World Actor 独占同步 Scope、领域唯一真相、P03 严格依赖边界。

## 前置阶段

P00–P20、P24(B)、P26(C) 已正式 ACCEPTED。最新 P26 归档 HEAD `58e84431d5acf70a743f5a3ffc6a38be90bf7e65`，CI #195 10/10 SUCCESS（275 Unit/16 Architecture）。P17 WorldInstance、P18 WorldExecutor、P19 确定性 Tick、P20 Spatial Grid、P26 控制/标签均具备真实验证。

## 当前 P21

**P21 — Visibility / Relevance（ACCEPTED，目标 C）**，依赖 P20 + P26 已满足，首次代码 HEAD 尚无 CI 通过证据。

- `Runtime.World.WorldVisibilitySet` 为每个完整 Observer EntityIdentity 私有的派生有界可见集合；不占有可变实体或第二套 Transform。真实 P18 World Scope 发布版本化只读 `WorldVisibilityView` 与 `WorldVisibilityDelta`。
- 查询从 P20 Nearby 空间候选按距离闭区间筛选，P26 Required/Excluded Tags、Life 与 Control 状态后按 EntityId 排序；观察者本人永不属于自身列表。
- 每次成功 Refresh 获得连续 per-observer SnapshotVersion 和 World/Epoch/Observer；重复刷新不重复 Enter/Leave。目标销毁或旧代际被替换、观察者退出/死亡、Tag/Control/位置跨界在下次 Refresh 输出正确 Delta；历史视图不被原地修改。
- 最多 1024 Visible、半径最大 4096m，容量溢出不会发布部分版本；Faulted 世界拒绝刷新，P18 过期租约不能写。
- 新增 Unit、真实独立 WorldVisibilityProbe、第 11 个阻断 Job。实现 #197、独立 REVIEW #198 均 11 Job SUCCESS，正式 ACCEPTED HEAD 需独立十一个 Job CI。

## 当前审查与下一安全动作

- 初次 CI #196 CS0165/CA1859 编译 FAIL 已修复，稳定实现 #197 11 Job 全绿，相关记录保留于 [P21](progress/P21.md)。
- 独立四视角审查：[P21 Review](reviews/P21_VISIBILITY_REVIEW.md)。独立 REVIEW HEAD 已 #198 十一 Job SUCCESS，正式 ACCEPTED，状态提交 HEAD CI 尚需单独核验。

## 不属于 P21 验收

目前只实现 Pull + Refresh，不是即时网络推送。P23 Packet/客户端编码、P27 Movement、P60 Host 授权、P62 Replay 和 P64 高负载尚未通过。P21 独立审查 #198 已 11 Job 全绿，P21 正式 ACCEPTED；P21 完成后按路线图推进后续已满足依赖的阶段。

入口：[PROGRESS](PROGRESS.md)、[P21](progress/P21.md)、[WORLD_VISIBILITY](modules/WORLD_VISIBILITY.md)、[DEVELOPMENT_ROADMAP](DEVELOPMENT_ROADMAP.md)、[ARCHITECTURE](ARCHITECTURE.md)。
