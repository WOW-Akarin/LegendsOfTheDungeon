# 当前上下文

更新时间：2026-11-16 +08:00  
分支：`main`（用户要求持续直接更新，不创建工作分支）  
当前里程碑：M1 基础  
当前阶段：P16 — ACCEPTED（P17 NOT_STARTED；P18/P24 待实施）

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

P00/P01 文档基线已验收；P02 建立 .NET 10 Solution；截至 P10 已验收 Kernel、Runtime.Abstractions、Runtime.Core、Platform、Security.Contracts 五个生产 Assembly、Unit/Architecture 测试入口、NuGet lock file 与 CI Gate，并已由 locked-mode CI run #4 全绿验证。尚无游戏业务功能、游戏网络集成或性能通过证据；P11 已有真实 PostgreSQL 18 技术层集成证据。

## 当前任务与下一步

P00–P10 已 ACCEPTED。P11 已 ACCEPTED：实现基线 0a092260，run #72 Unit 109/109、Architecture 16/16、真实 PostgreSQL 18 Integration 全绿；独立 REVIEW HEAD 270022e 的 run #73 两个 job 全绿。ConnectionFactory、参数化命令、事务 Unknown、Migration、BoundedBulkLoader、P65.F 初始注入与幂等消歧已形成可复现证据。正式 ACCEPTED HEAD b395553 的 run #74 也已完整 SUCCESS。P12 已正式 ACCEPTED：实现基线 d9e22e4，run #83 Unit 123/123、Architecture 16/16、PG Integration 全绿；独立 REVIEW HEAD 48d5604 的 run #84 两个 Job SUCCESS。双车道 Mailbox、逻辑 Scheduler、有界 WorkChannel、有限 WorkerPool、超时未完成清单与 Abandoned 恢复标识已验证。正式 ACCEPTED HEAD 14f509c run #85 全绿，最终归档 HEAD 987b6e7 run #86 完整 SUCCESS。P13 已正式 ACCEPTED：实现基线 42952d3，run #99 Unit 139/139、Architecture 16/16、真实 TCP Loopback 10 项与 PostgreSQL 集成全绿；独立 REVIEW HEAD f49e599 的 run #100 三 Job SUCCESS。正式 ACCEPTED HEAD 8fc43bf 的 run #101 三 Job 完整 SUCCESS，P13 最终归档 HEAD f969109 的 #102 三 Job 也 SUCCESS。P14 已正式 ACCEPTED：实现基线 f4d7b6c run #112 四 Job SUCCESS（Unit 151/151、Architecture 16/16、真实 Worker 诊断 6 项、PostgreSQL/TCP 回归）；独立 REVIEW HEAD a496aadd run #113 四 Job 全绿。正式 ACCEPTED HEAD 220c5d9 的 run #114 四 Job 完整 SUCCESS，最终归档 HEAD fa7bb44 的 #115 四 Job SUCCESS。P15.J JSON 与 P15.S PostgreSQL 18 SQL 已实现：最终实现 HEAD 42b0679 的 run #131 五 Job 全绿，Unit 174/174、Architecture 16/16、7 项真实静态数据 PG 探针及 P11/P13/P14 集成均 PASS。P15 已正式 ACCEPTED：实现 #131（174 Unit/16 Architecture/PG 7项及五 Job SUCCESS）；独立 REVIEW HEAD ce19933 的 run #132 五 Job SUCCESS。正式 ACCEPTED HEAD 39918e8 的 run #133 五 Job 全部 SUCCESS；P16 NOT_STARTED。

## 阻塞与技术债

P11 必需实现阻塞 0。留存风险：模拟回执丢失而非物理断网；有界 Bulk 不是 COPY 性能导入；P65.F 不是完整 P65；业务持久化、真实 Host、生产认证、P64 性能均未完成。下一安全动作：从 P18 WorldMailbox / WorldExecutor 开始（P09/P12 直接依赖均满足），P17 尚受 P18/P24 阻塞；P16.F/P16.R 最终实现 HEAD 2b82a1b run #144 五 Job SUCCESS：191 Unit、16 Architecture、PG 18 P15/P16 9 项集成及既有 P11/P13/P14 回归均 PASS。独立 REVIEW HEAD 651b5ccb 的 CI #145 五 Job 完整 SUCCESS，P16 已正式 ACCEPTED；正式 ACCEPTED HEAD 7620aa8 的 run #146 五 Job 完整 SUCCESS。P16 引用校验/热重载仍未开始。真实业务 Host 埋点仍属后续阶段。生产认证和 WorldExecutor 仍未接线。P12 不替代 P18 WorldExecutor，性能和业务恢复仍待后续阶段。

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

- [P10 证据](progress/P10.md)
- [P11 阶段进度](progress/P11.md)
- [P12 阶段进度](progress/P12.md)
- [P13 阶段进度](progress/P13.md)
- [P14 阶段进度](progress/P14.md)
- [P15 阶段进度](progress/P15.md)
- [P16 阶段进度](progress/P16.md)
- [P16 技术契约](modules/CATALOG_RELOAD.md)
- [P16 四视角独立审查](reviews/P16_CATALOG_REVIEW.md)
- [P15 StaticData 模块契约](modules/STATIC_DATA.md)
- [P15 四视角审查](reviews/P15_STATIC_DATA_REVIEW.md)
- [P14 技术契约](modules/DIAGNOSTICS.md)
- [P14 独立审查](reviews/P14_DIAGNOSTICS_REVIEW.md)
- [P64.F 负载准备清单](operations/P64_BASELINE.md)
- [P13 传输模块契约](modules/NETWORKING.md)
- [P13 四视角审查](reviews/P13_NETWORKING_REVIEW.md)
- [P12 独立审查](reviews/P12_CONCURRENCY_REVIEW.md)
- [Concurrency 契约](modules/CONCURRENCY.md)
- [P11 独立代码审查](reviews/P11_PERSISTENCE_REVIEW.md)
- [Persistence 契约](modules/PERSISTENCE.md)
- [Platform 契约](modules/PLATFORM.md)
