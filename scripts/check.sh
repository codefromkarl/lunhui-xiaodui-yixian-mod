#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet="$DOTNET_ROOT/dotnet"
managed="${GAME_MANAGED:-$HOME/.local/share/Steam/steamapps/common/轮回小队 Demo/ReincarnationSquad_Data/Managed}"
mkdir -p logs
python3 scripts/prepare_original_assets.py | tee logs/original-art.log
python3 scripts/build_content.py | tee logs/content.log
python3 scripts/check_original_sources.py | tee logs/original-sources.log
python3 scripts/check_contracts.py | tee logs/contracts.log
python3 -m unittest discover -s tests -p 'test_*.py' 2>&1 | tee logs/presentation-tests.log
"$dotnet" build src/YixianSect.csproj -c Release "-p:GameManaged=$managed" | tee logs/build.log
"$dotnet" run --project scripts/yixian-tests -c Release "-p:GameManaged=$managed" | tee logs/unit-tests.log
"$dotnet" run --project scripts/yixian-fates/fate-tests.csproj -c Release "-p:GameManaged=$managed" | tee logs/fate-tests.log
