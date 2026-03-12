#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALADOR_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
echo "[DEPRECATED] Use: bash troubleshoot-upload.sh" >&2
exec bash "$INSTALADOR_ROOT/troubleshoot-upload.sh" "$@"
