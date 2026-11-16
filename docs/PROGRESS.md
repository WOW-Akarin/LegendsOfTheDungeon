# 当前进度

更新时间：2026-11-17
分支：main（直接更新，不创建分支）
当前阶段：P26 — Control / Ownership / Tags（REVIEW，目标 C）
已正式 ACCEPTED：P00–P20、P24(B)；P26 正在独立 REVIEW

## 最近真实验证

- P20 Spatial/Cell/Grid 实现 #185、独立 REVIEW #186、正式 ACCEPTED #187、归档 #188 均九 Job SUCCESS，257 Unit / 16 Architecture。
- P20 归档 HEAD `ec80ea7a7ce2755a70807f750c0a4fe9d4f01f0a`；CI #188 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43905616426。
- P21 Visibility 的直接依赖 **P20 + P26**；P20 已接受、P26 是当前解除阻塞的工作。

## P26 实现 CI 已通过、独立审查进行中

- 新增 Entities.Control/Tags/Relationship 三种窄能力，控制/标签多个来源使用带 EntityGeneration 的类型区分句柄。
- P17 WorldInstance 新增按来源授予/撤销、Tag 查询、四关系赋值与跨角色循环拒绝、退休入向关系清理。
- P19 WorldTick 摘要 schema v3 包含控制/标签来源明细与四角色关系，避免旧哈希漏掉未来可撤销状态。
- 新增 Unit 和真实 P18 WorldControlProbe，第十个独立 CI Job。实现 HEAD `7689a25d88924511cb508127e25d1aa38cc06e43` CI #192（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43910885713）**10/10 SUCCESS**：275 Unit / 16 Architecture，P26 五类真实探针与全部旧集成通过。
- 阶段记录：[P26](progress/P26.md) / [实体能力契约](modules/WORLD_CONTROL.md)。

## 真实实现 CI 记录

- #189：CA1711 Flags 枚举命名失败，构建 Gate 阻断。
- #190：272 Unit 中 1 个跨能力句柄误撤销测试失败，修复 Tag.Revoke 句柄 Kind 校验。
- #191：272 Unit / 16 Architecture，10/10 全绿；#192：增补同 Tick 不同来源 Hash、令牌不复用、永久 Tag 容量回归，**275 Unit / 16 Architecture，10/10 全绿**。
- 独立四视角审查：[P26 Review](reviews/P26_CONTROL_OWNERSHIP_REVIEW.md)。本次 REVIEW HEAD CI 尚未完成，不提前 ACCEPTED。

## 下一安全动作

核验独立 REVIEW HEAD 10 Job CI，随后正式 ACCEPTED HEAD 再次跑完整 10 Job；完成 P26 后 P21 Visibility 才解锁。
