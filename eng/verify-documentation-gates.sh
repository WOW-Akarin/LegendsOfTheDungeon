#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

EVIDENCE_ROOT="$ROOT/artifacts/documentation-gate"
FIXTURE_ROOT="$EVIDENCE_ROOT/fixtures"
LOG_ROOT="$EVIDENCE_ROOT/logs"
SUMMARY="$EVIDENCE_ROOT/summary.txt"

rm -rf "$EVIDENCE_ROOT"
mkdir -p "$FIXTURE_ROOT" "$LOG_ROOT"
trap 'rm -rf "$FIXTURE_ROOT"' EXIT

build_fixture() {
  local project="$1"
  local log="$2"

  dotnet build "$project"     --configuration Release     --nologo     -p:RestorePackagesWithLockFile=false     >"$log" 2>&1
}

expect_success() {
  local label="$1"
  local project="$2"
  local log="$3"

  if ! build_fixture "$project" "$log"; then
    echo "ERROR: $label 应成功，但构建失败。"
    cat "$log"
    exit 1
  fi

  echo "PASS: $label"
}

expect_failure_with() {
  local label="$1"
  local project="$2"
  local expected="$3"
  local log="$4"

  if build_fixture "$project" "$log"; then
    echo "ERROR: $label 应失败，但构建成功。"
    cat "$log"
    exit 1
  fi

  if ! grep -Fq "$expected" "$log"; then
    echo "ERROR: $label 虽失败，但没有出现预期诊断 $expected。"
    cat "$log"
    exit 1
  fi

  echo "PASS: $label（$expected）"
}

mkdir -p   "$FIXTURE_ROOT/positive"   "$FIXTURE_ROOT/missing"   "$FIXTURE_ROOT/generated-exempt"   "$FIXTURE_ROOT/thirdparty-exempt"   "$FIXTURE_ROOT/missing-reason"   "$FIXTURE_ROOT/invalid-kind"   "$FIXTURE_ROOT/no-warn-bypass"   "$FIXTURE_ROOT/not-as-error-bypass" \
  "$FIXTURE_ROOT/disable-without-exemption"

cat >"$FIXTURE_ROOT/positive/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk" />
EOF

cat >"$FIXTURE_ROOT/positive/Api.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

/// <summary>
/// 提供文档 Gate 的合规公共 API 样本；仅用于验证中文 XML 注释不会被编译 Gate 拒绝。
/// </summary>
public static class DocumentedApi
{
    /// <summary>
    /// 验证输入非负并原样返回。调用方必须传入大于等于零的值；该方法不跨线程、不执行 I/O，也不修改共享状态。
    /// </summary>
    /// <param name="value">待验证的非负整数。</param>
    /// <returns>验证通过后的原值。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="value"/> 小于零时抛出。</exception>
    public static int ValidateNonNegative(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value;
    }
}
EOF

cat >"$FIXTURE_ROOT/missing/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk" />
EOF

cat >"$FIXTURE_ROOT/missing/Api.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class MissingDocumentationApi
{
    public static void Execute()
    {
    }
}
EOF

cat >"$FIXTURE_ROOT/generated-exempt/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LegendsOfTheDungeonDocumentationGateExempt>true</LegendsOfTheDungeonDocumentationGateExempt>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <LegendsOfTheDungeonDocumentationGateExemptionKind>Generated</LegendsOfTheDungeonDocumentationGateExemptionKind>
    <LegendsOfTheDungeonDocumentationGateExemptionReason>P04 自测：模拟工具生成的公共代码。</LegendsOfTheDungeonDocumentationGateExemptionReason>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/generated-exempt/Generated.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class GeneratedApi
{
    public static void Execute()
    {
    }
}
EOF

cat >"$FIXTURE_ROOT/thirdparty-exempt/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LegendsOfTheDungeonDocumentationGateExempt>true</LegendsOfTheDungeonDocumentationGateExempt>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <LegendsOfTheDungeonDocumentationGateExemptionKind>ThirdParty</LegendsOfTheDungeonDocumentationGateExemptionKind>
    <LegendsOfTheDungeonDocumentationGateExemptionReason>P04 自测：模拟按原样纳入的第三方公共代码。</LegendsOfTheDungeonDocumentationGateExemptionReason>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/thirdparty-exempt/ThirdParty.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class ThirdPartyApi
{
    public static void Execute()
    {
    }
}
EOF

