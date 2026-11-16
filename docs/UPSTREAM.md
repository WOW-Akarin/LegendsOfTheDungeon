# 上游研究基线

> 状态：P00 冻结基线  
> 研究日期：2026-11-16  
> 本文只记录研究来源，不把 AzerothCore 的 WoW 专用协议或玩法纳入 LegendsOfTheDungeon Core。

## AzerothCore 固定基线

| 项目 | 值 |
|---|---|
| 上游仓库 | https://github.com/azerothcore/azerothcore-wotlk |
| 上游默认分支 | `master` |
| 固定 Commit | `17f9f9d5003708136c798ab4ef8c1b9762251bad` |
| Commit 时间 | 2026-11-16T00:45:15Z |
| 获取方式 | GitHub 公共仓库/REST 元数据；后续源码研究必须按固定 SHA 获取 |
| 仓库许可标识 | GPL-2.0 |
| Legends 用途 | 研究可泛化的服务器状态机、算法结构、运行机制和失败语义 |

固定 SHA 的目的，是保证研究记录和测试向量可复现。后续即使 AC `master` 前进，已有研究记录仍引用本基线；需要升级时必须新增基线历史并评估已有研究与测试的影响。

## 来源与许可规则

1. 默认优先采用“行为/思想研究 + 独立 C# 设计”，不机械翻译 C++ 类或文件。
2. 每个研究主题必须记录 Commit、File、Function/符号、通用机制、WoW 特定部分、Legends 映射和已知差异。
3. 直接复制、改写或分发上游代码、数据、测试向量前，必须再次核对具体来源文件及其许可/归属要求。
4. “改写为 C#”不自动消除原许可义务；如发生直接代码复用，必须在对应研究记录和提交中明确标注复用方式与归属。
5. 第三方子模块、生成数据和非 AC 自有来源必须按其实际许可单独核查，不能只依赖仓库根许可结论。

## 研究路径

研究记录统一放在 `docs/research/azerothcore/`。模板见 [REFERENCE_TEMPLATE.md](research/azerothcore/REFERENCE_TEMPLATE.md)。

## 基线更新历史

| 日期 | Commit | 原因 | 影响 |
|---|---|---|---|
| 2026-11-16 | `17f9f9d5003708136c798ab4ef8c1b9762251bad` | P00 首次冻结 | 后续 AC 研究默认以此 SHA 为准 |
