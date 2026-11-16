# 项目文档中心

LegendsOfTheDungeon 的设计、决策、研究、进度和验收事实统一维护在本目录。计划不能替代实现证据；阶段状态以 PROGRESS 和阶段归档为准。

## 当前入口

- [PROJECT_SPEC.md](PROJECT_SPEC.md)：范围、开发规则、变更与验收制度
- [ARCHITECTURE.md](ARCHITECTURE.md)：依赖、状态所有权、执行与一致性语义
- [DEVELOPMENT_ROADMAP.md](DEVELOPMENT_ROADMAP.md)：P00–P67 阶段依赖、交付物和出口
- [UPSTREAM.md](UPSTREAM.md)：AzerothCore 固定研究基线、来源与许可规则
- [CONTEXT.md](CONTEXT.md)：当前恢复摘要
- [PROGRESS.md](PROGRESS.md)：当前任务、停点、风险和验证
- [decisions/](decisions/)：架构与范围决策记录
- [research/azerothcore/](research/azerothcore/)：AzerothCore 研究记录与模板
- [progress/](progress/)：阶段验收证据
- [templates/](templates/)：CONTEXT、PROGRESS 与阶段归档模板
- [RECOVERY_WORKFLOW.md](RECOVERY_WORKFLOW.md)：恢复顺序、状态迁移、更新触发点与 BLOCKED 规则
- [operations/BUILD.md](operations/BUILD.md)：SDK、NuGet 锁文件与统一验证入口
- [reviews/ARCHITECTURE_RESPONSIBILITY_REVIEW.md](reviews/ARCHITECTURE_RESPONSIBILITY_REVIEW.md)：高 fan-in / Common / Utils / Manager 类职责聚集人工审查

## 当前状态

当前里程碑：M0 基线。  
当前阶段：P04 — 中文注释与文档质量 Gate，**IN_PROGRESS**。P00–P03 已 ACCEPTED；实时状态以 [PROGRESS.md](PROGRESS.md) 为准。

P00 固定的 AzerothCore 研究 SHA：

`17f9f9d5003708136c798ab4ef8c1b9762251bad`

## 协作与提交规则

- 按 PROJECT_SPEC → ARCHITECTURE → ROADMAP → ADR/模块文档逐层解释约束。
- 任务实施、功能检查、架构自审、失败恢复检查、最终验收分开记录。
- 不把计划、模拟实现或未运行命令写成已完成证据。
- 当前用户明确要求持续直接更新 `main`，因此本项目当前工作流不创建任务分支。
- 复制、改写或分发上游代码/数据/测试向量前必须核查实际来源许可与归属要求。