cat >"$FIXTURE_ROOT/missing-reason/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LegendsOfTheDungeonDocumentationGateExempt>true</LegendsOfTheDungeonDocumentationGateExempt>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <LegendsOfTheDungeonDocumentationGateExemptionKind>Generated</LegendsOfTheDungeonDocumentationGateExemptionKind>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/missing-reason/Generated.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class MissingReasonApi
{
}
EOF

cat >"$FIXTURE_ROOT/invalid-kind/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LegendsOfTheDungeonDocumentationGateExempt>true</LegendsOfTheDungeonDocumentationGateExempt>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <LegendsOfTheDungeonDocumentationGateExemptionKind>Internal</LegendsOfTheDungeonDocumentationGateExemptionKind>
    <LegendsOfTheDungeonDocumentationGateExemptionReason>P04 自测：该例外类型必须被拒绝。</LegendsOfTheDungeonDocumentationGateExemptionReason>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/invalid-kind/Generated.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class InvalidKindApi
{
}
EOF

cat >"$FIXTURE_ROOT/no-warn-bypass/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/no-warn-bypass/Api.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class NoWarnBypassApi
{
}
EOF

cat >"$FIXTURE_ROOT/not-as-error-bypass/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <WarningsNotAsErrors>$(WarningsNotAsErrors);CS1591</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/not-as-error-bypass/Api.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class WarningDowngradeBypassApi
{
}
EOF

cat >"$FIXTURE_ROOT/disable-without-exemption/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
EOF

cat >"$FIXTURE_ROOT/disable-without-exemption/Api.cs" <<'EOF'
namespace LegendsOfTheDungeon.DocumentationFixtures;

public static class DisabledDocumentationGateApi
{
}
EOF

{
  echo "P04 中文注释与文档质量 Gate 自测"
  echo "SDK: $(dotnet --version)"
  echo
} >"$SUMMARY"

expect_success   "完整中文 XML 注释的 Public API 可构建"   "$FIXTURE_ROOT/positive/Fixture.csproj"   "$LOG_ROOT/positive.log" | tee -a "$SUMMARY"

expect_failure_with   "缺失 Public XML 注释被编译器拒绝"   "$FIXTURE_ROOT/missing/Fixture.csproj"   "CS1591"   "$LOG_ROOT/missing.log" | tee -a "$SUMMARY"

expect_success   "Generated 显式例外（含理由）可构建"   "$FIXTURE_ROOT/generated-exempt/Fixture.csproj"   "$LOG_ROOT/generated-exempt.log" | tee -a "$SUMMARY"

expect_success   "ThirdParty 显式例外（含理由）可构建"   "$FIXTURE_ROOT/thirdparty-exempt/Fixture.csproj"   "$LOG_ROOT/thirdparty-exempt.log" | tee -a "$SUMMARY"

expect_failure_with   "例外缺少理由被拒绝"   "$FIXTURE_ROOT/missing-reason/Fixture.csproj"   "LOTD0003"   "$LOG_ROOT/missing-reason.log" | tee -a "$SUMMARY"

expect_failure_with   "非法例外类型被拒绝"   "$FIXTURE_ROOT/invalid-kind/Fixture.csproj"   "LOTD0002"   "$LOG_ROOT/invalid-kind.log" | tee -a "$SUMMARY"

expect_failure_with   "NoWarn 绕过 CS1591 被拒绝"   "$FIXTURE_ROOT/no-warn-bypass/Fixture.csproj"   "LOTD0004"   "$LOG_ROOT/no-warn-bypass.log" | tee -a "$SUMMARY"

expect_failure_with   "WarningsNotAsErrors 降级 CS1591 被拒绝"   "$FIXTURE_ROOT/not-as-error-bypass/Fixture.csproj"   "LOTD0004"   "$LOG_ROOT/not-as-error-bypass.log" | tee -a "$SUMMARY"

echo >>"$SUMMARY"
echo "PASS: P04 文档质量自动 Gate 自测全部通过。" | tee -a "$SUMMARY"
