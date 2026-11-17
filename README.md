# LegendsOfTheDungeon

LegendsOfTheDungeon（副本霸主）是参考 AzerothCore 可泛化机制设计的 **.NET 10 / C# 模块化游戏服务端核心**，并不是 WoW 3.3.5a 协议兼容服务器。

采用模块化单体、World Actor 独占世界执行上下文、Ports & Adapters；以可复现自动化测试验证能力。阶段状态、技术范围及限制以中文规范和实际 CI 证据为准。

## 当前进度

- P00–P23、P24(B)、P25(C)、P26(C)、P27(C)、**P28(C)** 均正式 ACCEPTED。
- **P28 — Server Motion / Path 已验收**：十种服务器运动模式、P23 路径版本与取消/抢占、P20 唯一 Transform/Spatial 更新、P25 速度上限、P26 控制、P27 会话隔离及 P19 v6 状态摘要。稳定实现 [CI #235](https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44016152259)、独立审查 [CI #236](https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44016560072)、正式验收 [CI #237](https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/44016970168) **各 16/16 SUCCESS**，367 Unit / 16 Architecture。
- **下一阶段：P29 — Movement Validation（尚未开始）**。真实 3D 动态 NavMesh、P29 检测和处罚、P60 生产网关、P62 全链路重放、P64 长稳性能仍分别待完成。

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

当前工作基线：P28 C 已正式 ACCEPTED（#235/#236/#237 分别 16/16 SUCCESS），main 直推，不建分支。下一任务 P29 Movement Validation。最终归档 HEAD 的 CI 将独立校验。
