# 当前进度

更新时间：2026-11-17
分支：main（直接更新，不创建分支）
当前阶段：P21 — Visibility / Relevance（ACCEPTED，目标 C）
正式 ACCEPTED：P00–P20、P24(B)、P26(C)。P21(C) 已正式 ACCEPTED。

## 基线真实证据

- P26 Control/Ownership/Tags：实现 #192、独立 REVIEW #193、正式 ACCEPTED #194、归档 #195 全部 10/10 SUCCESS（275 Unit、16 Architecture）。
- P26 归档 HEAD `58e84431d5acf70a743f5a3ffc6a38be90bf7e65`；CI #195：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43912821041。
- P21 直接依赖 P20 Spatial 和 P26 标签、控制，均已接受。

## P21 正式 ACCEPTED

- 新增 Runtime.World 的 `WorldVisibilityFilter`、`WorldVisibilitySet`、`WorldVisibilityView`、`WorldVisibilityDelta`。
- 只读 P20 QueryNearby 和 P26 Tags/Control/Life；仅在 P18 当前同步 World Scope 中发布带 World/Epoch/Observer+版本的 Enter/Leave；无实体位置双写、无 WoW UpdateObject/Packet。
- 初次实现包含有界距离/成员过滤、跨边界、状态变化、退休与代际复用、观察者死亡、旧租约拒绝及快照冻结的独立 Unit 和真实 P21 WorldVisibilityProbe。
- CI 新增第 11 个 `world-visibility` 阻断 Job，维持原有十 Job 回归。
- 详细入口：[P21](progress/P21.md) / [WORLD_VISIBILITY](modules/WORLD_VISIBILITY.md)。

## P21 真实 CI

- 初始 P21 HEAD `34fbcb0` CI #196（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43915892879）在编译阶段 FAIL：CS0165 观察者快照局部变量 definite assignment、CA1859 私有 Normalize 具体返回类型。均已修复且保留失败记录。
- 稳定实现 HEAD `0af03e5a96b743e2ecce85dcc6335e9d95fa2901` CI #197（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43916141338）**11/11 SUCCESS**，290/290 Unit、16/16 Architecture、P02–P05 Gate 与真实 P21 WorldVisibilityProbe 五类场景成功，原有十项 Job 均 SUCCESS。
- 新增独立四视角审查：[P21 Review](reviews/P21_VISIBILITY_REVIEW.md)，独立 REVIEW HEAD `fe790ec26a11699e38e2b54f917268faf3dd5c98` CI #198（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43916929617）11/11 SUCCESS，P21 C 正式 ACCEPTED；正式 ACCEPTED HEAD `b16fa376bc1a323e18b2fd16bf841128f553cb3c` CI #199（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43917430402）**11/11 SUCCESS**，P21 C 正式收口。

## 下一安全动作

P21 实现 #197、独立 REVIEW #198、正式 ACCEPTED #199 各自 11/11 SUCCESS。下一按路线图启动 P22 — Spawn / Respawn（P16.F/P19/P20/P24 前置已 ACCEPTED）；P21 Pull+Refresh 不代表网络推送或 P64 性能达标。

## P21 三阶段真实验收归档

- 稳定实现 HEAD `0af03e5a96b743e2ecce85dcc6335e9d95fa2901`：CI #197 11/11 SUCCESS。
- 独立 REVIEW HEAD `fe790ec26a11699e38e2b54f917268faf3dd5c98`：CI #198 11/11 SUCCESS。
- 正式 ACCEPTED HEAD `b16fa376bc1a323e18b2fd16bf841128f553cb3c`：CI #199 11/11 SUCCESS，290 Unit、16 Architecture，P21 五类真实 Probe 和原有十项回归全绿。
- 初次 #196 CS0165/CA1859 编译 FAIL 均已保留并严格修复；未降低分析/架构/负向 Gate。
- 本次归档 HEAD 提交自身 CI 尚待独立核验，不影响前三轮实际验收结果。下一 P22 尚未实施。
