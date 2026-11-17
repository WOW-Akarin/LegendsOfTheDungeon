# LegendsOfTheDungeon

LegendsOfTheDungeon（副本霸主）是参考 AzerothCore 可泛化机制设计的 **.NET 10 / C# 模块化游戏服务端核心**，并不是 WoW 3.3.5a 协议兼容服务器。

采用模块化单体、World Actor 独占世界执行上下文、Ports & Adapters；以可复现自动化测试验证能力。阶段状态、技术范围及限制以中文规范和实际 CI 证据为准。

## 当前进度

- P00–P22、P24(B)、P26(C) 已正式验收。
- 已验收：**P23 — Terrain / Collision / Navigation Ports（ACCEPTED）**。三类通用查询 Port 与真实平面障碍参考 Adapter 已实现；稳定实现 CI #213、独立 REVIEW CI #214、正式 ACCEPTED CI #215 各 13/13 SUCCESS（322 Unit、16 Architecture），P23 B+C 正式验收完成。
- 当前：**P25 — Stats / Resources / Modifier（REVIEW）**。已加入基础/派生属性、来源修饰与资源账本的实现候选，稳定实现 CI #220 14/14 SUCCESS（334 Unit/16 Architecture），独立 REVIEW HEAD CI 尚未验证。
- 后续 P27/P28 Movement/Path、真实 3D NavMesh、网络 Host、全链路 Replay、性能长稳测试并未完成。

## 文档

- [项目文档入口](docs/README.md)
- [当前进度与真实证据](docs/PROGRESS.md)
- [P23 阶段记录](docs/progress/P23.md)

## 开发验证

- SDK：`global.json` 固定 .NET 10.0.404。
- 正向验证：`./eng/verify.sh`。
- 负向门禁：`./eng/verify-negative-gates.sh`。
- 统一 GitHub Actions CI：严格 locked restore、Release、Unit、Architecture、负向质量门禁及独立集成探针，详见 [构建说明](docs/operations/BUILD.md)。

用户要求直接更新 `main`、不创建分支；仍按独立实现/REVIEW/ACCEPTED 三阶段分别保留真实 CI 证据。

下一阶段：P25 实现 CI → 独立审查 → 正式验收；之后根据依赖推进 P27。
