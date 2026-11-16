# 当前进度

更新时间：2026-11-17
分支：main（直接更新，不创建分支）
当前阶段：P21 — Visibility / Relevance（IN_PROGRESS，目标 C）
正式 ACCEPTED：P00–P20、P24(B)、P26(C)。P21 尚未验收。

## 基线真实证据

- P26 Control/Ownership/Tags：实现 #192、独立 REVIEW #193、正式 ACCEPTED #194、归档 #195 全部 10/10 SUCCESS（275 Unit、16 Architecture）。
- P26 归档 HEAD `58e84431d5acf70a743f5a3ffc6a38be90bf7e65`；CI #195：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43912821041。
- P21 直接依赖 P20 Spatial 和 P26 标签、控制，均已接受。

## P21 当前代码（首次 CI 尚未验证）

- 新增 Runtime.World 的 `WorldVisibilityFilter`、`WorldVisibilitySet`、`WorldVisibilityView`、`WorldVisibilityDelta`。
- 只读 P20 QueryNearby 和 P26 Tags/Control/Life；仅在 P18 当前同步 World Scope 中发布带 World/Epoch/Observer+版本的 Enter/Leave；无实体位置双写、无 WoW UpdateObject/Packet。
- 初次实现包含有界距离/成员过滤、跨边界、状态变化、退休与代际复用、观察者死亡、旧租约拒绝及快照冻结的独立 Unit 和真实 P21 WorldVisibilityProbe。
- CI 新增第 11 个 `world-visibility` 阻断 Job，维持原有十 Job 回归。
- 详细入口：[P21](progress/P21.md) / [WORLD_VISIBILITY](modules/WORLD_VISIBILITY.md)。

## 下一安全动作

观察首次 P21 实现 HEAD 十一个真实 Job CI，失败先修复并留存；再独立 REVIEW 和正式 ACCEPTED HEAD 各自跑十一个 Job。P21 仍未 ACCEPTED。
