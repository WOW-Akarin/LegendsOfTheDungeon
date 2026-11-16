# ADR-0001：通用核心范围与 AzerothCore 固定研究基线

- 状态：Accepted
- 日期：2026-11-16
- 决策级别：S2 / A2
- 关联阶段：P00

## 背景

LegendsOfTheDungeon 需要研究 AzerothCore 的服务器机制，但项目目标不是复刻 WoW 3.3.5a 客户端兼容服务器。若不先冻结边界，后续很容易把 Opcode、DBC、具体职业/副本等专有内容错误带入 Core。

## 决策

### 1. Core 准入测试

候选能力进入 Core 前必须同时回答：

1. 移除 WoW 上下文后是否仍成立？
2. 是否存在当前真实服务器运行场景，而不是为了“将来可能用”预建？
3. 是否可通过替换数据或策略复用于其他游戏？
4. 是否能符合现有状态所有权、执行上下文和模块依赖规则？

前三项任一为否，则默认不进入 Core；第四项不满足时先修正设计，不通过访问 Internal、万能 Manager 或固定成功结果绕过。

### 2. 明确纳入范围

Kernel、Platform、静态数据、World Runtime、实体、移动、战斗、Ability/Effect/Status、Items/Inventory/Loot、Progression/Objectives、AI、交互/在线/小组、通用场景、脚本扩展、安全管理、持久化、模拟与工程工具。

### 3. 明确排除

- WoW 客户端兼容与 3.3.5a / Build 12340
- WoW Opcode、数据包布局、SRP6、HeaderCrypt
- DBC/DB2 强制兼容与 AC 数据库逐表兼容
- 具体职业、种族、天赋、Glyph
- 具体副本、Boss
- LFG、PvP、Warden
- 完整 WoW Guild、Mail、Auction、Chat 玩法

通用传输帧、认证接口、可配置装备槽、通用场景仍可属于 Core，但不得因此引入 WoW 专用格式。

### 4. AC 固定研究基线

冻结 https://github.com/azerothcore/azerothcore-wotlk 的：

`17f9f9d5003708136c798ab4ef8c1b9762251bad`

研究记录必须引用完整 SHA、文件和符号。升级上游基线属于显式变更，不允许把浮动 `master` 当成既有研究证据。

## 范围变更规则

改变 Core 范围、核心模型、所有权或验收标准时：

1. 必须有明确需求依据；
2. 创建或更新 S2 ADR，写替代方案与代价；
3. 说明对现有阶段、契约、测试和迁移的影响；
4. 同步 PROJECT_SPEC / ARCHITECTURE / ROADMAP 中负责该事实的基线；
5. 保留旧研究与验收证据，不静默改写历史。

## 后果

优点：研究可追溯，WoW 特定内容不会因 AC 存在而自动进入核心；后续 P63 可复现相同参考源码。

代价：不能直接按 AC C++ 目录逐文件翻译；每个研究主题都需要额外的通用性判断和差异记录。
