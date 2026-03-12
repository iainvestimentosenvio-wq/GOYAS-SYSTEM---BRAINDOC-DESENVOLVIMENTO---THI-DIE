#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

MANIFEST_DIR="$PROJECT_ROOT/INSTALADOR/saida/update"
HISTORY_DIR="$MANIFEST_DIR/history"
CURRENT_MANIFEST="$MANIFEST_DIR/update-manifest.json"
TARGET_VERSION="${1:-}"

if [ ! -d "$HISTORY_DIR" ]; then
  echo "ERROR: history directory not found: $HISTORY_DIR" >&2
  exit 1
fi

if [ -n "$TARGET_VERSION" ]; then
  target="$HISTORY_DIR/update-manifest-${TARGET_VERSION}.json"
  if [ ! -f "$target" ]; then
    echo "ERROR: target version not found in history: $TARGET_VERSION" >&2
    exit 1
  fi
else
  target="$(ls -1 "$HISTORY_DIR"/update-manifest-*.json 2>/dev/null | sort | tail -n1 || true)"
  if [ -z "$target" ]; then
    echo "ERROR: no historical manifests found" >&2
    exit 1
  fi
fi

cp "$target" "$CURRENT_MANIFEST"

version="$(jq -r '.version' "$CURRENT_MANIFEST")"
echo "Rollback completed. Active manifest: $CURRENT_MANIFEST"
echo "Active version: $version"
