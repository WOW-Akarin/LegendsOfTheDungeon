# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `151c9fff55612df3e60411d796f740814c4eed69` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P05 / Kernel Strong IDs 与 Result / IN_PROGRESS / B

## 目标与成功条件

交付 Kernel 第一批真实公共值契约：强类型 ID、EntityGeneration、WorldEpoch、Result/Error/ValidationIssue；证明不同 ID 编译期不可混用，default/0、非法文本、溢出被拒绝，Error Code 可以稳定序列化，并保持 Kernel 无高层依赖。

## 开始前状态与已有未提交修改

- P00–P04 已 ACCEPTED。
- P05 开始 HEAD：`151c9fff55612df3e60411d796f740814c4eed69`。
- Kernel 当前只有 AssemblyMetadata，没有业务类型。
- 当前执行容器无 dotnet；继续使用 GitHub Actions .NET 10.0.404 runner 作为真实编译/测试证据。
- 用户要求直接更新 `main`，不创建任务分支。

## 已完成与证据链接

- P03 已强制 Kernel 的架构依赖边界。
- P04 已强制新 Public API 的 XML 文档缺失构建失败。
- [Kernel 基础值契约](modules/KERNEL.md) 已定义 Strong ID、empty/default、canonical 文本和 Error Code 边界。
- [P05 阶段证据](progress/P05.md) 已建立。

## 当前停点

P05 第一批实现已准备落库：9 个强类型值、Result/Error/ValidationIssue、Unit 边界测试、三类跨 ID 编译失败 Gate 和 CI evidence。

## 下一步（最多 5 项）

1. 提交 P05 Kernel primitives、Unit 与 compile-negative Gate。
2. 读取首次 CI 的 P04 XML/Analyzer/Unit/Architecture/compile-negative 真实结果。
3. 若失败，从第一个真实错误修复，不添加 ID 转换或 suppress。
4. 记录 Unit 数量、CS1503 负例和 kernel-contract-gate artifact。
5. 独立自审契约/序列化/default 语义后进入 REVIEW。

## 修改文件及原因

新增 Kernel primitives、Unit、compile-negative Gate、CI artifact，并同步 BUILD/P05 证据。

## 契约、数据所有权与 ADR 变化

新增 A1 公共值契约 `kernel.primitives.v1`；无可变状态所有权变化、无跨模块依赖变化，因此不新增 ADR。

## AC 参考与已知差异（适用时）

不适用。Strong IDs/Result 是项目通用基础值契约，不从 AzerothCore 复制实现或测试向量。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P04 依赖状态 | GitHub Actions，2026-11-16 | PASS | progress/P04.md |
| P05 Unit | 尚未实现 | NOT_RUN | 待 CI |
| P05 compile-negative | 尚未实现 | NOT_RUN | 待 CI |
| P03/P04 回归 | 尚未执行 P05 HEAD | NOT_RUN | 待 CI |

## 失败、阻塞、风险与未验证假设

- CLR struct 的 `default(T)` 不可禁止；必须通过 empty sentinel + 边界拒绝，而不是声称类型系统能消灭 default。
- 不提供统一 `ItemId`，用 Definition/Instance 两个强类型直接满足物品身份分离要求。
- Error Message 不作为机器稳定契约；Code 才是稳定序列化键。

## 恢复步骤

1. 读取 PROJECT_SPEC、ARCHITECTURE §3、CONTEXT、PROGRESS、modules/KERNEL.md 和 progress/P05.md。
2. 核对 main HEAD 与 P05 起始 SHA `151c9fff55612df3e60411d796f740814c4eed69`。
3. 打开 `src/Kernel`、P05 Unit 与 `eng/verify-kernel-contract-gates.sh`。
4. 读取最新 CI；失败时从第一个真实编译/Analyzer/Test 诊断修复，不降低 P03/P04 Gate。
5. 所有出口证据满足后进入 REVIEW。

## 不应重复执行的操作

不要加入统一 ItemId 或不同 ID 之间的转换运算符；不要用 suppress/NoWarn 绕过 P04。
