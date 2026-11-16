# 当前进度

更新时间：2026-11-16 +08:00
分支：main（直接更新，不创建分支）
当前阶段：P17 — WorldInstance / Registry / Lifecycle（ACCEPTED，目标 C）
最近已验收：P00–P16、P18、P24(B)，P19 尚未验收

## 最近可靠证据

- P18 WorldExecutor ACCEPTED：六 Job 全绿，技术同步 Scope/双车道/回执/过期门禁。
- P24(B) ACCEPTED：实现 #163、独立 REVIEW #164、正式 ACCEPTED #165 及归档 #166 均六 Job 全绿，216/216 Unit、16/16 Architecture。
- P24 归档 HEAD：`94329e2c2e98d35eef8d7afc2614d9dc1629f597`；CI #166（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43887320098）六 Job SUCCESS。

## P17 已正式 ACCEPTED

- `Runtime.World` 实现世界生命周期和实体代际注册表，新增 World 级真实租约校验。
- 跨世界归属仅一处共享 Claim，不将实体可变实例暴露给其它 World；销毁保留旧代际 tombstone。
- `Remove/Despawn/Stop` 走有界同步清理，并调用 P18 RetireEntity 使旧回执失效；目标自动清理与注册表代际校验。
- 新建独立 P17 世界生命周期 Unit 和真实并行 Worker 集成 Probe；CI 从六 Job 增加为七 Job。
- [P17 阶段记录](progress/P17.md)、[技术契约](modules/WORLD_LIFECYCLE.md)。

## 本次真实实现 CI

- 实现 HEAD `15d974ab3fe3e2b863e02a9389a2d24b3f693935` run #172（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43891431383）：**七 Job 全部 SUCCESS**，230/230 Unit、16/16 Architecture；P17 并行 World / 过期回执 / 停机 / Faulted 4 类真实 Probe 全部 PASS，P11/P13/P14/P15/P16/P18 集成回归 PASS。
- 已记录实施过程中 #167–#170 的真实 FAIL，并逐项修复锁图、P03 共享职责、CI 路径及停机目标残留，不做测试或门禁豁免。
- 独立四视角审查：[P17 WorldInstance Review](reviews/P17_WORLD_LIFECYCLE_REVIEW.md)；独立 REVIEW HEAD `28dcac573e1ea024bdb78b34e3f2e939e1107382` 的 CI #173 七 Job SUCCESS（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43891977310）。正式 ACCEPTED HEAD `404381f6764e301218d4ef76cea499abb90d46f3` 的 CI #174 七 Job 全部 SUCCESS（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43892469009），P17 C 正式收口。

## 下一安全动作

P17 已按规定完成实现 #172、独立 REVIEW #173、正式 ACCEPTED #174 全部七 Job 验证。下阶段进入 P19 WorldTick / Scheduler，需独立完成确定性 Tick、多世界 Worker 调度及负载证据，不提前宣称 P19 PASS。尚不声称 P19/P20/P27/Host/正式性能完成。

## P17 归档验证基线

- 正式 ACCEPTED HEAD：`404381f6764e301218d4ef76cea499abb90d46f3`，CI run #174 7/7 SUCCESS，230 Unit / 16 Architecture。
- 过程失败 #167–#170 透明保留；实现稳定 #171/#172、独立 REVIEW #173、正式 ACCEPTED #174 均七 Job 全绿。
- 当前归档提交仅记录真实证据，其自身 CI 在提交后另行验证，不冒充已验证。
