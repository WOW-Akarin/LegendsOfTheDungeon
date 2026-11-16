# P13 — 通用网络传输独立四视角 REVIEW

- 状态：ACCEPTED
- 实现基线：42952d326177f69ed7715ec4fc27e451bebf5574
- 实现验证：run #99 SUCCESS（Unit 139/139、Architecture 16/16、三 Job 全绿）
- 独立 REVIEW HEAD：f49e599ff3a70e4c872c2bdaaed839d289bc40f8，run #100 三 Job SUCCESS
- 更新时间：2026-11-16 +08:00

## 功能

`LengthPrefixedFrameCodec` 使用 4B 大端整数与 1–1048576B payload，header 和 payload 不受 TCP Read 边界限制；正常 EOF、截断、超大声明、整帧超时各有可识别结果。异步单读/单写，发送方多线程入队，但有容量和字节双硬上限。发送仅说明本地入队，不误宣称远端接收或领域提交。

## 架构和安全

`Platform.Networking` 是独立技术 Assembly，仅引用 `Security.Contracts`，没有 Runtime/Domain/World/Host 引用。`NetworkIngressGate` 复用 P60.F 默认拒绝 Port 与 ProductionIdentityGuard；Loopback 连接不能通过 LocalTest 身份获得世界写权限。代码没有任何由网络字节执行 World 写操作的 Handler。真实生产身份签发和 Host 组合根仍未实现，不能拿这里的安全默认值代替生产认证。

TCP 接入只绑定 127.0.0.1；客户端只允许 IPAddress.IsLoopback；pending backlog 与单次 Accept 限制明确。已接纳连接数的全局生命周期配额未实现，需要在生产 Host 及 P60/P64 中统一编排，不能公开该 Listener 为公网接口。

## 失败与恢复

通用帧长度先验证再分配，慢速头部也受整体期限限制；速率预算按帧数和 payload 字节数分别执行。满队列可明确返回 Busy，发送限流返回 RateLimited；不会无限排队。可取消流应在 Stop/Dispose 后释放 Socket；不配合取消的自定义流不妨碍 Stop 超时返回，当前帧保守标记 DeliveryUnknown。调用方不得在结果未知时直接生成新经济命令，而应以后续业务的幂等键查询/恢复。

## 真实证据和失败记录

- run #90–#92：CA1512 初始真实构建失败；按分析器 API 规范修复，未 suppression。
- run #93：CS9174 网络 Probe ReadOnlyMemory 集合表达式编译失败；改为数组载体。
- run #94–#99：全链路 SUCCESS，含所有负向质量 Gate、原 PostgreSQL 18 Integration 和新增真实 Loopback Job。
- 最终 run #99：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43835963119
- Net Unit 及现有 Unit 总计 139/139 PASS；Architecture 16/16 PASS。Loopback 10 项包含 TCP 半包粘包、超长声明、EOF/截断、慢帧、双向服务器/客户端、断线、入站限流、慢发送背压、未授权门禁，以及单独的非配合取消 Stream 故障注入。

## 结论与剩余风险

四视角必需实现未解决项：**0**。路线图 C 目标在限定本机 TCP 技术范围内达到 ACCEPTED；独立 REVIEW HEAD run #100 全绿，最终 ACCEPTED HEAD CI 尚待复核。尚无公网 TLS、真实生产身份、服务器全局连接配额、正式 WorldExecutor、P64 性能或 P66 长稳证据，不以本阶段结果替代这些验收。
