#!/usr/bin/env bash
set -euo pipefail

# Mede tempo de startup sem abrir UI (baseline e simulado lento)
DOTNET_CLI_TELEMETRY_OPTOUT=1 PROTONS_STARTUP_PROBE=1 dotnet run --project Protons.UI/Protons.UI.csproj -c Release --no-build
DOTNET_CLI_TELEMETRY_OPTOUT=1 PROTONS_STARTUP_PROBE=1 PROTONS_SIMULATE_SLOW_STARTUP_MS=200 dotnet run --project Protons.UI/Protons.UI.csproj -c Release --no-build
