#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
echo "[DEPRECATED] Use: bash build-and-run-windows-gate.sh" >&2
exec bash "$SCRIPT_DIR/build-and-run-windows-gate.sh" "$@"
