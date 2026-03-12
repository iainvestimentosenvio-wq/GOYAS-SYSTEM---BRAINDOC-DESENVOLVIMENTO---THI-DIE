#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
AUTON_SCRIPT="$INSTALADOR_ROOT/comum/scripts/windows-autonomous-round.sh"

RUN_ID="${1:-$(date -u +%Y%m%dT%H%M%SZ)}"
WINDOWS_VM_SET="${WINDOWS_VM_SET:-win10-lite,win11-lite}"

parse_vm_set() {
  local raw="$1"
  local item
  local -a parsed=()
  IFS=',' read -r -a items <<< "$raw"
  for item in "${items[@]}"; do
    item="$(echo "$item" | tr -d '[:space:]')"
    [ -n "$item" ] || continue
    parsed+=("$item")
  done
  if [ "${#parsed[@]}" -eq 0 ]; then
    echo "FAIL: WINDOWS_VM_SET vazio" >&2
    exit 1
  fi
  printf '%s\n' "${parsed[@]}"
}

resolve_run_dir() {
  local run_id="$1"
  local d1="$INSTALADOR_ROOT/saida/validacao-windows-${run_id}"
  local d2="$INSTALADOR_ROOT/saida/validacao-windows-autonoma-${run_id}"
  if [ -d "$d1" ]; then
    printf '%s' "$d1"
    return 0
  fi
  if [ -d "$d2" ]; then
    printf '%s' "$d2"
    return 0
  fi
  return 1
}

run_regressao() {
  local vm="$1"
  local log_file="$INSTALADOR_ROOT/saida/test-logs/run-regressao-autonoma-${vm}-${RUN_ID}.log"
  mkdir -p "$(dirname "$log_file")"

  if ! bash "$AUTON_SCRIPT" run-regressao --vm "$vm" --run-id "$RUN_ID" >"$log_file" 2>&1; then
    echo "FAIL: run-regressao command failed for $vm. See $log_file" >&2
    exit 1
  fi
}

[ -f "$AUTON_SCRIPT" ] || {
  echo "FAIL: missing script $AUTON_SCRIPT" >&2
  exit 1
}

rm -rf "$INSTALADOR_ROOT/saida/validacao-windows-${RUN_ID}" "$INSTALADOR_ROOT/saida/validacao-windows-autonoma-${RUN_ID}"

mapfile -t VM_LIST < <(parse_vm_set "$WINDOWS_VM_SET")
for vm in "${VM_LIST[@]}"; do
  run_regressao "$vm"
done

RUN_DIR="$(resolve_run_dir "$RUN_ID")" || {
  echo "FAIL: run directory not found for run_id=$RUN_ID" >&2
  exit 1
}
CSV_FILE="$RUN_DIR/resumo.csv"

[ -f "$CSV_FILE" ] || {
  echo "FAIL: missing CSV evidence: $CSV_FILE" >&2
  exit 1
}

for vm in "${VM_LIST[@]}"; do
  step_prefix="run_regressao_${vm}"
  status="$(awk -F, -v s="${step_prefix}_result" '$1==s{val=$2} END{print val}' "$CSV_FILE")"
  if [ -z "$status" ]; then
    echo "FAIL: missing regression result row for $vm in $CSV_FILE" >&2
    exit 1
  fi

  if [ "${QGA_ALLOW_BLOCKED:-0}" = "1" ]; then
    case "$status" in
      PASS)
        pulled_count="$(find "$RUN_DIR" -maxdepth 1 -type f -name "${vm}-guest-*" | wc -l | tr -d ' ')"
        if [ "${pulled_count:-0}" -lt 1 ]; then
          echo "FAIL: regression marked PASS for $vm but no ${vm}-guest-* artifact was collected" >&2
          exit 1
        fi
        ;;
      BLOQUEADO)
        ping_status="$(awk -F, -v s="${step_prefix}_qga_ping" '$1==s{val=$2} END{print val}' "$CSV_FILE")"
        if [ "$ping_status" != "BLOQUEADO" ]; then
          echo "FAIL: $vm result BLOQUEADO but qga_ping row is '$ping_status'" >&2
          exit 1
        fi
        ;;
      *)
        echo "FAIL: unexpected regression status for $vm: $status" >&2
        exit 1
        ;;
    esac
  else
    if [ "$status" != "PASS" ]; then
      echo "FAIL: strict mode requires PASS for $vm, got '$status' (set QGA_ALLOW_BLOCKED=1 for legacy mode)" >&2
      exit 1
    fi
    required_files=(
      "${vm}-guest-regressao-windows-${RUN_ID}.json"
      "${vm}-guest-msi-results-${RUN_ID}.json"
      "${vm}-guest-inno-results-${RUN_ID}.json"
      "${vm}-guest-artifact-verify-${RUN_ID}.json"
      "${vm}-guest-msi-metrics-${RUN_ID}.json"
      "${vm}-guest-inno-metrics-${RUN_ID}.json"
      "${vm}-guest-upgrade-metrics-${RUN_ID}.json"
    )
    for req in "${required_files[@]}"; do
      if [ ! -f "$RUN_DIR/$req" ]; then
        echo "FAIL: strict mode requires file $req for $vm" >&2
        exit 1
      fi
    done

    msi_total="$(python3 - "$RUN_DIR/${vm}-guest-msi-results-${RUN_ID}.json" <<'PY'
import json,sys
with open(sys.argv[1], encoding='utf-8-sig') as f:
    data=json.load(f)
print(int(data.get('summary',{}).get('TOTAL',0)))
PY
)"
    inno_total="$(python3 - "$RUN_DIR/${vm}-guest-inno-results-${RUN_ID}.json" <<'PY'
import json,sys
with open(sys.argv[1], encoding='utf-8-sig') as f:
    data=json.load(f)
print(int(data.get('summary',{}).get('TOTAL',0)))
PY
)"
    if [ "${msi_total:-0}" -le 0 ]; then
      echo "FAIL: strict mode requires msi summary TOTAL>0 for $vm" >&2
      exit 1
    fi
    if [ "${inno_total:-0}" -le 0 ]; then
      echo "FAIL: strict mode requires inno summary TOTAL>0 for $vm" >&2
      exit 1
    fi
  fi
done

echo "PASS: autonomous regression flow validated (run_id=$RUN_ID, dir=$RUN_DIR)"
