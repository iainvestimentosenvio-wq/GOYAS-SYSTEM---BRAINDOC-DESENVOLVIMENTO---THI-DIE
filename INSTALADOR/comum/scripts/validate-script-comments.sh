#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

TARGETS=(
  "$PROJECT_ROOT/INSTALADOR/testes"
  "$PROJECT_ROOT/INSTALADOR/windows/scripts"
  "$PROJECT_ROOT/INSTALADOR/linux"
  "$PROJECT_ROOT/INSTALADOR/comum/scripts"
)

# Heuristics for stale comments that should be reviewed.
PATTERNS=(
  "\\bTODO\\b"
  "\\bFIXME\\b"
  "\\bHACK\\b"
  "nao implementado"
  "\\bplaceholder\\b"
)

echo "Scanning scripts for stale comments..."
fail=0

for target in "${TARGETS[@]}"; do
  if [ ! -d "$target" ]; then
    continue
  fi

  for pattern in "${PATTERNS[@]}"; do
    if rg -n -i --glob '*.ps1' --glob '*.sh' --glob '*.iss' "^[[:space:]]*(#|;).*${pattern}" "$target"; then
      fail=1
    fi
  done
done

if [ "$fail" -ne 0 ]; then
  echo "ERROR: stale comments found. Review required." >&2
  exit 1
fi

echo "OK: no stale comment markers found."
