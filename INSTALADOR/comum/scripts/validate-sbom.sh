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

SBOM_JSON="$PROJECT_ROOT/INSTALADOR/saida/sbom/protons-${VERSION}-sbom.json"
SBOM_RAW="$PROJECT_ROOT/INSTALADOR/saida/sbom/protons-${VERSION}-sbom"
SBOM_FILE="$SBOM_JSON"

if [ ! -f "$SBOM_FILE" ]; then
  if [ -f "$SBOM_RAW" ]; then
    SBOM_FILE="$SBOM_RAW"
  else
    echo "❌ SBOM nao encontrado: $SBOM_JSON" >&2
    exit 1
  fi
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "❌ jq nao encontrado (necessario para validar JSON)" >&2
  exit 1
fi

jq empty "$SBOM_FILE" || { echo "❌ JSON invalido"; exit 1; }

COMPONENTS=$(jq '.components | length' "$SBOM_FILE")
if [ "$COMPONENTS" -le 0 ]; then
  echo "❌ SBOM vazio" >&2
  exit 1
fi

VERSION_FOUND=$(jq -r '.metadata.component.version // empty' "$SBOM_FILE")
if [ -z "$VERSION_FOUND" ]; then
  echo "❌ Versao ausente em metadata.component.version" >&2
  exit 1
fi

if [ "$VERSION_FOUND" != "$VERSION" ]; then
  echo "❌ Versao SBOM ($VERSION_FOUND) difere de version.env ($VERSION)" >&2
  exit 1
fi

echo "✅ SBOM valido ($COMPONENTS componentes, versao $VERSION_FOUND)"
