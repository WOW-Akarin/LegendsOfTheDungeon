# P13 — 通用 Networking.Transport 契约

## 协议与容量

- **契约 ID/版本：** `platform.networking.frame.v1`。不包含 WoW Opcode、WorldPacket、3.3.5a、压缩、加密或业务消息 ID。
- **用途与真实调用方：** `TcpLoopbackConnector` 将本机 TCP Socket 包装为 `IFrameTransport`；`FramedStreamTransport` 还可连接任意受控 `Stream`；P13 真实 TCP Probe 同时模拟两端，使用本机限定的 `TcpLoopbackEndpoint` 进行客户端与服务端适配器双向测试。生产公网 Host 尚不存在。
- **线缆格式：** 4 字节无符号大端序 payload 长度 `N`，后接恰好 `N` 字节不透明数据；`1 <= N <= MaximumPayloadBytes`。不包括长度字段的 payload 预算，默认由可信调用方显式配置；配置硬上限 1 MiB。零长度错误，非零但超长的声明在分配 payload 前拒绝。
- **读取：** 单一消费者串行解码。TCP 任意分段/粘包均可按总长度组合；帧边界正常 EOF 返回 null，报头/载荷中途 EOF 返回 Truncated；整个 header+payload 共用一个计时期限，避免每次收到一字节都刷新超时。读取期限上限 30 秒。取消使连接关闭，不能在半帧之后按错误流偏移继续读取。
- **缓冲：** 每个连接使用单个有界 Channel 供多个发送方并发 Offer 与唯一写协程；`maximumQueuedFrames` 最多 256、`maximumQueuedBytes` 最多 4 MiB；`OfferSend` 在接纳前复制快照，不在 Busy/RateLimited 时分配缓存；封包写入串行，不能交错不同帧的报头与载荷。
- **背压：** `Accepted` 仅说明已排队；满字节/帧数返回 Busy；已关闭返回 Closed；每连接入站/出站独立 1000ms 固定窗口限制帧数及 payload 字节，超限出站返回 RateLimited，入站触发 FrameProtocolException(RateLimited) 并关闭连接。固定窗口具有边界突发性质，不等同令牌桶的长期平滑 QPS。
- **关闭：** `StopAsync(timeout)` 先停止新 Offer，再等待发送者；超过期限取消并关闭 Stream，报告 `Drained`、排队帧/字节、完整写入流的计数、在途写入可能结果未知。操作系统接受本地 Write 并不代表远端应用已收到，更非世界命令已提交。`DisposeAsync` 必须及时释放 Socket/缓存，不等待不配合取消的自定义 Stream。
- **可插拔替换：** 新传输实现须遵守相同 `IFrameTransport`（Offer/Receive/Stop/Dispose）语义；本阶段仅包含 TCP Loopback 工厂与只绑定 127.0.0.1 的 `TcpLoopbackEndpoint`（Socket backlog 上限 128、同一时刻只有一个 Accept），不实现生产公网监听、TLS、并发活动连接总量管理或互联网安全认证。

## 安全边界

- **依赖 P60.F：** `NetworkIngressGate` 使用 `IIdentityBindingPort` 与 `ICommandAuthorizationPort`，必须先满足 `ProductionIdentityGuard`，而 P10 只提供 DenyByDefaultBinder 与 LocalTest 身份。没有真实生产认证签发时任何网络请求都无法通过。
- 网络模块没有命令派发回调，不直接调用 WorldExecutor、Player/Actor 状态写 API，不使用未经验证的网络 ActorId。
- 真正的认证证据传递、身份主体绑定、指令级授权、World/Epoch/Generation 复核属于后续 P18/P27/P60/P61。网络帧成功解析并不是命令被接纳/提交。
- 本机 Loopback 不等同真实公网威胁模型；不提供未认证的生产公网监听。敏感输入不会进入异常消息或任意文本日志。

## 验证范围

- Unit：大端格式/长度校验/零长度/超限/截断、限流窗口、队列帧数与字节容量、无生产身份的默认拒绝、非 Loopback 地址拒绝。
- Linux 实际 TCP Loopback：分片、粘包、超大帧、慢 Header、截断、双向收发与关闭、断线释放、入站限速、慢消费侧发送背压、未授权数据不得跨世界写边界。
- 不包括公网 TLS、身份签发者、业务路由、性能 P64、长稳 P66；不能把本阶段成功等同全系统上线。

阶段状态和 CI/失败证据见 [P13](../progress/P13.md)。
