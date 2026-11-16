# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `6cbe721366901f61849f43d493b90c96a36eaa46` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P03 / Architecture Tests 第一批 / REVIEW / B（架构规则）

## 目标与成功条件

让生产项目边界由 CI 强制：实际项目引用必须匹配显式白名单；Domain 不得依赖 Platform Adapter/Host；模块不得依赖其他模块 Infrastructure/Internal 或任何 Host；生产项目引用图无环；公共 API 不得泄漏被禁止实现；`InternalsVisibleTo` 必须显式授权。每条自动规则同时具备合法样本与故意违规负例。

## 开始前状态与已有未提交修改

- P02 已 ACCEPTED，证据见 [progress/P02.md](progress/P02.md)。
- P03 开始 HEAD：`6cbe721366901f61849f43d493b90c96a36eaa46`。
- 当前 Solution 只有 2 个生产项目：Kernel、Runtime.Abstractions；不为测试规则提前创建空业务模块。
- 当前执行容器仍无 dotnet；继续使用真实 GitHub Actions .NET 10.0.404 runner 作为执行证据。
- 仍按用户要求直接更新 `main`，不创建任务分支。

## 已完成与证据链接

- P02 已建立 ArchitectureTests 项目并纳入 `eng/verify.sh` 和 CI。
- [ARCHITECTURE 2.3/2.4](ARCHITECTURE.md) 已定义项目依赖规则与自动检查原则。
- P03 路线图已明确要求正例、故意违规负例、真实依赖白名单、契约泄漏、IVT 与无环检查。

## 当前停点

P03 最终实现 CI run #12 已全绿：Build、Unit 3/3、Architecture 16/16、build/unit/architecture 三类故意失败 Gate 全部通过。当前进入独立 REVIEW。

## 下一步（最多 5 项）

1. 独立读取 REVIEW HEAD 的架构清单、RuleEngine、fixture、Assembly 扫描器与 CI 脚本。
2. 核对 P03-001～008 都有正例/故意违规负例，且真实生产图检查不依赖关键词。
3. 核对 REVIEW HEAD 自身 CI 全绿并上传 TRX。
4. 核对 P03/ARCHITECTURE/CONTEXT/README 状态与链接一致。
5. 无必需问题后将 P03 ACCEPTED，再进入 P04。

## 修改文件及原因

将新增架构清单、规则引擎、真实仓库扫描器、正/负例测试、职责审查文档，并扩展 Architecture 负向 Gate。

## 契约、数据所有权与 ADR 变化

P03 不改变业务状态所有权和公共业务契约。新增工程级架构清单与测试 Gate：真实 ProjectReference 白名单、公共 API/IVT 扫描、无环和职责聚集审查。无需新增 ADR。

## AC 参考与已知差异（适用时）

不适用。本阶段验证项目结构，不做 AzerothCore 行为对照。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P02 依赖状态 | GitHub，2026-11-16 | PASS | progress/P02.md |
| P03 run #8 build | GitHub Actions，2026-11-16 | FAIL：CA1854×1 / CA1859×4 | run 43803313873 |
| P03 真实 Architecture Tests | GitHub Actions，2026-11-16 | PASS：16/16 | run 43803757326 |
| P03 三类负向 Gate | GitHub Actions，2026-11-16 | PASS：build/unit/architecture 故意失败均被检出 | run 43803757326 |
| P03 故意违规负例 | 尚未实现 | NOT_RUN | 待 CI |

## 失败、阻塞、风险与未验证假设

- 当前只有两个生产项目，因此部分模块/Host 规则的生产正例空间有限；规则正确性必须靠故意违规 fixture 证明，不能为了测试制造无调用方生产项目。
- 公共 API 泄漏检查必须基于真实 Assembly 公共签名，不以类型名关键词代替。
- Common/Utils/中心 Manager 风险不能仅凭名字判断；自动 Gate 只检查跨模块依赖集中度/显式审查元数据，职责是否合理仍保留人工 Review。

## 恢复步骤

1. 读取 PROJECT_SPEC、ARCHITECTURE、CONTEXT、PROGRESS 和 [P03 证据](progress/P03.md)。
2. 核对 main HEAD 与 P03 起始 SHA `6cbe721366901f61849f43d493b90c96a36eaa46`。
3. 打开 `eng/architecture/` 与 ArchitectureTests，确认真实项目清单和当前失败 fixture。
4. 读取最新 GitHub Actions run；若失败，从第一个真实 architecture test 修复，不弱化规则。
5. P03 已进入 REVIEW；按 progress/P03.md 的当前停点做独立审查。

## 不应重复执行的操作

不要创建只为“凑规则”的空生产模块；不要用项目/类型名称关键词扫描代替依赖图和公共 API 分析。
