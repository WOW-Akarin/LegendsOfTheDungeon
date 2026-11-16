#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

BUILD_LOG="$(mktemp)"
TEST_LOG="$(mktemp)"
ARCH_LOG="$(mktemp)"
trap 'rm -f "$BUILD_LOG" "$TEST_LOG" "$ARCH_LOG"' EXIT

if dotnet build LegendsOfTheDungeon.slnx --configuration Release --no-restore --nologo -p:LOTD_INJECT_BUILD_FAILURE=true >"$BUILD_LOG" 2>&1; then
  echo "ERROR: 故意编译失败没有让 dotnet build 返回失败。"
  cat "$BUILD_LOG"
  exit 1
fi
echo "PASS: 故意编译失败被构建 Gate 检出。"

if LOTD_INJECT_TEST_FAILURE=1 dotnet test tests/Unit/LegendsOfTheDungeon.UnitTests/LegendsOfTheDungeon.UnitTests.csproj --configuration Release --no-build --no-restore --nologo >"$TEST_LOG" 2>&1; then
  echo "ERROR: 故意测试失败没有让 dotnet test 返回失败。"
  cat "$TEST_LOG"
  exit 1
fi
echo "PASS: 故意测试失败被测试 Gate 检出。"

echo
if LOTD_INJECT_ARCHITECTURE_FAILURE=1 dotnet test tests/Architecture/LegendsOfTheDungeon.ArchitectureTests/LegendsOfTheDungeon.ArchitectureTests.csproj --configuration Release --no-build --no-restore --nologo >"$ARCH_LOG" 2>&1; then
  echo "ERROR: 故意架构测试失败没有让 dotnet test 返回失败。"
  cat "$ARCH_LOG"
  exit 1
fi
echo "PASS: 故意架构测试失败被 Architecture Gate 检出。"
