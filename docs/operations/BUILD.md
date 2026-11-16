# 构建与依赖基线

> 关联阶段：P02–P04  
> 当前目标：提供干净环境可复现的 restore / build / test / architecture-test / documentation-gate 入口。

## SDK 与语言

- Target Framework：`net10.0`
- SDK：`10.0.404`，由 `global.json` 固定
- roll-forward：`latestPatch`
- prerelease：禁用
- C#：14.0
- Nullable：启用
- .NET Analyzers：启用
- TreatWarningsAsErrors：启用
- Deterministic：启用

SDK 10.0.404 已在 P02 GitHub Actions Linux x64 runner 通过 `dotnet --info` 实际验证。

## NuGet 与锁文件

`NuGet.config` 清空继承源，当前只允许 `https://api.nuget.org/v3/index.json`。

直接包版本集中在 `Directory.Packages.props`：

| 包 | 版本 |
|---|---:|
| Microsoft.NET.Test.Sdk | 18.11.0 |
| xunit | 2.9.3 |
| xunit.runner.visualstudio | 4.0.1 |
| Npgsql（P11 Integration Probe） | 10.0.3 |

每个项目提交 `packages.lock.json`。正常验证必须执行：

```bash
dotnet restore LegendsOfTheDungeon.slnx --locked-mode
```

依赖变更流程：

1. 明确修改 `Directory.Packages.props` 或项目引用。
2. 在非 locked 模式重新生成受影响的 `packages.lock.json`。
3. 审阅直接/传递依赖、版本及 content hash 的 diff。
4. 提交中央版本与 lock file。
5. CI 再以 `--locked-mode` 验证；锁文件与声明不一致时必须失败。

禁止用 `latest`、通配版本或删除 lock file 绕过 restore 失败。

## 统一验证

正向：

```bash
./eng/verify.sh
```

该脚本依次执行 SDK 信息、locked restore、Release build、Unit tests 和 Architecture tests。Release build 同时受 P04 公共 XML 注释编译 Gate 约束。

Gate 负向自测：

```bash
./eng/verify-negative-gates.sh
```

脚本要求故意编译失败、故意 Unit 测试失败和故意 Architecture 测试失败都产生非零退出码；如果任一错误被错误吞掉，负向自测自身失败。

## 文档质量 Gate

生产公共 API 缺失 XML 注释由中央 `Directory.Build.props` 默认启用 `GenerateDocumentationFile` 后交给编译器 `CS1591` 检测；全局 `TreatWarningsAsErrors=true` 使其阻断构建。

自测入口：

```bash
./eng/verify-documentation-gates.sh
```

脚本现场生成合规、缺失、Generated/ThirdParty 例外、非法例外、`NoWarn` / `WarningsNotAsErrors` 绕过，以及普通项目直接关闭 `GenerateDocumentationFile` 的样本。证据输出到 `artifacts/documentation-gate/`；中文语义与复杂逻辑说明按 [中文注释与文档质量 Review Checklist](../reviews/DOCUMENTATION_QUALITY_REVIEW.md) 人工审查。

## CI 证据

GitHub Actions 在干净 Ubuntu runner 上执行同一验证链。测试 TRX 作为 `test-results` artifact 保存 14 天；P04 文档 Gate 日志与摘要作为 `documentation-gate` artifact 保存 14 天。


## Kernel Strong ID compile Gate

P05 使用独立编译夹具证明 Strong ID 的 CLR 类型隔离，而不是只做运行时类型比较：

```bash
./eng/verify-kernel-contract-gates.sh
```

正例必须成功编译；负例同时尝试 `EntityId → ActorId`、`ItemDefinitionId → ItemInstanceId`、`WorldId → WorldEpoch`，要求至少出现 3 个 `CS1503`。日志与摘要输出到 `artifacts/kernel-contract-gate/` 并由 CI 保存 14 天。

## P11 PostgreSQL Integration

- CI 的 persistence-integration Job 使用 Ubuntu Linux x64、SDK 10.0.404、Npgsql 10.0.3、PostgreSQL 18。
- PostgreSQL 镜像已固定：postgres:18@sha256:2ea8630df176ad090ec92136dee850397c5af8781e4bcf2e4162593691c61bbc。
- 独立 Integration 项目：tests/Integration/LegendsOfTheDungeon.PersistenceProbe；Npgsql 锁文件已提交并用 --locked-mode 验证，程序也纳入 Solution。
- 构建命令：dotnet restore LegendsOfTheDungeon.slnx --locked-mode；真实数据库运行：dotnet run --project tests/Integration/LegendsOfTheDungeon.PersistenceProbe/LegendsOfTheDungeon.PersistenceProbe.csproj --configuration Release --no-restore。
- CI Service 注入 LOTD_TEST_POSTGRES 专用临时连接串；不能在公共日志中输出原值。连接池上限为 1 以验证事务释放。
- run #72（43824023757）完整成功：Unit 109/109、Architecture 16/16；真实数据库跑过回滚、Serializable、版本与幂等、Migration、BoundedBulkLoader、超时、故障回执未知、取消和连接释放。
- 独立 REVIEW run #73（43824564517）SUCCESS，正式 ACCEPTED HEAD run #74（43824857942）SUCCESS；两次均为 build-test + persistence-integration 全绿。
- 容器实际启动日志确认 PostgreSQL 18.7（Debian 18.7-1.pgdg13+2），并经 docker image inspect 验证 digest。
- Integration Job 会上传 persistence-integration/report.txt artifact（保留 14 天）。该报告不含连接串和参数秘密；只记录通过探针组。
- P65.F 仅初始确认丢失模拟，后续业务/断电/消息恢复需单独记录证据。

## P13 TCP Loopback Integration

- 独立技术程序集：`src/Platform/Networking/LegendsOfTheDungeon.Platform.Networking`，无第三方 NuGet 包；通过 Security.Contracts 的 P60.F 默认拒绝安全 Port 限定边界。
- `networking-loopback` CI Job 与 `persistence-integration` 并行依赖基础 `build-test`；Ubuntu Linux x64 / SDK 10.0.404 / Release、锁文件模式。
- 首先在干净工作区执行：

```bash
dotnet restore tests/Integration/LegendsOfTheDungeon.NetworkingProbe/LegendsOfTheDungeon.NetworkingProbe.csproj --locked-mode
dotnet run --project tests/Integration/LegendsOfTheDungeon.NetworkingProbe/LegendsOfTheDungeon.NetworkingProbe.csproj --configuration Release --no-restore
```

- Probe 仅监听 127.0.0.1 随机端口，真实 Socket 验证半包/粘包、慢报头、截断/EOF、超大长度、断线、双向客户端/服务端适配器、入站限流、慢接收方背压、未授权拦截；额外使用故意忽略取消的流替身验证停止结果未知。通过时输出 `P13_NETWORKING_LOOPBACK_PASS`，CI 上传 `artifacts/networking-loopback/report.txt`。
- 最终实现 run #99（43835963119）三 Job SUCCESS，Unit 139/139、Architecture 16/16，网络探针 10/10、PG 集成 PASS。独立 REVIEW HEAD run #100 三 Job SUCCESS，正式 ACCEPTED HEAD run #101 三 Job SUCCESS。完整证据见 [P13 阶段记录](../progress/P13.md)。
