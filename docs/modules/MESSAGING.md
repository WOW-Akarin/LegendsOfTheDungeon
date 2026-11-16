# Command / Query / Event 协作契约

> 首次落地阶段：P09。实时状态与 CI 证据见 [P09 进度](../progress/P09.md)。

## 分层

- **Kernel**：CommandId、MessageVersion、ICommand/IQuery/IQuerySnapshot、DomainEvent、DeferredEventBuffer 与不可变事件 envelope/batch。
- **Runtime.Abstractions**：Command/Query Handler 与 Dispatcher 端口、DeferredEvent Publisher 端口。
- **Runtime.Core**：显式 Handler 注册表与 Command/Query 分发实现。
- **Modules**：定义自己的 Command/Query/Event、Handler 和只读 Query Snapshot；不通过 Service Locator 获取依赖。

Handler 注册是运行时组合职责，不属于 Kernel。P09 不引入通用 `GetService(Type)`、`Resolve<T>` 或全局静态 registry。

## Command

Command 必须携带：

- 非零 `CommandId`；
- 正整数 `MessageVersion`。

Handler 通过 `ICommandHandler<TCommand,TResult>` 返回 `Result<TResult>`。业务预期失败使用 `KernelError`；取消保留标准 OperationCanceledException；不可预期异常向运行时上层传播。

每次 dispatch 创建独立 `DeferredEventBuffer`。Handler 返回成功表示其领域提交点已经完成，此后 dispatcher 才 commit queue 并 publish；失败/取消/异常都 discard queue。

## Query

Query 携带正整数 `MessageVersion`。返回值必须实现 `IQuerySnapshot`，表示与可变领域状态断开的只读快照。Handler 不接收 DeferredEventQueue，因此 Query 路径没有领域事件写入口。

`IQuerySnapshot` 是语义标记，不可能单独靠 CLR interface 证明深层不可变；模块 Review 仍需检查公开 setter、可变 collection 和领域实体引用。P09 示范快照只有 get-only scalar/string 属性，并由测试反射检查。

## DeferredEvent

> 命名说明：路线图中的 `DeferredEventQueue` 概念在 C# API 中命名为 `DeferredEventBuffer`，以满足启用的 CA1711 API 命名规则；数量/深度/因果/提交语义不变。


`DeferredEventBuffer` 绑定当前 CommandId。根事件记录：

- CommandId 因果来源；
- 从 1 开始的 EventSequence；
- null parent sequence；
- depth 0；
- Event 自己的 MessageVersion。

派生事件必须以当前 queue 真实产生的 envelope 为 parent，记录 parent sequence，depth+1。外部伪造/其他 queue 的 parent 被拒绝。

队列有两个独立上限：

- maximumEvents：整个 Command 派生树最多事件数；
- maximumDepth：根事件 depth=0，可允许的最大派生深度。

Commit 返回不可变快照 batch 并封闭队列；Discard 清空并封闭。封闭后继续 enqueue/commit 会失败。

## 注册、未知消息与故障

- 同一 Command CLR 类型只能注册一个 Handler；重复注册立即失败。
- 同一 Query CLR 类型只能注册一个 Handler；重复注册立即失败。
- 未知 Command 返回 `runtime.command.unknown`。
- 未知 Query 返回 `runtime.query.unknown`。
- publisher 在领域提交后失败时异常向上传播，不能伪装成可安全重试的业务失败；后续持久化/outbox 策略由 P11 以后阶段承担。

契约 ID / 版本：`runtime.messaging.v1`
