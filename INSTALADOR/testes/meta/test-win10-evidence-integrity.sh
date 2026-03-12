#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

RUN_ID="${1:-}"
VM_NAME="${2:-win10-lite}"

if [ -z "$RUN_ID" ]; then
  echo "FAIL: informe RUN_ID. Exemplo: bash testes/meta/test-win10-evidence-integrity.sh 20260215T123000Z" >&2
  exit 1
fi

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

RUN_DIR="$(resolve_run_dir "$RUN_ID")" || {
  echo "FAIL: run directory not found for run_id=$RUN_ID" >&2
  exit 1
}

required_json=(
  "${RUN_DIR}/${VM_NAME}-guest-regressao-windows-${RUN_ID}.json"
  "${RUN_DIR}/${VM_NAME}-guest-msi-results-${RUN_ID}.json"
  "${RUN_DIR}/${VM_NAME}-guest-inno-results-${RUN_ID}.json"
  "${RUN_DIR}/${VM_NAME}-guest-artifact-verify-${RUN_ID}.json"
  "${RUN_DIR}/${VM_NAME}-guest-msi-metrics-${RUN_ID}.json"
  "${RUN_DIR}/${VM_NAME}-guest-inno-metrics-${RUN_ID}.json"
  "${RUN_DIR}/${VM_NAME}-guest-upgrade-metrics-${RUN_ID}.json"
)

for file in "${required_json[@]}"; do
  if [ ! -f "$file" ]; then
    echo "FAIL: required file missing: $file" >&2
    exit 1
  fi
done

python3 - "$RUN_ID" "${required_json[@]}" <<'PY'
import json
import sys

run_id = sys.argv[1]
paths = sys.argv[2:]

def load(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)

data = {p: load(p) for p in paths}

for path, obj in data.items():
    obj_run_id = obj.get("run_id")
    if obj_run_id != run_id:
        raise SystemExit(f"FAIL: run_id mismatch in {path}: expected {run_id}, got {obj_run_id}")

msi_path = next(p for p in paths if p.endswith(f"msi-results-{run_id}.json"))
inno_path = next(p for p in paths if p.endswith(f"inno-results-{run_id}.json"))
reg_path = next(p for p in paths if p.endswith(f"regressao-windows-{run_id}.json"))

msi_total = int(data[msi_path].get("summary", {}).get("TOTAL", 0))
inno_total = int(data[inno_path].get("summary", {}).get("TOTAL", 0))
if msi_total <= 0:
    raise SystemExit(f"FAIL: MSI summary TOTAL<=0 in {msi_path}")
if inno_total <= 0:
    raise SystemExit(f"FAIL: INNO summary TOTAL<=0 in {inno_path}")

perf_checks = data[reg_path].get("performance", {}).get("checks", [])
required_metrics = {
    "msi_install_p95_seconds",
    "msi_uninstall_p95_seconds",
    "inno_install_p95_seconds",
    "inno_uninstall_p95_seconds",
}

seen = {row.get("metric"): row for row in perf_checks if isinstance(row, dict)}
missing = sorted(required_metrics - set(seen.keys()))
if missing:
    raise SystemExit(f"FAIL: missing performance metrics in {reg_path}: {', '.join(missing)}")

for metric in sorted(required_metrics):
    row = seen[metric]
    if not bool(row.get("available")):
        raise SystemExit(f"FAIL: metric not available ({metric}) in {reg_path}")
    value = row.get("value")
    try:
        value = float(value)
    except Exception:
        raise SystemExit(f"FAIL: metric value not numeric ({metric}) in {reg_path}")
    if value <= 0:
        raise SystemExit(f"FAIL: metric value must be > 0 ({metric}) in {reg_path}")

print("PASS: win10 evidence integrity validated")
PY

echo "PASS: evidence integrity ok (run_id=$RUN_ID, vm=$VM_NAME, dir=$RUN_DIR)"
