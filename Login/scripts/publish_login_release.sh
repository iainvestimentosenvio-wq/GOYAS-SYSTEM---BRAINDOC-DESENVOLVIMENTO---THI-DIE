#!/usr/bin/env bash
set -euo pipefail

# Publica a UI em Release com ReadyToRun para startup mais rápido
OUTPUT_DIR="publish/login-ui"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet publish Protons.UI/Protons.UI.csproj -c Release -o "$OUTPUT_DIR" -p:PublishReadyToRun=true

echo "Publicado em: $OUTPUT_DIR"
