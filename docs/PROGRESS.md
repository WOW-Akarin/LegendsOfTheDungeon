# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `adf443506a9e6361f43c70c590d74b031c8d2695` / `main` / 通过 GitHub 直接提交，无本地未提交工作区  
阶段 / 任务 / 状态 / 目标完成度：P00 / 范围冻结与 AC 研究基线 / ACCEPTED / 文档基线

## 目标与成功条件

冻结通用 Core 范围、固定 AC Commit、落地来源与许可规则、研究模板、SDK 固定策略、首个真实数据库和初始部署/性能验收配置，并形成可复核证据。

## 开始前状态与已有未提交修改

开始时仓库仅有根 README 与 docs 下三份设计基线及旧 docs/README。未发现代码、Solution、UPSTREAM、ADR、研究记录、CONTEXT、PROGRESS 或阶段证据。任务开始分支为 `main`；按用户要求不创建分支。

## 已完成与证据链接

- [UPSTREAM](UPSTREAM.md)：固定 AC SHA 与许可/来源规则。
- [ADR-0001](decisions/ADR-0001-core-scope-and-upstream-baseline.md)：Core 准入、排除项、范围变更规则。
- [ADR-0002](decisions/ADR-0002-engineering-and-release-baseline.md)：.NET/SDK、PostgreSQL、部署资源和性能档位。
- [AC Reference 模板](research/azerothcore/REFERENCE_TEMPLATE.md)。
- [P00 阶段证据](progress/P00.md)。

## 当前停点

P00 已完成最终自审并 ACCEPTED；P01 尚未开始。

## 下一步（最多 5 项）

1. 开始 P01 — 开发规范、上下文与断线恢复制度。
2. 按 P01 验收项正式固化 CONTEXT/PROGRESS/阶段归档模板。
3. 检查状态枚举、恢复步骤、围栏和相对链接。
4. 完成 P01 自审与阶段证据后再进入 P02。
5. P02 时实测 .NET 10 SDK 并创建 global.json。

## 修改文件及原因

见 P00 阶段证据中的交付物清单。

## 契约、数据所有权与 ADR 变化

没有代码或公共运行时契约。新增 ADR-0001 与 ADR-0002；未改变 ARCHITECTURE 的状态所有权模型。

## AC 参考与已知差异（适用时）

固定 AC 基线 `17f9f9d5003708136c798ab4ef8c1b9762251bad`。当前仅建立研究制度，尚未声称任何机制通过 AC 实际运行对照。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| GitHub 上游仓库/branch 元数据核查 | GitHub，2026-11-16 | PASS | UPSTREAM.md |
| PROJECT_SPEC/ARCHITECTURE/ROADMAP P00 人工交叉审阅 | 2026-11-16 | PASS | progress/P00.md |
| dotnet restore/build/test | P00 尚无 Solution | N/A | P00/P01 文档阶段不强求 |

## 失败、阻塞、风险与未验证假设

- P02 尚未实测可用 .NET 10 SDK，因此没有伪造 `global.json` SDK 版本。
- P11 尚未运行 PostgreSQL 真实集成；PostgreSQL 18 是目标选择，不是通过证据。
- P64.F 尚未冻结活跃比例、命令速率和绝对性能阈值；P00 的 4 vCPU/8 GiB 与中档负载是目标环境。
- 尚未进行 AC 实际运行参考验证。

## 恢复步骤

1. 读取 PROJECT_SPEC、CONTEXT、PROGRESS。
2. 读取 ARCHITECTURE 与 DEVELOPMENT_ROADMAP 的 P00/P01。
3. 核对 main 当前 HEAD 与本文件记录的任务起始基线。
4. 打开 progress/P00.md 查看验证矩阵与未解决项。
5. P00 ACCEPTED 后从 P01 开始，不直接跳到工程骨架。

## 不应重复执行的操作

无数据库迁移、数据修复或其他有副作用操作。不要重新选择浮动 AC master 替换既有固定 SHA，除非按 ADR 流程升级基线。
