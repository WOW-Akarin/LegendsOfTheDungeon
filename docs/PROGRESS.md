# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `6cbe721366901f61849f43d493b90c96a36eaa46` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P03 / Architecture Tests 第一批 / IN_PROGRESS / B（架构规则）

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

P03 第一批 Architecture Test 实现已准备落库：显式生产项目清单、真实项目图/API/IVT 扫描、P03-001～008 规则、正/负例 fixture 和 Architecture 失败注入 Gate。下一步读取首次 CI。

## 下一步（最多 5 项）

1. 推送 P03 Architecture Test 实现并读取首次 GitHub Actions run。
2. 若编译/测试失败，从第一个真实错误修复，不删规则、不弱化 fixture。
3. CI 通过后记录实际 Architecture test 数量与三类负向 Gate。
4. 独立核对真实生产图、公共 API/IVT 扫描和无关键词误报原则。
5. 全部出口证据满足后进入 REVIEW。

## 修改文件及原因

将新增架构清单、规则引擎、真实仓库扫描器、正/负例测试、职责审查文档，并扩展 Architecture 负向 Gate。

## 契约、数据所有权与 ADR 变化

P03 不改变业务状态所有权和公共业务契约。新增的是工程级架构元数据/测试契约；若规则需要改变既有架构设计，再单独记录 ADR。

## AC 参考与已知差异（适用时）

不适用。本阶段验证项目结构，不做 AzerothCore 行为对照。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P02 依赖状态 | GitHub，2026-11-16 | PASS | progress/P02.md |
| P03 真实 Architecture Tests | 尚未实现 | NOT_RUN | 待 CI |
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
5. 所有正/负例、真实生产图和最终 HEAD CI 通过后进入 REVIEW。

## 不应重复执行的操作

不要创建只为“凑规则”的空生产模块；不要用项目/类型名称关键词扫描代替依赖图和公共 API 分析。
