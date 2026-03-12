#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"
VERSION="1.0.0"
if [ -f "$VERSION_FILE" ]; then
  VERSION_LINE="$(grep -E '^VERSION=' "$VERSION_FILE" | head -n1 || true)"
  if [ -n "$VERSION_LINE" ]; then
    VERSION="${VERSION_LINE#VERSION=}"
  fi
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "❌ ERRO: dotnet não encontrado no PATH" >&2
  exit 1
fi

TOOLS_DIR="$PROJECT_ROOT/.tools"
mkdir -p "$TOOLS_DIR"

if [ ! -x "$TOOLS_DIR/dotnet-CycloneDX" ]; then
  dotnet tool install --tool-path "$TOOLS_DIR" CycloneDX
fi

OUTPUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/sbom"
mkdir -p "$OUTPUT_DIR"

"$TOOLS_DIR/dotnet-CycloneDX" "$PROJECT_ROOT/Login/Protons.sln" \
  -o "$OUTPUT_DIR" \
  -f "protons-${VERSION}-sbom" \
  -j --exclude-dev -rs

RAW_FILE="$OUTPUT_DIR/protons-${VERSION}-sbom"
JSON_FILE="$OUTPUT_DIR/protons-${VERSION}-sbom.json"
if [ -f "$RAW_FILE" ] && [ ! -f "$JSON_FILE" ]; then
  mv "$RAW_FILE" "$JSON_FILE"
fi

if command -v jq >/dev/null 2>&1; then
  tmp_file="$(mktemp)"
  jq --arg ver "$VERSION" '.metadata.component.version = $ver' "$JSON_FILE" > "$tmp_file"
  mv "$tmp_file" "$JSON_FILE"
else
  echo "⚠️  jq não encontrado - metadata.component.version não atualizada"
fi

echo "✅ SBOM gerado: $JSON_FILE"
