# P18 — WorldMailbox / WorldExecutor 技术契约

## 状态所有权与项目边界

- `Runtime.Abstractions` 提供 `IWorldAction`（同步）、`IWorldWriteContext`（同步）和不可自行伪造真实权能的 `IWorldReplyTicket` 只读 Port；`Platform.WorldExecution` 实现每世界独占执行器，不定义游戏实体、世界登记中心或业务状态。
- 使用 P12 `BoundedMailbox` 普通/关键双车道与独立预算。每世界默认普通 256、关键 64、关键连发上限 8；显式限制普通 ≤4096、关键 ≤1024、burst ≤64、一次运行 ≤1024、活跃实体技术标记 ≤10,000、待回执票据 ≤4096。满队列返回 `Busy`，**不宣称已经执行业务**。
- 同世界不可重入：`RunBatch` 用排它的原子状态门禁，同期第二调用返回 `Busy` 且不移除队列消息；不同 `WorldExecutor` 可在有限 Worker 上并行。
- 生产 Assembly 的引用/锁文件及 P03 白名单均在实际 .slnx 内校验；没有 Host/真实领域写权限外泄，也不依赖 Npgsql、Transport Driver。

## 有效执行租约 / 防越权

- 同步 `IWorldAction.Execute` 调用期间才签发私有 Scope；`IWorldWriteContext.Demand(world, epoch, entity, generation)` 要求调用方提供**实体原本持有**的 WorldId/Epoch/EntityId/Generation，不得一律取 `context.World/Epoch` 并假装实体归属当前世界。
- 检查当前独占世界运行序列、作用中的 Scope 对象引用、同步执行线程 ID、环境上下文、当前世界 Epoch、实体活跃 Generation；任一缺失均失败。不以 AsyncLocal 或线程 ID 单独充当授权证明。
- 写上下文在回调结束时失效；`Task.Run` 即使继承 AsyncLocal，也无法通过同步线程与 Scope 的双重校验；在 Scope 结束后使用保存的引用同样失败。
- `BindEntity` 只登记技术 Generation，当前活跃 ID 重复 Bind 失败；退休后相同 ID 只能用更高 Generation；`RetireEntity` 立即使旧代际票据失效。`AdvanceEpoch` 只能在执行器闲置时单调推进，旧命令及票据作废。

## 外部不可信输入与异步回执

- **P60.F 前置门禁**：`OfferExternal` 对空身份、LocalTest 身份和默认拒绝授权始终返回 Denied。未来生产认证 Port 签发的 Production 身份还必须通过明确的 `ICommandAuthorizationPort`；接纳前与真正执行前二次核验，授权适配器异常 fail closed。**本阶段无生产身份签发器/真实 Host，不声称公网玩家已可授权写世界**。
- 可信组合根显式保管 `TrustedWorldProducer`。持有此端口仍**只可 Offer 或 OfferReply**，无法直接获得 `IWorldWriteContext`；端口绝不能交给不可信网络调用方。
- 外部 DB/Network Worker 只接收由活动世界同步租约签发的 `IWorldReplyTicket`；票据关联精确 WorldId/Epoch/EntityId/Generation/单调 RequestSequence，真实实现绑定本世界私有 Owner 与引用身份。伪造、跨世界、重复、过期请求、过期 Epoch/Generation 均拒绝。
- 工作结果走关键车道，每次执行前再次核对 ticket 是否仍有效；已接纳的回执在排队期间若实体退休/新请求覆盖也不得写入。关键车道容量独立于普通洪流，同时 P12 的有界关键配额避免普通命令长时饥饿。
- `Accepted` 只代表 Mailbox 接纳，`WorldRunReport.Executed` 也只代表同步回调正常返回；未证明数据库或外部系统事务提交成功。异常回调可能部分变更已发生，执行器设置 `Faulted`、停止消费剩余消息、封闭接纳，并保留 `Remaining`，等待未来 Host 的一致性恢复，不伪称回滚。
- `Complete` 停止新接纳、旧队列可排空。无离线请求持久队列、生产授权撤销实现、跨进程一致性或多世界路由。

## 验收测试及后续边界

- Unit：跨世界/错误 Epoch/Generation 写拒绝；Scope 结束后拒绝；Task.Run 继承上下文仍无法写；同世界非重入、不同世界并行；关键/普通车道独立容量和处理公平；请求序号/重放/伪造票据；Epoch 更新/实体退休/新代际；未授权入口；票据预算；故障与关闭保留队列语义。
- `WorldExecutionProbe`：真实 Background Worker、两个世界并行、旧 Epoch/Generation 和 Replay、默认安全拒绝、回调异常 fail closed；成功打印 `P18_WORLD_EXECUTION_INTEGRATION_PASS`。六个 CI Job 包括 build-test / world-execution / persistence-integration / staticdata-postgres / diagnostics-integration / networking-loopback。
- P18 技术实现只能证明 **Guard 正确拦截**和模拟拥有者使用执行器的流程；真正的 `Entity` 写 API 与 `WorldInstance` Registry 生命周期在 P24/P17，真实 Tick 与调度由 P19，生产认证体系在 P60。不能把测试用 `GuardedModel` 说成游戏实体系统。
