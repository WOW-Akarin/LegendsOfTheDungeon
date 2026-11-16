# 当前进度

更新时间：2026-11-17
分支：main（直接更新，不创建分支）
当前阶段：P26 — Control / Ownership / Tags（ACCEPTED，目标 C）
已正式 ACCEPTED：P00–P20、P24(B)；P26(C) 已正式 ACCEPTED

## 最近真实验证

- P20 Spatial/Cell/Grid 实现 #185、独立 REVIEW #186、正式 ACCEPTED #187、归档 #188 均九 Job SUCCESS，257 Unit / 16 Architecture。
- P20 归档 HEAD `ec80ea7a7ce2755a70807f750c0a4fe9d4f01f0a`；CI #188 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43905616426。
- P21 Visibility 的直接依赖 **P20 + P26**；P20 已接受、P26 是当前解除阻塞的工作。

## P26 正式 ACCEPTED

- 新增 Entities.Control/Tags/Relationship 三种窄能力，控制/标签多个来源使用带 EntityGeneration 的类型区分句柄。
- P17 WorldInstance 新增按来源授予/撤销、Tag 查询、四关系赋值与跨角色循环拒绝、退休入向关系清理。
- P19 WorldTick 摘要 schema v3 包含控制/标签来源明细与四角色关系，避免旧哈希漏掉未来可撤销状态。
- 新增 Unit 和真实 P18 WorldControlProbe，第十个独立 CI Job。实现 HEAD `7689a25d88924511cb508127e25d1aa38cc06e43` CI #192（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43910885713）**10/10 SUCCESS**：275 Unit / 16 Architecture，P26 五类真实探针与全部旧集成通过。
- 阶段记录：[P26](progress/P26.md) / [实体能力契约](modules/WORLD_CONTROL.md)。

## 真实实现 CI 记录

- #189：CA1711 Flags 枚举命名失败，构建 Gate 阻断。
- #190：272 Unit 中 1 个跨能力句柄误撤销测试失败，修复 Tag.Revoke 句柄 Kind 校验。
- #191：272 Unit / 16 Architecture，10/10 全绿；#192：增补同 Tick 不同来源 Hash、令牌不复用、永久 Tag 容量回归，**275 Unit / 16 Architecture，10/10 全绿**。
- 独立四视角审查：[P26 Review](reviews/P26_CONTROL_OWNERSHIP_REVIEW.md)。独立 REVIEW HEAD `3f8e80ade8b42fd9b21e543ea4530835c7924718` 的 CI #193（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43911621160）10/10 SUCCESS，P26 C 已正式 ACCEPTED；正式 ACCEPTED HEAD `f0ee3d9601add1a799caacfa4d49c06a3179c149` 的 CI #194（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43912274627）**10/10 SUCCESS**，P26 C 完整验收。

## 下一安全动作

P26 实现 #192、独立 REVIEW #193、正式 ACCEPTED #194 全部十 Job SUCCESS。下一阶段按路线图启动 P21 — Visibility / Relevance：P20 + P26 两项前置依赖已 ACCEPTED，但 P21 的 VisibilitySet、过滤和 Delta 尚未实现。

## P26 三阶段实证归档

- 实现 HEAD `7689a25d88924511cb508127e25d1aa38cc06e43`，CI #192：10/10 SUCCESS。
- 独立 REVIEW HEAD `3f8e80ade8b42fd9b21e543ea4530835c7924718`，CI #193：10/10 SUCCESS。
- 正式 ACCEPTED HEAD `f0ee3d9601add1a799caacfa4d49c06a3179c149`，CI #194：10/10 SUCCESS，275 Unit / 16 Architecture，P26 五类真实 Probe 和全部旧集成回归成功。
- #189 CA1711、#190 真实跨种类撤销漏洞 FAIL 均透明留存，#191 首次完整 10/10、#192 源历史 Hash/Token/Tag 容量加固后再次 10/10。
- 当前归档提交自身的十 Job CI 须提交后另外核验，不能将文档提交等同于验证。
