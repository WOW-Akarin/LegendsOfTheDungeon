# 当前进度

更新时间：2026-11-16 +08:00
分支：main（按用户要求持续直接提交）
当前阶段 / 状态 / 目标：P13 — Networking.Transport / REVIEW / C
实现审查基线：42952d326177f69ed7715ec4fc27e451bebf5574
已验收：P00–P12；下一阶段：P14 — Metrics / Diagnostics（NOT_STARTED）

## 已实现

- `Platform.Networking` 新 Assembly，通用大端长度帧、Stream Port、TCP Loopback 客户端与服务端适配器、接收超时。
- 入站与出站限流、双预算有界发送队列（256 帧/4 MiB）、硬性最大帧 1 MiB、最大读取期限 30 秒、关闭结果未知的保守报告。
- P60.F 身份绑定/授权入口默认拒绝，未配置生产身份适配器时无世界写通道。
- `docs/modules/NETWORKING.md` 载明协议/失败路径，`docs/progress/P13.md` 包含阶段出口矩阵，`docs/reviews/P13_NETWORKING_REVIEW.md` 有四视角代码审查。

## 真实验证

- #90–#92：CA1512 Analyzer FAIL，已修复且未弱化规则。
- #93：ReadOnlyMemory 初始化 CS9174 FAIL，已修复。
- #94/#95/#96/#97/#98/#99：连续 SUCCESS。
- 最终实现 #99：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43835963119 — locked restore/Release build；Unit 139/139、Architecture 16/16；原 P02/P03/P04/P05 Gates、PostgreSQL 18 集成和新增 networking-loopback Job 10 项全部 PASS。
- 四视角代码审查必需未解决项 0；独立 REVIEW HEAD CI 尚未执行，不能提前 ACCEPTED。

## 下一步

1. REVIEW HEAD 的全部三 Job CI 必须 SUCCESS。
2. 正式 ACCEPTED 文档提交并验证 ACCEPTED HEAD 的全部三 Job。
3. 后续 P14 从 Metrics/Diagnostics 开始；真实生产认证/WorldExecutor、TLS、公网安全、P64 性能仍不属于本阶段。

## 风险与恢复

P13 仅本机 Loopback Transport，不提供未认证公网监听或世界写入口。关闭后 in-flight write 可能结果未知；任何可靠业务重试必须保留 CommandId、持久账本消歧。按 PROJECT_SPEC → ARCHITECTURE → PROGRESS → progress/P13.md → CI 日志恢复。
