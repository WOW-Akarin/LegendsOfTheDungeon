#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

dotnet --info
dotnet restore LegendsOfTheDungeon.slnx --nologo --locked-mode
dotnet build LegendsOfTheDungeon.slnx --configuration Release --no-restore --nologo

rm -rf artifacts/test-results
mkdir -p artifacts/test-results

dotnet test tests/Unit/LegendsOfTheDungeon.UnitTests/LegendsOfTheDungeon.UnitTests.csproj \
  --configuration Release --no-build --no-restore --nologo \
  --logger "trx;LogFileName=unit.trx" \
  --results-directory artifacts/test-results

dotnet test tests/Architecture/LegendsOfTheDungeon.ArchitectureTests/LegendsOfTheDungeon.ArchitectureTests.csproj \
  --configuration Release --no-build --no-restore --nologo \
  --logger "trx;LogFileName=architecture.trx" \
  --results-directory artifacts/test-results
