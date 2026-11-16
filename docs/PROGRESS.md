# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `87f7e29467be9319a6359c724deda597985cdd81` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P08 / Kernel Random / REVIEW / B

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

P08 最终实现 CI run #49 已全绿：Unit 86/86、Architecture 16/16、P04/P05 Gate 全部通过；Seed 持久化、固定流向量与不稳定随机源审查通过，当前进入独立 REVIEW。

## 下一步（最多 5 项）

1. 独立读取 REVIEW HEAD 的 RandomState/Random/Probability/WeightedSelector/RandomStreams 与 RandomTests。
2. 核对 REVIEW HEAD 自身 Unit 86+、Architecture、P04/P05 Gate 全绿。
3. 核对 Seed/状态/算法版本与命名流派生版本均已锁定。
4. 重查 System.Random/Random.Shared/GetHashCode/时间戳/Guid Seed 路径为 0。
5. 无必需问题后将 P08 ACCEPTED，再进入 P09。

## 契约、数据所有权与 ADR 变化

新增 A1 公共随机契约 `kernel.random.v1`；PRNG 实例拥有自己的可变状态，但不拥有世界业务状态，不新增 ADR。

## AC 参考与已知差异（适用时）

不适用。P08 固定项目自己的 xoshiro256** / SplitMix64 规则，不复制 AzerothCore 随机实现。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P07 依赖状态 | GitHub Actions，2026-11-16 | PASS | progress/P07.md |
| P08 Unit | GitHub Actions，2026-11-16 | PASS：86/86 | run 43811342353 / artifact 14200686587 |
| P03/P04/P05/P06/P07 回归 | GitHub Actions，2026-11-16 | PASS：Architecture 16/16 + 文档/Strong ID Gate | run 43811342353 |

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
5. P08 已进入 REVIEW；按 progress/P08.md 当前停点做独立审查。

## 不应重复执行的操作

不要使用 System.Random、Random.Shared、Guid、时间戳、运行时 HashCode 或非稳定 string hash 作为核心确定性随机源/Seed。
