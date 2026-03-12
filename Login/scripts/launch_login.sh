#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT="$ROOT_DIR/Protons.UI/bin/Release/net8.0/Protons.UI"

# Evita abrir múltiplas instâncias do login.
if pgrep -f "$OUTPUT" >/dev/null; then
  exit 0
fi

need_build=0
if [ ! -x "$OUTPUT" ]; then
  need_build=1
else
  if find "$ROOT_DIR/Protons.UI" "$ROOT_DIR/Protons.Core" "$ROOT_DIR/Protons.Infrastructure" \
      \( -path "*/bin/*" -o -path "*/obj/*" \) -prune -o -type f -newer "$OUTPUT" -print -quit | grep -q .; then
    need_build=1
  fi
fi

# Compila somente se houver mudanças e executa o binário direto (mais rápido).
if [ "$need_build" -eq 1 ]; then
  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_MULTILEVEL_LOOKUP=0 \
    dotnet build Protons.UI/Protons.UI.csproj -c Release --no-restore -clp:ErrorsOnly
fi

if [ -x "$OUTPUT" ]; then
  "$OUTPUT"
else
  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_MULTILEVEL_LOOKUP=0 \
    dotnet run --project Protons.UI/Protons.UI.csproj -c Release --no-build
fi
