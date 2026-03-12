#!/usr/bin/env bash
set -euo pipefail

# Executa o app em Release sem rebuild (mais rápido para testes locais)
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project Protons.UI/Protons.UI.csproj -c Release --no-build
