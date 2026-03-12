#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

CHECKLIST_FILE="$INSTALADOR_ROOT/CHECKLIST.md"
RUN_ID=""
ALLOW_RECONCILE="1"

usage() {
  cat <<'EOF'
Uso:
  bash testes/meta/test-checklist-honesty.sh --run-id <YYYYMMDDTHHMMSSZ> [--checklist <path>]
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --checklist)
      CHECKLIST_FILE="${2:-}"
      shift 2
      ;;
    --run-id)
      RUN_ID="${2:-}"
      shift 2
      ;;
    --allow-reconcile)
      ALLOW_RECONCILE="${2:-}"
      shift 2
      ;;
    --strict-append-only)
      ALLOW_RECONCILE="0"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "FAIL: argumento invalido: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

[ -f "$CHECKLIST_FILE" ] || {
  echo "FAIL: checklist nao encontrado: $CHECKLIST_FILE" >&2
  exit 1
}

if ! [[ "$RUN_ID" =~ ^[0-9]{8}T[0-9]{6}Z$ ]]; then
  echo "FAIL: --run-id obrigatorio no formato YYYYMMDDTHHMMSSZ" >&2
  exit 1
fi

if [ "$ALLOW_RECONCILE" != "0" ] && [ "$ALLOW_RECONCILE" != "1" ]; then
  echo "FAIL: --allow-reconcile deve ser 0 ou 1" >&2
  exit 1
fi

heading="## Rodada Paralela ${RUN_ID} (sem Windows)"
section="$(awk -v h="$heading" '
  $0 == h {on=1; print; next}
  on && /^## / {exit}
  on {print}
' "$CHECKLIST_FILE")"

[ -n "$section" ] || {
  echo "FAIL: secao append-only nao encontrada: $heading" >&2
  exit 1
}

required_phrase="Sem alteração de score global nesta rodada paralela (G1-G4 dependem da trilha Windows)."
printf '%s\n' "$section" | rg -F "$required_phrase" >/dev/null || {
  echo "FAIL: frase obrigatoria de honestidade nao encontrada na secao da rodada" >&2
  exit 1
}

# Modo strict opcional para casos de append-only puro.
if [ "$ALLOW_RECONCILE" = "0" ] && command -v git >/dev/null 2>&1; then
  rel_path="$(realpath --relative-to="$PROJECT_ROOT" "$CHECKLIST_FILE" 2>/dev/null || true)"
  if [ -z "$rel_path" ]; then
    rel_path="INSTALADOR/CHECKLIST.md"
  fi
  diff_text="$(git -C "$PROJECT_ROOT" diff -- "$rel_path" || true)"
  if printf '%s\n' "$diff_text" | rg -q '^\-[^-]'; then
    echo "FAIL: checklist nao esta append-only (linhas removidas/modificadas detectadas)" >&2
    exit 1
  fi
fi

missing=0
while IFS= read -r ref; do
  ref="${ref#\`}"
  ref="${ref%\`}"
  case "$ref" in
    saida/*|documentos/*|comum/*|testes/*|CHECKLIST.md|RELATORIO_EXECUCAO_WINDOWS_VM.md)
      if [ ! -e "$INSTALADOR_ROOT/$ref" ]; then
        echo "FAIL: referencia inexistente na secao append-only: $ref" >&2
        missing=1
      fi
      ;;
  esac
done < <(printf '%s\n' "$section" | rg -o '`[^`]+`' || true)

if [ "$missing" -ne 0 ]; then
  exit 1
fi

echo "PASS: checklist e honestidade de score validados"
