# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `87f7e29467be9319a6359c724deda597985cdd81` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P08 / Kernel Random / IN_PROGRESS / B

## 目标与成功条件

实现 IRandom/DeterministicRandom/Probability/WeightedSelector，固定算法与版本并支持状态导出恢复；建立 Combat/Loot/AI 稳定独立随机流，证明插入 AI 抽样不改变 Loot 流，并明确概率与权重边界。

## 开始前状态与已有未提交修改

- P00–P07 已 ACCEPTED。
- P08 开始 HEAD：`87f7e29467be9319a6359c724deda597985cdd81`。
- Kernel 已具备 primitives、Geometry 与 Time。
- 当前执行容器无 dotnet；GitHub Actions .NET 10.0.404 runner 继续作为真实编译/测试环境。
- 用户要求直接更新 `main`，不创建任务分支。

## 已完成与证据链接

- [P07 证据](progress/P07.md) 已验收 Time。
- [Kernel 基础值契约](modules/KERNEL.md) 已加入 `kernel.random.v1` 设计。
- [P08 阶段证据](progress/P08.md) 已建立。

## 当前停点

P08 run #47 已执行 Unit 85/86；known-vector/状态恢复/权重/流隔离通过。唯一失败是测试试图用 decimal 精确表达 1/2^33 的错误假设，正在修测试向量，随机实现不变。

## 下一步（最多 5 项）

1. 提交 Probability 量化测试向量修复并读取下一次 CI。
2. 读取首次 CI 的 Analyzer/P04/Unit/Architecture/P05 Gate 真实结果。
3. 若失败，从第一个真实诊断修复，不改变算法版本来迎合测试。
4. 核对 known vector、状态恢复、概率/权重边界与 AI/Loot 隔离实际证据。
5. 独立自审无 HashCode/System.Random 后进入 REVIEW。

## 契约、数据所有权与 ADR 变化

新增 A1 公共随机契约 `kernel.random.v1`；PRNG 实例拥有自己的可变状态，但不拥有世界业务状态，不新增 ADR。

## AC 参考与已知差异（适用时）

不适用。P08 固定项目自己的 xoshiro256** / SplitMix64 规则，不复制 AzerothCore 随机实现。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P07 依赖状态 | GitHub Actions，2026-11-16 | PASS | progress/P07.md |
| P08 Unit | 尚未实现 | NOT_RUN | 待 CI |
| P03/P04/P05/P06/P07 回归 | 尚未执行 P08 HEAD | NOT_RUN | 待 CI |

## 风险与未验证假设

- 算法升级必须提升版本；不能在相同 AlgorithmId/Version 下改变序列。
- 不承诺密码学安全；该 PRNG 仅用于确定性游戏模拟。
- 流派生必须使用固定 salt；禁止 runtime HashCode。
- WeightedSelector 当前要求每项权重 > 0；若未来需要“禁用但保留项”，应在调用方过滤或另行版本化契约。

## 恢复步骤

1. 读取 ARCHITECTURE §3、CONTEXT、PROGRESS、modules/KERNEL.md 和 progress/P08.md。
2. 核对 main HEAD 与 P08 起始 SHA `87f7e29467be9319a6359c724deda597985cdd81`。
3. 打开 Kernel Random 源码与 `RandomTests.cs`。
4. 读取最新 CI；失败时从第一个 Analyzer/Compiler/Test 真实诊断修复，不改算法版本来迎合测试。
5. 出口证据满足后进入 REVIEW。

## 不应重复执行的操作

不要使用 System.Random、Random.Shared、Guid、时间戳、运行时 HashCode 或非稳定 string hash 作为核心确定性随机源/Seed。
