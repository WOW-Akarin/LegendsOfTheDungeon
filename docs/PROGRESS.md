# 当前进度

更新时间：2026-11-16 +08:00
分支：main（直接更新，不创建分支）
当前阶段：P20 — Spatial / Cell / Grid（ACCEPTED，目标 C）
已正式 ACCEPTED：P00–P19、P24(B)；P20(C) 已正式 ACCEPTED

## 最近真实证据

- P19 WorldTick/Scheduler：实现 #179、独立 REVIEW #180、正式 ACCEPTED #181、归档 #182 均 8/8 SUCCESS，242/242 Unit、16/16 Architecture。
- P19 归档 HEAD `8aeea75a9bb0e3aa87ce2b0842a3ce31bbffae10`；CI #182：https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43898982784
- P17 WorldInstance、P18 WorldExecutor、P24 Transform 均已验收，作为 P20 单一写租约与唯一位置真相。

## P20 已正式 ACCEPTED

- Spatial/Cell/Grid 内置到 Runtime.World，世界唯一 Transform + 派生索引，统一同步原子重定位与负坐标 Cell。
- Nearby / Range 查询、Grid 卸载/重激活、FromCanonical 重建、代际退休与停机索引清理。
- 首版随机索引与暴力扫描对照、并发读/写测试、1600 Entity Seeded Release 基准。
- CI 新增独立 world-spatial Job（总九个），实现 HEAD `875f0cd64c2dbcf34985c0c2de3a1bbc90c7921b` 的 CI #185（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43903710433）9/9 SUCCESS，257 Unit / 16 Architecture。
- 入口：[P20 阶段记录](progress/P20.md) / [Spatial 契约](modules/WORLD_SPATIAL.md)。

## P20 首版基准与真实验证

- 初次实现 CI #183 因 `Math.Hypot` 不存在而 FAIL；正确替换后 #184 九 Job 首次全绿。对 Removing/Faulted 查询进一步加固后，稳定实现 HEAD `875f0cd64c2dbcf34985c0c2de3a1bbc90c7921b` CI #185 九 Job 全绿。
- #185 P20 WorldSpatialProbe：850 次 seeded 随机跨 Cell 移动与暴力扫描一致、失败重定位、并发读、重建/移除/Stop、首版 Release Benchmark 全部 PASS。
- #185 环境 Ubuntu 26.04.1 LTS / 4 CPU / .NET 10 Release / Seed=2071；1600 实体、700 Move、400 Nearby；Move p50/p95/p99 0.0040/0.0093/0.0198 ms；Query p50/p95/p99 0.1866/0.3860/0.4764 ms；分配 7,325,944 bytes。性能非 P64 10 World 压测。
- 独立审查：[P20 Spatial Review](reviews/P20_WORLD_SPATIAL_REVIEW.md)，REVIEW HEAD `f9a90264fa5fbab02d5ce596414f7c2a133e5bec` CI #186（https://github.com/WOW-Akarin/LegendsOfTheDungeon/actions/runs/43904373576）9/9 SUCCESS。P20 C 正式 ACCEPTED；本次 ACCEPTED HEAD CI 仍待核验。

## 下一安全动作

核验本次 P20 正式 ACCEPTED HEAD 的九 Job CI，之后归档三阶段真实证据；不预先推进 P21 Visibility 或声称 P64 大规模性能已验收。
