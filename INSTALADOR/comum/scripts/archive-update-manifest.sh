#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

MANIFEST_DIR="$PROJECT_ROOT/INSTALADOR/saida/update"
CURRENT_MANIFEST="$MANIFEST_DIR/update-manifest.json"
HISTORY_DIR="$MANIFEST_DIR/history"

if [ ! -f "$CURRENT_MANIFEST" ]; then
  echo "ERROR: current manifest not found: $CURRENT_MANIFEST" >&2
  exit 1
fi

mkdir -p "$HISTORY_DIR"
version="$(jq -r '.version' "$CURRENT_MANIFEST")"
out="$HISTORY_DIR/update-manifest-${version}.json"
cp "$CURRENT_MANIFEST" "$out"

echo "Archived: $out"
