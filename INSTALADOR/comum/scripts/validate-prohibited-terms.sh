#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

# Escopo documental oficial: evita falso positivo em logs tecnicos de validacao.
TARGET_DOCS=(
  "$PROJECT_ROOT/INSTALADOR/documentos"
  "$PROJECT_ROOT/INSTALADOR/saida/RELATORIO-EXECUTIVO-FINAL.txt"
  "$PROJECT_ROOT/INSTALADOR/saida/RESULTADO-FINAL.txt"
  "$PROJECT_ROOT/INSTALADOR/saida/entrega-final/README.md"
)

TERMS=(
  "\\bIA\\b"
  "inteligencia artificial"
  "chatgpt"
  "codex"
  "openai"
  "gerado automaticamente"
)

args=()
for term in "${TERMS[@]}"; do
  args+=("-e" "$term")
done

if rg -n -i "${args[@]}" "${TARGET_DOCS[@]}"; then
  echo "ERROR: prohibited terms detected in final documentation." >&2
  exit 1
fi

echo "OK: prohibited terms not found in documentation targets."
