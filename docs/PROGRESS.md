# 当前进度

更新时间（含时区）：2026-11-16 +08:00  
代码基线 / Branch / 工作区：任务开始 `8bbedd6bf96a224317b032d802d11e774fca1ff7` / `main` / GitHub 直接提交；当前执行容器无 dotnet SDK  
阶段 / 任务 / 状态 / 目标完成度：P09 / Command / Query / Event / REVIEW / B

## 目标与成功条件

建立跨模块协作基础：Command→Result、只读 Query、DeferredEvent 因果/序号与提交后发布；Runtime.Core 提供显式 Handler 注册/分发，重复注册与未知消息失败，派生事件队列有数量/深度上限，且不存在全局 Service Locator。

## 开始前状态与已有未提交修改

- P00–P08 已 ACCEPTED。
- P09 开始 HEAD：`8bbedd6bf96a224317b032d802d11e774fca1ff7`。
- 当前生产项目只有 Kernel 与 Runtime.Abstractions；P09 将新增职责明确的 Runtime.Core。
- 当前执行容器无 dotnet；GitHub Actions .NET 10.0.404 runner 继续作为真实编译/测试环境。
- 用户要求直接更新 `main`，不创建任务分支。

## 已完成与证据链接

- [P08 证据](progress/P08.md) 已验收 Random。
- [Command / Query / Event 协作契约](modules/MESSAGING.md) 已建立。
- [P09 阶段证据](progress/P09.md) 已建立。

## 当前停点

P09 最终实现 CI run #56 已全绿：Unit 97/97、Architecture 16/16、P04/P05 Gate 全部通过；取消 enqueue 后 discard、Service Locator 搜索和 Runtime.Core 架构/lock 自审通过，当前进入独立 REVIEW。

## 下一步（最多 5 项）

1. 独立读取 REVIEW HEAD 的 Kernel Messaging、Runtime 端口/dispatcher 与 MessagingTests。
2. 核对 REVIEW HEAD 自身 Unit 97+、Architecture、P04/P05 Gate 全绿。
3. 核对 Runtime.Core P03 白名单、Solution、lock graph 与真实引用一致。
4. 重查无 Service Locator、Kernel 无 Handler registry、Query Snapshot 无可变公开成员。
5. 无必需问题后将 P09 ACCEPTED，再进入 P10。

## 契约、数据所有权与 ADR 变化

新增 A1 协作契约 `runtime.messaging.v1`。DeferredEventQueue 只拥有单次 Command 执行的临时事件队列状态，不拥有领域状态；Handler 注册由 Runtime.Core 所有。无架构方向变化，不新增 ADR。

## AC 参考与已知差异（适用时）

不适用。P09 是项目通用协作模型，不复制 AzerothCore dispatcher/event bus 实现。

## 验证

| 命令/检查 | 环境与时间 | 结果 | 报告 |
|---|---|---|---|
| P08 依赖状态 | GitHub Actions，2026-11-16 | PASS | progress/P08.md |
| P09 Unit | GitHub Actions，2026-11-16 | PASS：97/97 | run 43813919670 / artifact 14201089332 |
| Architecture/P04/P05 回归 | GitHub Actions，2026-11-16 | PASS：Architecture 16/16 + 文档/Strong ID Gate | run 43813919670 |

## 风险与未验证假设

- Runtime.Core dispatcher 是类型专用 registry，不得演变成任意 Service Locator。
- Query Snapshot 的深层不可变性仍需模块 Review；marker interface 不能单独证明。
- P09 只保证“Handler 成功返回后才 publish”；不提供数据库 outbox 或跨进程 exactly-once。
- Event publisher 提交后失败会向上传播；恢复/outbox 属于后续 Persistence 阶段。

## 恢复步骤

1. 读取 ARCHITECTURE、CONTEXT、PROGRESS、modules/MESSAGING.md 与 progress/P09.md。
2. 核对 main HEAD 与 P09 起始 SHA `8bbedd6bf96a224317b032d802d11e774fca1ff7`。
3. 打开 Kernel Messaging、Runtime.Abstractions Messaging、Runtime.Core dispatcher 和 MessagingTests。
4. 读取最新 CI；失败时从第一个 Analyzer/Compiler/Test 真实诊断修复，不添加 Service Locator 或 suppress。
5. 出口证据满足后进入 REVIEW。

## 不应重复执行的操作

不要把 Handler registry 放进 Kernel；不要增加全局静态 dispatcher/service provider；不要让 Query 返回可变领域实体。
