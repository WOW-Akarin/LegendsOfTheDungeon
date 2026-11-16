# 当前进度

更新时间：2026-11-16 +08:00
分支：main（按用户要求持续直接提交）
当前阶段 / 状态 / 目标：P13 — Networking.Transport / ACCEPTED / C
下一阶段：P14 — Metrics / Diagnostics（NOT_STARTED）
阶段起始 HEAD：987b6e76f3880023b9282b6c2e21f3f867bae539
实现审查 HEAD：42952d326177f69ed7715ec4fc27e451bebf5574
独立 REVIEW HEAD：f49e599ff3a70e4c872c2bdaaed839d289bc40f8

## 已验收范围

- 独立 `Platform.Networking` Assembly：4B uint32 大端长度帧、Stream Port、本机 TCP 客户端和服务端适配器。
- 硬性单帧 1 MiB、每连接等待队列 256 帧/4 MiB、整帧读取期限 30 秒；串行解码、单写入者、并发生产者背压、入站/出站限流。
- 真实 TCP Loopback 及单独故障流 Probe：半包/粘包、超长、慢头部、截断/正常 EOF、双向收发、慢消费者、断线释放、限流、未授权阻断、非配合取消写的结果未知。
- P60.F 身份绑定/授权默认拒绝；无生产认证签发者和任何 World 写入 API，不开放公网 Host。
- `docs/modules/NETWORKING.md`、[P13 完整证据](progress/P13.md)、[P13 四视角 REVIEW](reviews/P13_NETWORKING_REVIEW.md)。

## 真实验收证据

- 早期 CI #90/#91/#92：CA1512 FAIL；#93：CS9174 FAIL，均按真实诊断修复，不关闭分析器。
- 实现 CI #94–#99 连续 SUCCESS；最终实现 #99：Unit 139/139、Architecture 16/16、10 项 Networking Loopback、原 PostgreSQL 18 Integration，全部质量 Gate PASS。
- 独立 REVIEW HEAD #100：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43836500422 — 三 Job 全部 SUCCESS。
- 正式 ACCEPTED HEAD 8fc43bf 的 CI #101：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43836883165 — 三 Job 完整 SUCCESS。
- 四视角必需未解决项 0，独立 REVIEW #100 与正式 ACCEPTED #101 均全绿；P13 已完整收口。

## 当前停点

1. 从 P14 — Metrics / Diagnostics 开启下一阶段（当前 NOT_STARTED）。
2. 后续 P18/P27/P60/P61 接入真实认证、主体授权和 WorldEpoch/Generation 写入校验；P13 不承诺公网 TLS、全局连接配额、P64 性能或长稳。
3. 世界命令可靠性须保留幂等键，不能将网络本地 Write 当成 DB 提交。

## 恢复

按 PROJECT_SPEC → ARCHITECTURE → CONTEXT → PROGRESS → progress/P13.md 阅读；以实际 main HEAD、三 Job CI 与阶段矩阵为准。失败退回 IN_PROGRESS。
