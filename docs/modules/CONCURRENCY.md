# P12 — Platform Concurrency 契约

## 架构分工

- Platform.Concurrency 是无业务状态的技术层，不能为 WorldExecutor 建立有效世界写租约，不能将 Task.Run / 异步任务视为世界可变状态执行权。P18 才接入 WorldMailbox、Epoch/Generation Guard 与世界所有权。
- BoundedMailbox<T> 提供普通/关键回执双独立容量；任何车道满则显式 Busy，关闭后返回 Closed，已接纳数据仍可 Drain；同一车道 FIFO。
- 每次 Drain 通过 maximumItems 限制单轮负载；maximumCriticalBurst 控制两车道同时有任务时的优先接纳比，跨多轮维持连续关键处理次数，防止普通任务饥饿。关键回执没有无限缓存。
- LogicalScheduler<T> 不执行回调，只存储 (DueTick, 接纳序号, 信封)，不创建系统 Timer 或依赖真实墙钟；严格容量限制、取消释放容量、单 Tick 最大派发数。世界逻辑计时器执行语义由后续 P19 实现。
- BoundedWorkChannel 用 bounded Channel 作为唯一排队能力，Offer 返回 Accepted/Busy/Closed/Duplicate，不允许无界积压；SnapshotOutstanding 列出已接纳未结束的 Queued/Running ID。
- BackgroundWorkerPool 使用固定任务消费者，逐项捕获异常、受控取消、失败记录数量有界且不包含原始异常/参数/连接串。关闭先停止接纳，再排空；超过期限发送取消，返回尚未完成的 ID/状态。超时不代表已经执行/已回滚，调用方必须根据持久账本或其他可靠机制恢复未完成任务。

## 需要验证的边界

- 线程安全和不超过容量；双车道在普通满时关键仍可接纳；关键 burst 后普通可执行。
- 单调 Stable Id 和逻辑 Tick 顺序；取消的调度不再执行。
- 后台任务异常之后的其他任务继续执行；逐项取消不会泄漏；关闭有界、超时列出 Queued/Running。
- 本模块不包含系统级性能验收；P64 将在目标部署负载测量吞吐和内存。

P12 阶段状态与证据以 [阶段记录](../progress/P12.md) 为准。
