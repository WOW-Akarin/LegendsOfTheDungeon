# 当前进度

更新时间：2026-11-17
分支：main（直接更新，不创建分支）
当前阶段：P26 — Control / Ownership / Tags（IN_PROGRESS，目标 C）
已正式 ACCEPTED：P00–P20、P24(B)；P26 尚未验收

## 最近真实验证

- P20 Spatial/Cell/Grid 实现 #185、独立 REVIEW #186、正式 ACCEPTED #187、归档 #188 均九 Job SUCCESS，257 Unit / 16 Architecture。
- P20 归档 HEAD `ec80ea7a7ce2755a70807f750c0a4fe9d4f01f0a`；CI #188 https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43905616426。
- P21 Visibility 的直接依赖 **P20 + P26**；P20 已接受、P26 是当前解除阻塞的工作。

## P26 实现中（CI 尚待验证）

- 新增 Entities.Control/Tags/Relationship 三种窄能力，控制/标签多个来源使用带 EntityGeneration 的类型区分句柄。
- P17 WorldInstance 新增按来源授予/撤销、Tag 查询、四关系赋值与跨角色循环拒绝、退休入向关系清理。
- P19 WorldTick 摘要 schema v3 包含控制/标签来源明细与四角色关系，避免旧哈希漏掉未来可撤销状态。
- 新增 Unit 和真实 P18 WorldControlProbe，第十个独立 CI Job。当前不得声称测试通过。
- 阶段记录：[P26](progress/P26.md) / [实体能力契约](modules/WORLD_CONTROL.md)。

## 下一安全动作

观察 P26 首次实现 HEAD 10 Job CI，遇 FAIL 先修复并归档，独立 REVIEW 和正式 ACCEPTED 分别跑自己的 10 Job。完成 P26 才进入 P21 Visibility；不越过依赖。
