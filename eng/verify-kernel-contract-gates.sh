#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

EVIDENCE_ROOT="$ROOT/artifacts/kernel-contract-gate"
FIXTURE_ROOT="$EVIDENCE_ROOT/fixtures"
LOG_ROOT="$EVIDENCE_ROOT/logs"
SUMMARY="$EVIDENCE_ROOT/summary.txt"

rm -rf "$EVIDENCE_ROOT"
mkdir -p "$FIXTURE_ROOT/positive" "$FIXTURE_ROOT/misuse" "$LOG_ROOT"
trap 'rm -rf "$FIXTURE_ROOT"' EXIT

write_project() {
  local path="$1"

  cat >"$path" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsTestProject>true</IsTestProject>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$ROOT/src/Kernel/LegendsOfTheDungeon.Kernel/LegendsOfTheDungeon.Kernel.csproj" />
  </ItemGroup>
</Project>
EOF
}

write_project "$FIXTURE_ROOT/positive/Fixture.csproj"
write_project "$FIXTURE_ROOT/misuse/Fixture.csproj"

cat >"$FIXTURE_ROOT/positive/Program.cs" <<'EOF'
using LegendsOfTheDungeon.Kernel;

internal static class PositiveStrongIdContract
{
    internal static ulong Read()
    {
        var entity = new EntityId(1);
        var actor = new ActorId(2);
        var world = new WorldId(3);
        var ability = new AbilityId(4);
        var definition = new ItemDefinitionId(5);
        var instance = new ItemInstanceId(6);
        var task = new TaskId(7);
        var generation = new EntityGeneration(8);
        var epoch = new WorldEpoch(9);

        return entity.Value
            + actor.Value
            + world.Value
            + ability.Value
            + definition.Value
            + instance.Value
            + task.Value
            + generation.Value
            + epoch.Value;
    }
}
EOF

cat >"$FIXTURE_ROOT/misuse/Program.cs" <<'EOF'
using LegendsOfTheDungeon.Kernel;

internal static class NegativeStrongIdContract
{
    private static void AcceptActor(ActorId value)
    {
    }

    private static void AcceptItemInstance(ItemInstanceId value)
    {
    }

    private static void AcceptWorldEpoch(WorldEpoch value)
    {
    }

    internal static void VerifyCompileTimeSeparation()
    {
        var entity = new EntityId(1);
        var definition = new ItemDefinitionId(2);
        var world = new WorldId(3);

        AcceptActor(entity);
        AcceptItemInstance(definition);
        AcceptWorldEpoch(world);
    }
}
EOF

{
  echo "P05 Kernel Strong ID compile contract gate"
  echo "SDK: $(dotnet --version)"
  echo
} >"$SUMMARY"

POSITIVE_LOG="$LOG_ROOT/positive.log"
if ! dotnet build "$FIXTURE_ROOT/positive/Fixture.csproj" --configuration Release --nologo >"$POSITIVE_LOG" 2>&1; then
  echo "ERROR: 合法 Strong ID 使用应成功编译。" | tee -a "$SUMMARY"
  cat "$POSITIVE_LOG"
  exit 1
fi
echo "PASS: 合法 Strong ID 使用可编译。" | tee -a "$SUMMARY"

MISUSE_LOG="$LOG_ROOT/misuse.log"
if dotnet build "$FIXTURE_ROOT/misuse/Fixture.csproj" --configuration Release --nologo >"$MISUSE_LOG" 2>&1; then
  echo "ERROR: 跨类型 Strong ID 误用应在编译期失败。" | tee -a "$SUMMARY"
  cat "$MISUSE_LOG"
  exit 1
fi

CS1503_COUNT="$(grep -c "error CS1503" "$MISUSE_LOG" || true)"
if [ "$CS1503_COUNT" -lt 3 ]; then
  echo "ERROR: 预期至少 3 个 CS1503，实际为 $CS1503_COUNT。" | tee -a "$SUMMARY"
  cat "$MISUSE_LOG"
  exit 1
fi

echo "PASS: EntityId→ActorId、ItemDefinitionId→ItemInstanceId、WorldId→WorldEpoch 均被编译器以 CS1503 拒绝。" | tee -a "$SUMMARY"
echo "PASS: P05 Kernel compile contract gate 全部通过。" | tee -a "$SUMMARY"
