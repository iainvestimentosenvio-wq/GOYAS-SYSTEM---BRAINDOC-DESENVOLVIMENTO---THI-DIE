#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"
VERSION="1.0.0"
PRODUCT_NAME="Protons"
PUBLISHER="Protons Team"

if [ -f "$VERSION_FILE" ]; then
  VERSION_LINE="$(grep -E '^VERSION=' "$VERSION_FILE" | head -n1 || true)"
  PRODUCT_LINE="$(grep -E '^PRODUCT_NAME=' "$VERSION_FILE" | head -n1 || true)"
  PUBLISHER_LINE="$(grep -E '^PUBLISHER=' "$VERSION_FILE" | head -n1 || true)"
  [ -n "$VERSION_LINE" ] && VERSION="${VERSION_LINE#VERSION=}"
  [ -n "$PRODUCT_LINE" ] && PRODUCT_NAME="${PRODUCT_LINE#PRODUCT_NAME=}"
  [ -n "$PUBLISHER_LINE" ] && PUBLISHER="${PUBLISHER_LINE#PUBLISHER=}"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "❌ ERRO: dotnet não encontrado no PATH" >&2
  exit 1
fi

TOOLS_DIR="$PROJECT_ROOT/.tools"
mkdir -p "$TOOLS_DIR"

if [ ! -x "$TOOLS_DIR/sbom-tool" ]; then
  dotnet tool install --tool-path "$TOOLS_DIR" Microsoft.Sbom.DotNetTool
fi

OUTPUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/sbom"
mkdir -p "$OUTPUT_DIR"

PRODUCT_SLUG="$(echo "$PRODUCT_NAME" | tr '[:upper:]' '[:lower:]' | tr ' ' '-' | tr -cd 'a-z0-9-')"
NAMESPACE_URI="${SBOM_NAMESPACE_URI:-urn:${PRODUCT_SLUG:-protons}:sbom}"

PUBLISH_DIR="${1:-}"
if [ -z "$PUBLISH_DIR" ]; then
  LINUX_PUBLISH="$PROJECT_ROOT/Login/Protons.UI/bin/Release/net8.0/linux-x64/publish"
  WIN_PUBLISH="$PROJECT_ROOT/Login/Protons.UI/bin/Release/net8.0/win-x64/publish"
  if [ -d "$LINUX_PUBLISH" ]; then
    PUBLISH_DIR="$LINUX_PUBLISH"
  elif [ -d "$WIN_PUBLISH" ]; then
    PUBLISH_DIR="$WIN_PUBLISH"
  else
    echo "❌ ERRO: publish nao encontrado." >&2
    echo "   Gere com: dotnet publish Login/Protons.UI/Protons.UI.csproj -c Release -r <RID>" >&2
    exit 1
  fi
fi

if [ ! -d "$PUBLISH_DIR" ]; then
  echo "❌ ERRO: publish dir invalido: $PUBLISH_DIR" >&2
  exit 1
fi

echo "Gerando SPDX..."
echo "  Version: $VERSION"
echo "  Product: $PRODUCT_NAME"
echo "  Publisher: $PUBLISHER"
echo "  Namespace: $NAMESPACE_URI"
echo "  PublishDir: $PUBLISH_DIR"
echo "  Output: $OUTPUT_DIR"

MANIFEST_DIR="$PUBLISH_DIR/_manifest"
rm -rf "$MANIFEST_DIR"
export DeleteManifestDirIfPresent=true

"$TOOLS_DIR/sbom-tool" generate \
  -b "$PUBLISH_DIR" \
  -bc "$PROJECT_ROOT/Login/Protons.UI" \
  -pn "$PRODUCT_NAME" \
  -pv "$VERSION" \
  -ps "$PUBLISHER" \
  -nsb "$NAMESPACE_URI" \
  -V Verbose

if [ -d "$MANIFEST_DIR" ]; then
  rm -rf "$OUTPUT_DIR/_manifest"
  cp -a "$MANIFEST_DIR" "$OUTPUT_DIR/"
  rm -rf "$MANIFEST_DIR"
else
  echo "❌ ERRO: SBOM nao foi gerado em $MANIFEST_DIR" >&2
  exit 1
fi

echo "✅ SPDX gerado em: $OUTPUT_DIR/_manifest"
