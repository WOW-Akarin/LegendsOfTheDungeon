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

独立 Probe 在 tests/Integration/LegendsOfTheDungeon.PersistenceProbe，使用 Npgsql 10.0.3、postgres:18 service。实际依赖锁文件已由 run #67 非 locked bootstrap 生成并检入；后续必须执行 dotnet restore <Probe.csproj> --locked-mode。run #67 已 PASS 真实回滚、Serializable、条件版本更新、重复幂等、提交前取消与 statement_timeout；Migration、BulkLoader、提交结果未知故障及镜像 digest 仍待验收。
