#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
content_only=false
case "${1:-}" in
  --content-only) content_only=true ;;
  --help|-h)
    echo 'Usage: bash scripts/check.sh [--content-only]'
    echo 'Default: content checks, Release build and C# tests (requires GAME_MANAGED).'
    echo '--content-only: generate/check local content and run Python tests; no game or .NET required.'
    exit 0 ;;
  '') ;;
  *) echo "Unknown option: $1" >&2; exit 2 ;;
esac
if (( $# > 1 )); then echo 'Expected at most one option.' >&2; exit 2; fi
python=python3
if [[ -x .venv/bin/python3 ]]; then python="$PWD/.venv/bin/python3"; fi
if [[ "$(uname -s)" == Darwin ]]; then
  managed="${GAME_MANAGED:-$HOME/Library/Application Support/Steam/steamapps/common/轮回小队 Demo/ReincarnationSquad_Data/Managed}"
else
  managed="${GAME_MANAGED:-$HOME/.local/share/Steam/steamapps/common/轮回小队 Demo/ReincarnationSquad_Data/Managed}"
fi
mkdir -p logs
"$python" scripts/prepare_original_assets.py | tee logs/original-art.log
"$python" scripts/build_content.py | tee logs/content.log
"$python" scripts/check_original_sources.py | tee logs/original-sources.log
"$python" scripts/check_contracts.py | tee logs/contracts.log
"$python" -m unittest discover -s tests -p 'test_*.py' 2>&1 | tee logs/presentation-tests.log
if "$content_only"; then
  echo 'PASS content-only checks. C# build/tests and in-game Mono tests were NOT run.'
  exit 0
fi
if [[ ! -f "$managed/Reincarnation.Domain.dll" ]]; then
  echo "找不到游戏程序集：$managed" >&2
  echo '设置 GAME_MANAGED 为游戏的 ReincarnationSquad_Data/Managed 目录后重试。' >&2
  echo '仅运行内容与 Python 检查：bash scripts/check.sh --content-only' >&2
  exit 2
fi
export GAME_MANAGED="$managed"
if [[ -n "${DOTNET_ROOT:-}" ]]; then
  dotnet="$DOTNET_ROOT/dotnet"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  dotnet="$DOTNET_ROOT/dotnet"
else
  dotnet="$(command -v dotnet || true)"
fi
if [[ ! -x "$dotnet" ]]; then
  echo '找不到 dotnet。安装 .NET 8 SDK，或将 DOTNET_ROOT 指向安装目录。' >&2
  exit 2
fi
"$dotnet" build src/YixianSect.csproj -c Release "-p:GameManaged=$managed" | tee logs/build.log
"$dotnet" run --project scripts/yixian-tests -c Release "-p:GameManaged=$managed" | tee logs/unit-tests.log
"$dotnet" run --project scripts/yixian-fates/fate-tests.csproj -c Release "-p:GameManaged=$managed" | tee logs/fate-tests.log
