#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

RUN_ID="${1:-$(date -u +%Y%m%dT%H%M%SZ)}"
OUTPUT_DIR="${2:-$INSTALADOR_ROOT/saida/test-logs}"
REPORT_PATH="$OUTPUT_DIR/go-nogo-test-$RUN_ID.md"
LOG_PATH="$OUTPUT_DIR/go-nogo-test-$RUN_ID.log"

mkdir -p "$OUTPUT_DIR"

set +e
bash "$INSTALADOR_ROOT/comum/scripts/check-go-nogo.sh" --output "$REPORT_PATH" >"$LOG_PATH" 2>&1
rc=$?
set -e

[ -f "$REPORT_PATH" ] || {
  echo "FAIL: report file not generated: $REPORT_PATH" >&2
  exit 1
}

rg -n "^# Gate GO/NO-GO$" "$REPORT_PATH" >/dev/null || {
  echo "FAIL: missing report header in $REPORT_PATH" >&2
  exit 1
}

rg -n "^## Criterios$" "$REPORT_PATH" >/dev/null || {
  echo "FAIL: missing criteria section in $REPORT_PATH" >&2
  exit 1
}

decision="$(sed -nE 's/^- Decisao final: \*\*([A-Z-]+)\*\*$/\1/p' "$REPORT_PATH" | head -n1)"

if [ -z "$decision" ]; then
  echo "FAIL: missing final decision line in $REPORT_PATH" >&2
  exit 1
fi

case "$decision" in
  GO)
    if [ "$rc" -ne 0 ]; then
      echo "FAIL: decision is GO but script exit code is $rc" >&2
      exit 1
    fi
    if rg -n '^\| C[0-9]+ \| .* \| FAIL \|' "$REPORT_PATH" >/dev/null; then
      echo "FAIL: decision is GO but there are failed criteria" >&2
      exit 1
    fi
    ;;
  NO-GO)
    if [ "$rc" -eq 0 ]; then
      echo "FAIL: decision is NO-GO but script exit code is 0" >&2
      exit 1
    fi
    if ! rg -n '^\| C[0-9]+ \| .* \| FAIL \|' "$REPORT_PATH" >/dev/null; then
      echo "FAIL: decision is NO-GO but no failed criterion was found" >&2
      exit 1
    fi
    ;;
  *)
    echo "FAIL: unexpected decision value: $decision" >&2
    exit 1
    ;;
esac

echo "PASS: go-nogo gate report validated (decision=$decision, exit_code=$rc)"
