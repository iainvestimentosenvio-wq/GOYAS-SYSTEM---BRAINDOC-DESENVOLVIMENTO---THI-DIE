#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
echo "[DEPRECATED] Use: bash run-windows-p10-gate.sh" >&2
exec bash "$SCRIPT_DIR/run-windows-p10-gate.sh" "$@"
