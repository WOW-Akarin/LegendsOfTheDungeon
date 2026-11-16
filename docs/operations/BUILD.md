# 构建与依赖基线

> 关联阶段：P02  
> 当前目标：提供干净环境可复现的 restore / build / test / architecture-test 入口。

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

该脚本依次执行 SDK 信息、locked restore、Release build、Unit tests 和 Architecture tests。

Gate 负向自测：

```bash
./eng/verify-negative-gates.sh
```

脚本要求故意编译失败与故意测试失败都产生非零退出码；如果任一错误被错误吞掉，负向自测自身失败。

## CI 证据

GitHub Actions 在干净 Ubuntu runner 上执行同一验证链。测试 TRX 作为 `test-results` artifact 保存 14 天。P03 将在现有 Architecture 测试入口上增加真正的依赖白名单、Internal/Infrastructure 泄漏和循环引用规则。
