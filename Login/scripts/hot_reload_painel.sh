#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE="${1:-debug}"

cd "$ROOT_DIR"
export DOTNET_USE_POLLING_FILE_WATCHER="${DOTNET_USE_POLLING_FILE_WATCHER:-1}"
export DOTNET_WATCH_RESTART_ON_RUDE_EDIT="${DOTNET_WATCH_RESTART_ON_RUDE_EDIT:-1}"

if [[ "$MODE" == "no-hot-reload" ]]; then
  echo "Iniciando dotnet watch (Debug, sem Hot Reload)..."
  DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet watch --non-interactive --project Protons.UI/Protons.UI.csproj -c Debug --no-hot-reload run
  exit 0
fi

if [[ "$MODE" != "debug" ]]; then
  echo "Uso: $0 [debug|no-hot-reload]"
  exit 1
fi

echo "Iniciando dotnet watch (Debug, Hot Reload)..."
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet watch --non-interactive --project Protons.UI/Protons.UI.csproj -c Debug run
