#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
CHECKLIST_FILE="$PROJECT_ROOT/INSTALADOR/CHECKLIST.md"

[ -f "$CHECKLIST_FILE" ] || {
  echo "FAIL: checklist file not found: $CHECKLIST_FILE" >&2
  exit 1
}

if ! rg -n "score so sobe com evidencia de run id" "$CHECKLIST_FILE" >/dev/null; then
  echo "FAIL: checklist must explicitly state score-evidence rule" >&2
  exit 1
fi

table_rows="$(awk '
  /^### Registro de score por evidencia \(rodada atual\)/ {insec=1; next}
  insec && /^### / {insec=0}
  insec && /^\|/ {print}
' "$CHECKLIST_FILE")"

if [ -z "$table_rows" ]; then
  echo "FAIL: missing score-evidence table in checklist" >&2
  exit 1
fi

parsed_rows=0

while IFS= read -r line; do
  # Ignore header and separator lines.
  if [[ "$line" =~ ^\|\ Pilar\  ]] || [[ "$line" =~ ^\|\ ---\  ]]; then
    continue
  fi

  IFS='|' read -r _ pillar before after delta gate run_id evidence _ <<< "$line"

  before="$(echo "$before" | tr -d ' `\r')"
  after="$(echo "$after" | tr -d ' `\r')"
  delta="$(echo "$delta" | tr -d ' `\r')"
  run_id="$(echo "$run_id" | sed -E 's/^[[:space:]]+|[[:space:]]+$//g' | tr -d '`\r')"
  evidence="$(echo "$evidence" | sed -E 's/^[[:space:]]+|[[:space:]]+$//g' | tr -d '`\r')"

  [[ "$before" =~ ^-?[0-9]+$ ]] || {
    echo "FAIL: invalid 'Antes' value in row: $line" >&2
    exit 1
  }
  [[ "$after" =~ ^-?[0-9]+$ ]] || {
    echo "FAIL: invalid 'Depois' value in row: $line" >&2
    exit 1
  }
  [[ "$delta" =~ ^-?[0-9]+$ ]] || {
    echo "FAIL: invalid 'Delta' value in row: $line" >&2
    exit 1
  }

  expected_delta=$((after - before))
  if [ "$delta" -ne "$expected_delta" ]; then
    echo "FAIL: delta mismatch in row: $line (expected $expected_delta)" >&2
    exit 1
  fi

  if [ "$delta" -gt 0 ]; then
    if ! [[ "$run_id" =~ ^[0-9]{8}T[0-9]{6}Z$ ]]; then
      echo "FAIL: positive delta requires valid RunId, got '$run_id' in row: $line" >&2
      exit 1
    fi
    if [ -z "$evidence" ] || [ "$evidence" = "N/A" ]; then
      echo "FAIL: positive delta requires evidence path in row: $line" >&2
      exit 1
    fi
    if [ ! -e "$PROJECT_ROOT/INSTALADOR/$evidence" ]; then
      echo "FAIL: evidence path does not exist: $evidence" >&2
      exit 1
    fi
  fi

  parsed_rows=$((parsed_rows + 1))
done <<< "$table_rows"

if [ "$parsed_rows" -lt 5 ]; then
  echo "FAIL: expected at least 5 score-evidence rows, got $parsed_rows" >&2
  exit 1
fi

echo "PASS: checklist score-evidence consistency validated"
