# 当前上下文

日期：2026-11-16 +08:00；分支：main；不创建分支
范围：通用 C#/.NET10 游戏服务器，不兼容 WoW 3.3.5a 客户端；上游固定 SHA `17f9f9d5003708136c798ab4ef8c1b9762251bad` 仅参考机制。

架构：模块化单体/World Actor/Ports & Adapters；WorldExecutor 唯一真实同步租约，世界/实体的 World/Epoch/Generation 必须有效，后台 Worker 不直接持有可变实体。P24 Transform 是位置唯一写入真相。

## 阶段真相

P00–P19、P24(B) 已正式验收。P19 归档 HEAD `8aeea75a9bb0e3aa87ce2b0842a3ce31bbffae10`，CI #182 八 Job SUCCESS。当前进入 **P20 Spatial（IN_PROGRESS，目标 C）**，新增代码尚未获得 CI PASS，不能冒充完成。

## 当前实现

- Runtime.World 内置有界三维 Cell 桶和 EntityId→Cell 派生反向索引；P17 世界 _gate + P18 同步 Scope 统一实体 Add/Move/Remove；不复制 Transform。
- 初次 Grid：Cell Active/Unloaded，空 Cell 才卸载；Nearby/Range 返回按值且 EntityId 升序的活动实体快照；合法 Cell 与边界按 floor；已卸载/越界移动失败不修改位置。
- Rebuild 只从唯一 Transform 重建派生索引；Stop 释放所有桶；清理异常 Faulted。
- Unit/真实 P20 WorldSpatialProbe（随机 850 Move 对照、并发读、Fail Prepare、Stop），首版 Seeded Release 微基准（1600 实体、700 Move、400 查询）经 CI 独立 Job 取真实数字，不预设结果。
- CI 从 8 扩成 9 Job，P20 实现 CI、独立 REVIEW 和 ACCEPTED 各自必须完整验证。性能基线不代表 P64 10 World/10,000 Entity/50ms 达标。

## 下一安全动作

观察首次提交 HEAD 的九 Job CI，修复全部真实 FAIL 后才能独立 REVIEW 与 ACCEPTED。下一阶段 P21 Visibility/P27 Movement/P62 Full Replay/P64 Performance 当前均未完成。入口：[PROGRESS](PROGRESS.md)、[P20](progress/P20.md)、[Spatial 契约](modules/WORLD_SPATIAL.md)、[ROADMAP](DEVELOPMENT_ROADMAP.md)、[ARCHITECTURE](ARCHITECTURE.md)。
