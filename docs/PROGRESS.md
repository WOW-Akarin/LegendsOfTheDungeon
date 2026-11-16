# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `34446eb8aec0a57fcfa4f0aa1ea68dc1d15eea08` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P06 / Kernel Geometry / IN_PROGRESS / B

## 目标与成功条件

实现 Position / Transform / Vector / Distance / Bounds 和方向角值契约；固定右手坐标、Z 向上、米/弧度、Yaw canonical 范围，覆盖距离/范围、NaN/Infinity、零长度向量、算术溢出、边界距离和 Adapter 风格序列化回读，同时保持 Geometry 纯数学、无 Runtime/World 依赖。

## 开始前状态与已有未提交修改

- P00–P05 已 ACCEPTED。
- P06 开始 HEAD：`34446eb8aec0a57fcfa4f0aa1ea68dc1d15eea08`。
- Kernel 已有 Strong IDs 与 Result/KernelError/ValidationIssue。
- 当前执行容器无 dotnet；GitHub Actions .NET 10.0.404 runner 继续作为真实编译/测试环境。
- 用户要求直接更新 `main`，不创建任务分支。

## 已完成与证据链接

- [P05 证据](progress/P05.md) 已验收 Kernel primitives。
- [Kernel 基础值契约](modules/KERNEL.md) 已加入 `kernel.geometry.v1` 约定。
- [P06 阶段证据](progress/P06.md) 已建立。

## 当前停点

P06 首轮实现 CI run #31 已全绿：Unit 48/48、Architecture 16/16、P04/P05 Gate 全部通过。自审发现 Vector 默认容差不应借用带米单位命名的 Distance 常量，正在改为无单位 Vector.DefaultTolerance。

## 下一步（最多 5 项）

1. 提交 Vector 无单位默认容差修正并运行最终实现 CI。
2. 读取首次 CI 的 Analyzer/P04/Unit/Architecture 真实结果。
3. 若失败，从第一个真实诊断修复，不放宽非有限数或溢出规则。
4. 核对角度归一、边界距离、零向量和 JSON wire 回读实际测试证据。
5. 独立自审纯数学依赖与数值语义后进入 REVIEW。

## 修改文件及原因

启动 P06 阶段证据与 Geometry 契约说明；业务实现将在下一提交落库。

## 契约、数据所有权与 ADR 变化

新增 A1 公共纯值契约 `kernel.geometry.v1`；无可变状态所有权变化、无跨模块依赖变化，不新增 ADR。

## AC 参考与已知差异（适用时）

不适用。P06 使用通用欧氏几何和项目坐标约定，不复制 AzerothCore 公式或代码。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P05 依赖状态 | GitHub Actions，2026-11-16 | PASS | progress/P05.md |
| P06 Unit | 尚未实现 | NOT_RUN | 待 CI |
| P03/P04 回归 | 尚未执行 P06 HEAD | NOT_RUN | 待 CI |

## 失败、阻塞、风险与未验证假设

- 不给世界范围拍脑袋设置任意最大坐标；只拒绝非有限输入，并对有限算术产生的非有限结果显式报 OverflowException。
- default 几何值是合法零值，不套用 Strong ID 的 empty 规则。
- JSON 只作为测试中的 Adapter 回读手段，不把 System.Text.Json 特性引入 Kernel。

## 恢复步骤

1. 读取 PROJECT_SPEC、ARCHITECTURE §3、CONTEXT、PROGRESS、modules/KERNEL.md 和 progress/P06.md。
2. 核对 main HEAD 与 P06 起始 SHA `34446eb8aec0a57fcfa4f0aa1ea68dc1d15eea08`。
3. 打开 `src/Kernel` Geometry 文件和对应 Unit。
4. 读取最新 CI；失败时从第一个真实编译/Analyzer/Test 诊断修复，不 suppress P04/Analyzer。
5. 出口证据满足后进入 REVIEW。

## 不应重复执行的操作

不要把 Runtime/World 查询加入 Geometry；不要在 Kernel 添加 JSON/DB 序列化属性；不要用过大容差掩盖数学错误。
