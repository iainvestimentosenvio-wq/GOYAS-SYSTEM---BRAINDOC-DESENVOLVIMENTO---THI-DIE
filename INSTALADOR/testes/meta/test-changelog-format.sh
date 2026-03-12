#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
CHANGELOG_FILE="$INSTALADOR_ROOT/documentos/CHANGELOG_RELEASE_FINAL.md"

[ -f "$CHANGELOG_FILE" ] || {
  echo "FAIL: changelog nao encontrado: $CHANGELOG_FILE" >&2
  exit 1
}

rg -n "^DataUTC:[[:space:]]+[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$" "$CHANGELOG_FILE" >/dev/null || {
  echo "FAIL: campo DataUTC ausente ou invalido em $CHANGELOG_FILE" >&2
  exit 1
}

rg -n "^Versao:[[:space:]]+[0-9]+\\.[0-9]+\\.[0-9]+$" "$CHANGELOG_FILE" >/dev/null || {
  echo "FAIL: campo Versao ausente ou invalido em $CHANGELOG_FILE" >&2
  exit 1
}

for section in "## Mudancas" "## Riscos aceitos" "## Bloqueios"; do
  rg -n "^${section}$" "$CHANGELOG_FILE" >/dev/null || {
    echo "FAIL: secao obrigatoria ausente: $section" >&2
    exit 1
  }
done

section_has_bullet() {
  local start="$1"
  awk -v start="$start" '
    $0 == start {in_section=1; next}
    in_section && /^## / {exit}
    in_section && /^- / {found=1}
    END {exit(found ? 0 : 1)}
  ' "$CHANGELOG_FILE"
}

for section in "## Mudancas" "## Riscos aceitos" "## Bloqueios"; do
  if ! section_has_bullet "$section"; then
    echo "FAIL: secao sem bullet em $CHANGELOG_FILE: $section" >&2
    exit 1
  fi
done

echo "PASS: formato do changelog validado"
