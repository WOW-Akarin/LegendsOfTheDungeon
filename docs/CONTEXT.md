# 当前上下文

更新时间：2026-11-16 +08:00  
分支：`main`（用户要求持续直接更新，不创建工作分支）  
当前里程碑：M0 基线  
当前阶段：P09 — IN_PROGRESS

## 当前范围

LegendsOfTheDungeon 是通用 C# / .NET 10 游戏服务器核心。研究 AzerothCore 的可泛化机制，但不以 WoW 3.3.5a 客户端兼容为目标。Core 范围与排除项以 [PROJECT_SPEC](PROJECT_SPEC.md) 和 [ADR-0001](decisions/ADR-0001-core-scope-and-upstream-baseline.md) 为准。

## 架构不变量

- 模块化单体、World Actor、Ports & Adapters。
- 可变状态有唯一领域所有者；WorldExecutor 是世界状态唯一合法写入上下文。
- 同 World 串行，多 World 由有限工作线程调度。
- 外部输入/DB 回执/离线结果通过有界 Mailbox 进入世界。
- Domain 不依赖具体 DB Driver、Transport、Host。
- 禁止万能 Manager/Repository/Service Locator/Common/Utils 逃逸边界。

## AC 基线

- 仓库：https://github.com/azerothcore/azerothcore-wotlk
- 固定 SHA：`17f9f9d5003708136c798ab4ef8c1b9762251bad`
- 详细规则：[UPSTREAM](UPSTREAM.md)

## 当前工程决策

- 目标框架：.NET 10；SDK `10.0.404` 已由 GitHub Actions Linux x64 runner 实际验证并写入 global.json。
- 首个真实 DB：PostgreSQL 18。
- 初始发布环境：Linux x64 容器、服务器 4 vCPU / 8 GiB。
- 发布目标档位：10 World / 总计 10,000 实体。
- 初始逻辑 Tick：50 ms；未声称已通过性能测试。
- 详细依据：[ADR-0002](decisions/ADR-0002-engineering-and-release-baseline.md)。

## 已具备能力

P00/P01 文档基线已验收；P02 已建立可构建的 .NET 10 Solution、Kernel 与 Runtime.Abstractions 两个生产 Assembly、Unit/Architecture 测试入口、NuGet lock file 与 CI Gate，并已由 locked-mode CI run #4 全绿验证。尚无业务功能、数据库集成、网络集成或性能通过证据。

## 当前任务与下一步

P00–P08 已 ACCEPTED。P09 已启动：正在建立 Kernel 消息/DeferredEvent 契约、Runtime Handler/Dispatcher 端口与 Runtime.Core 显式注册/分发；禁止全局 Service Locator。

## 阻塞与技术债

当前无 P09 阻塞。Query 必须返回与可变领域状态断开的 Snapshot；事件发布只发生在 Command Handler 成功返回之后。后续必须实测的事项：P11 PostgreSQL 实际版本/digest 与事务语义、P64.F 绝对性能阈值。

## 关键入口

- [PROJECT_SPEC](PROJECT_SPEC.md)
- [ARCHITECTURE](ARCHITECTURE.md)
- [DEVELOPMENT_ROADMAP](DEVELOPMENT_ROADMAP.md)
- [PROGRESS](PROGRESS.md)
- [P00 证据](progress/P00.md)
- [P01 当前进度](PROGRESS.md)
- [恢复工作流](RECOVERY_WORKFLOW.md)
- [P01 证据](progress/P01.md)
- [P02 证据](progress/P02.md)
- [P03 证据](progress/P03.md)
- [P04 证据](progress/P04.md)
- [P05 证据](progress/P05.md)
- [P06 证据](progress/P06.md)
- [P07 证据](progress/P07.md)
- [P08 证据](progress/P08.md)
- [P09 证据](progress/P09.md)
- [消息协作契约](modules/MESSAGING.md)
- [Kernel 基础值契约](modules/KERNEL.md)
- [架构职责审查](reviews/ARCHITECTURE_RESPONSIBILITY_REVIEW.md)
- [文档质量审查](reviews/DOCUMENTATION_QUALITY_REVIEW.md)
- [构建基线](operations/BUILD.md)
- [UPSTREAM](UPSTREAM.md)
- [AC 研究模板](research/azerothcore/REFERENCE_TEMPLATE.md)
