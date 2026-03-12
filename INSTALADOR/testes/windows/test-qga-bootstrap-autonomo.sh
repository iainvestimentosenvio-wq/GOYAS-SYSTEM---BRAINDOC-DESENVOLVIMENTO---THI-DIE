#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
AUTON_SCRIPT="$INSTALADOR_ROOT/comum/scripts/windows-autonomous-round.sh"

RUN_ID="${1:-$(date -u +%Y%m%dT%H%M%SZ)}"

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "FAIL: required command not found: $1" >&2
    exit 1
  }
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

run_bootstrap() {
  local vm="$1"
  local log_file="$INSTALADOR_ROOT/saida/test-logs/bootstrap-qga-${vm}-${RUN_ID}.log"
  local bootstrap_mode="${QGA_BOOTSTRAP_MODE:-manual-ready}"
  mkdir -p "$(dirname "$log_file")"

  if ! QGA_BOOTSTRAP_MODE="$bootstrap_mode" \
       bash "$AUTON_SCRIPT" bootstrap-qga --vm "$vm" --run-id "$RUN_ID" >"$log_file" 2>&1; then
    echo "FAIL: bootstrap-qga command failed for $vm. See $log_file" >&2
    exit 1
  fi
}

assert_no_broken_cdrom_source() {
  local vm="$1"
  local dump_file="$RUN_DIR/${vm}-post-bootstrap.dumpxml"
  sg libvirt -c "virsh dumpxml $vm" >"$dump_file"

  if ! python3 - "$dump_file" <<'PY'
import os
import sys
import xml.etree.ElementTree as ET

dump_file = sys.argv[1]
root = ET.parse(dump_file).getroot()
missing = []

for disk in root.findall("./devices/disk"):
    if disk.get("device") != "cdrom":
        continue
    target = disk.find("target")
    source = disk.find("source")
    dev = target.get("dev") if target is not None else "unknown"
    src = source.get("file") if source is not None else ""
    if src and not os.path.exists(src):
        missing.append((dev, src))

if missing:
    for dev, src in missing:
        print(f"missing_cdrom_source dev={dev} file={src}", file=sys.stderr)
    raise SystemExit(1)
PY
  then
    echo "FAIL: broken CD-ROM source detected for $vm (see $dump_file)" >&2
    exit 1
  fi
}

require_cmd bash
require_cmd awk
require_cmd sg
require_cmd virsh
require_cmd python3

[ -f "$AUTON_SCRIPT" ] || {
  echo "FAIL: missing script $AUTON_SCRIPT" >&2
  exit 1
}

rm -rf "$INSTALADOR_ROOT/saida/validacao-windows-${RUN_ID}" "$INSTALADOR_ROOT/saida/validacao-windows-autonoma-${RUN_ID}"

run_bootstrap "win10-lite"
run_bootstrap "win11-lite"

RUN_DIR="$(resolve_run_dir "$RUN_ID")" || {
  echo "FAIL: run directory not found for run_id=$RUN_ID" >&2
  exit 1
}
CSV_FILE="$RUN_DIR/resumo.csv"
SUMMARY_FILE="$RUN_DIR/windows-round-summary.md"

[ -f "$CSV_FILE" ] || {
  echo "FAIL: missing CSV evidence: $CSV_FILE" >&2
  exit 1
}
[ -f "$SUMMARY_FILE" ] || {
  echo "FAIL: missing summary evidence: $SUMMARY_FILE" >&2
  exit 1
}

for vm in win10-lite win11-lite; do
  status="$(awk -F, -v s="bootstrap_qga_${vm}_result" '$1==s{val=$2} END{print val}' "$CSV_FILE")"
  if [ -z "$status" ]; then
    echo "FAIL: missing bootstrap result row for $vm in $CSV_FILE" >&2
    exit 1
  fi
  if [ "${QGA_ALLOW_BLOCKED:-0}" = "1" ]; then
    case "$status" in
      PASS|BLOQUEADO) ;;
      *)
        echo "FAIL: unexpected bootstrap status for $vm: $status" >&2
        exit 1
        ;;
    esac
  else
    if [ "$status" != "PASS" ]; then
      echo "FAIL: strict mode requires PASS for $vm, got '$status' (set QGA_ALLOW_BLOCKED=1 for legacy mode)" >&2
      exit 1
    fi
  fi

  assert_no_broken_cdrom_source "$vm"
done

echo "PASS: QGA bootstrap autonomous flow validated (run_id=$RUN_ID, dir=$RUN_DIR)"
