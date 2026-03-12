#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
OUTPUT_DIR=""
STOP_ON_FAIL="0"

usage() {
  cat <<'EOF'
Uso:
  bash comum/scripts/run-parallel-safe-round.sh [opcoes]

Opcoes:
  --run-id <YYYYMMDDTHHMMSSZ>  Define RunId da rodada
  --output-dir <path>          Diretorio de saida (default: saida/validacao-paralela-<run-id>)
  --stop-on-fail <0|1>         Se 1, interrompe na primeira falha (default: 0)
  -h, --help                   Exibe ajuda
EOF
}

normalize_output_dir() {
  local raw="$1"
  if [[ "$raw" = /* ]]; then
    printf '%s' "$raw"
  else
    printf '%s/%s' "$INSTALADOR_ROOT" "$raw"
  fi
}

record_step() {
  local step="$1"
  local status="$2"
  local exit_code="$3"
  local log_file="$4"
  echo "${step},${status},${exit_code},${log_file}" >> "$SUMMARY_CSV"
}

run_step() {
  local index="$1"
  local name="$2"
  shift 2
  local log_file="$OUTPUT_DIR/$(printf '%02d' "$index")-${name}.log"

  if "$@" >"$log_file" 2>&1; then
    record_step "$name" "PASS" "0" "$log_file"
    return 0
  fi

  local rc=$?
  record_step "$name" "FAIL" "$rc" "$log_file"
  if [ "$STOP_ON_FAIL" = "1" ]; then
    return 99
  fi
  return 1
}

write_manifest() {
  local run_finished_utc
  run_finished_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  local pass fail total
  pass="$(awk -F, 'NR>1 && $2=="PASS"{c++} END{print c+0}' "$SUMMARY_CSV")"
  fail="$(awk -F, 'NR>1 && $2=="FAIL"{c++} END{print c+0}' "$SUMMARY_CSV")"
  total="$(awk -F, 'NR>1{c++} END{print c+0}' "$SUMMARY_CSV")"

  {
    echo "# Manifesto de Evidencias - Rodada Paralela Segura"
    echo
    echo "- RunId: \`$RUN_ID\`"
    echo "- TimestampUTC: \`$run_finished_utc\`"
    echo "- OutputDir: \`$OUTPUT_DIR\`"
    echo "- stop_on_fail: \`$STOP_ON_FAIL\`"
    echo
    echo "## Resumo"
    echo "- PASS: $pass"
    echo "- FAIL: $fail"
    echo "- TOTAL: $total"
    echo
    echo "## CSV"
    echo "- \`$SUMMARY_CSV\`"
    echo
    echo "## Arquivos gerados"
    find "$OUTPUT_DIR" -maxdepth 1 -type f | sort | while read -r file; do
      echo "- \`$file\`"
    done
  } > "$MANIFEST_MD"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --run-id)
      RUN_ID="${2:-}"
      shift 2
      ;;
    --output-dir)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    --stop-on-fail)
      STOP_ON_FAIL="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: argumento invalido: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if ! [[ "$RUN_ID" =~ ^[0-9]{8}T[0-9]{6}Z$ ]]; then
  echo "ERROR: run-id invalido: $RUN_ID (esperado YYYYMMDDTHHMMSSZ)" >&2
  exit 1
fi

if [ "$STOP_ON_FAIL" != "0" ] && [ "$STOP_ON_FAIL" != "1" ]; then
  echo "ERROR: --stop-on-fail deve ser 0 ou 1" >&2
  exit 1
fi

if [ -z "$OUTPUT_DIR" ]; then
  OUTPUT_DIR="$INSTALADOR_ROOT/saida/validacao-paralela-$RUN_ID"
else
  OUTPUT_DIR="$(normalize_output_dir "$OUTPUT_DIR")"
fi

mkdir -p "$OUTPUT_DIR"
SUMMARY_CSV="$OUTPUT_DIR/resumo.csv"
MANIFEST_MD="$OUTPUT_DIR/manifesto-evidencias.md"
echo "step,status,exit_code,log_file" > "$SUMMARY_CSV"

had_fail=0

run_and_handle() {
  local rc=0
  if run_step "$@"; then
    return 0
  fi
  rc=$?
  had_fail=1
  if [ "$rc" -eq 99 ]; then
    write_manifest
    echo "RUN_ID: $RUN_ID"
    echo "OUTPUT_DIR: $OUTPUT_DIR"
    exit 1
  fi
}

run_and_handle 1 "validate-prohibited-terms" bash "$INSTALADOR_ROOT/comum/scripts/validate-prohibited-terms.sh"
run_and_handle 2 "lint-build" bash "$INSTALADOR_ROOT/comum/scripts/lint-build.sh"
run_and_handle 3 "validate-version" bash "$INSTALADOR_ROOT/comum/scripts/validate-version.sh"
run_and_handle 4 "validate-script-comments" bash "$INSTALADOR_ROOT/comum/scripts/validate-script-comments.sh"
run_and_handle 5 "validate-sbom" bash "$INSTALADOR_ROOT/comum/scripts/validate-sbom.sh"
run_and_handle 6 "validate-update-manifest-strict" bash "$INSTALADOR_ROOT/comum/scripts/validate-update-manifest.sh" --manifest "$INSTALADOR_ROOT/saida/update/update-manifest.json" --strict-signature
run_and_handle 7 "test-update-manifest" bash "$INSTALADOR_ROOT/comum/scripts/test-update-manifest.sh"
run_and_handle 8 "test-linux-appimage" bash "$INSTALADOR_ROOT/testes/linux/test-appimage.sh"
run_and_handle 9 "test-installer-contract-static" bash "$INSTALADOR_ROOT/testes/windows/test-installer-contract-static.sh"
run_and_handle 10 "test-checklist-graficos" bash "$INSTALADOR_ROOT/testes/meta/test-checklist-graficos.sh"
run_and_handle 11 "test-checklist-score-evidence" bash "$INSTALADOR_ROOT/testes/meta/test-checklist-score-evidence.sh"

write_manifest
echo "RUN_ID: $RUN_ID"
echo "OUTPUT_DIR: $OUTPUT_DIR"

if [ "$had_fail" -eq 1 ]; then
  exit 1
fi

exit 0
