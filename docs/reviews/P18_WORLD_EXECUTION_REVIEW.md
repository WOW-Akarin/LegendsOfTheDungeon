# P18 — WorldMailbox / WorldExecutor 独立四视角 Review

- 状态：ACCEPTED
- 代码审查基线：5d51a41920110471f31e8efd3c157d9c3386f243
- 实现 CI #159 SUCCESS（六 Job、210 Unit、16 Architecture）
- 时间：2026-11-16

## 功能正确性

真正的同步世界写入租约要求同一 Scope 对象、同一世界 Epoch、同一执行线程、同一活动 RunBatch 以及匹配的实体 Generation；绝不能仅靠 AsyncLocal 表面继承获得写权限。跨世界和重入失败均可在 Unit 中真实触发。外部结果必须持有世界签发的一次性 RequestSeq + Generation + Epoch + Owner 票据，入队和执行前各验证一次；关键专属车道不被普通流量占据。

## 架构与状态所有者

Runtime.Abstractions 定义窄 Port，Platform.WorldExecution 使用 P12 有界 Mailbox 与 P60.F Security.Contracts，不对游戏 Domain 强加具体 Adapter 引用。执行器管理技术世界 Epoch/Entity Generation 而非真实 Entity 生命周期和领域数据；真实领域状态必须由 P17/P24 和 P19 后续接入。可信 Producer 作为组合根托管能力，不能交给 Socket/HTTP 非可信入口。

## 故障恢复与安全

空/本地测试身份默认 Denied，P60.F 生产身份没有可信签发者；执行器还需在运行前重新校验授权。Task.Run 与回调外泄 Context 不能继续写。回执伪造、重复、Entity 退休、Epoch 过期失败；错误命令导致 Faulted/Closed，不会悄悄继续执行尚未处理命令，不谎称已经原子回滚。报告明示 Executed/Stale/Denied/Remaining，队列容量与票据容量受上限约束。

## 证据与局限

- 实现 #159：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43868940960 — locked Release、210/210 Unit、16/16 Architecture、WorldExecutionProbe 实际线程 5 项、原 P11/P13/P14/P15/P16 全集成与 P02–P05 Gate 均 PASS。
- #151–#155 失败与修复保留真实日志；#156–#159 连续全部六 Job SUCCESS。
- P18 的技术 Guard 不是 P17 真实 Entity 系统或 P19 逻辑 Tick；P60 生产签发/授权实现仍未交付；完整线上闭环与 P64/P66 性能尚不具证据。

**结论：P18 技术 C 范围必需未解决项 0；独立 REVIEW HEAD 04c862c 的 CI run #160 六 Job 全部 SUCCESS，P18 正式 ACCEPTED。** 正式 ACCEPTED HEAD 95d2e30 的 run #161 六 Job 同样完整 SUCCESS。
