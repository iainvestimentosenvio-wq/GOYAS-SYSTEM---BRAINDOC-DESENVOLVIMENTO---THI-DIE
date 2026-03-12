#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CHECKLIST_PATH="$ROOT_DIR/painel principal/codigos/painel_principal/funcionalidades/ferramentas/ancorar_pdf/documentacao/CHECKLIST_ANCORA_PDF.md"
BLOCK_FILE=""

START_MARKER="<!-- CHECKLIST12_AUTO_VALIDATION_START -->"
END_MARKER="<!-- CHECKLIST12_AUTO_VALIDATION_END -->"

usage() {
  cat <<'USAGE'
Uso:
  bash Login/scripts/checklist12_smart_detector_sync_md.sh \
    --block-file Login/testes/TestResults/checklist12_smart_detector/checklist_block.md \
    [--checklist caminho/do/CHECKLIST_ANCORA_PDF.md]
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --block-file)
      BLOCK_FILE="${2:-}"
      shift 2
      ;;
    --checklist)
      CHECKLIST_PATH="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Parametro invalido: $1" >&2
      usage
      exit 2
      ;;
  esac
done

if [[ -z "$BLOCK_FILE" ]]; then
  echo "Informe --block-file com o conteudo gerado para o bloco automatico." >&2
  exit 2
fi

if [[ ! -f "$CHECKLIST_PATH" ]]; then
  echo "Checklist nao encontrado: $CHECKLIST_PATH" >&2
  exit 3
fi

if [[ ! -f "$BLOCK_FILE" ]]; then
  echo "Arquivo de bloco nao encontrado: $BLOCK_FILE" >&2
  exit 3
fi

tmp_file="$(mktemp)"
trap 'rm -f "$tmp_file"' EXIT

set +e
awk -v start="$START_MARKER" -v end="$END_MARKER" -v block_file="$BLOCK_FILE" '
BEGIN {
  in_block = 0
  found_start = 0
  found_end = 0
}
$0 == start {
  print $0
  while ((getline line < block_file) > 0) {
    print line
  }
  close(block_file)
  in_block = 1
  found_start = 1
  next
}
$0 == end {
  in_block = 0
  found_end = 1
  print $0
  next
}
!in_block {
  print $0
}
END {
  if (!found_start || !found_end) {
    exit 5
  }
}
' "$CHECKLIST_PATH" >"$tmp_file"
awk_exit_code=$?
set -e

if [[ $awk_exit_code -ne 0 ]]; then
  echo "Falha ao atualizar bloco automatico C12. Verifique os marcadores START/END no checklist." >&2
  exit 5
fi

mv "$tmp_file" "$CHECKLIST_PATH"
echo "Checklist C12 sincronizado: $CHECKLIST_PATH"
