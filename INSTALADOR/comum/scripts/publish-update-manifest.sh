#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

BASE_URL=""
CHANNEL="stable"
ROLLOUT_PERCENT="100"
SIGNATURE="UNSIGNED"
SIGNATURE_BASE_URL=""
RELEASE_NOTES_URL=""
STRICT_SIGNATURE=0

if [ "${CI:-}" = "true" ] || [ "${CI:-}" = "1" ]; then
  STRICT_SIGNATURE=1
fi

while [ $# -gt 0 ]; do
  case "$1" in
    --base-url)
      [ $# -ge 2 ] || { echo "ERROR: --base-url requires value" >&2; exit 1; }
      BASE_URL="$2"
      shift 2
      ;;
    --channel)
      [ $# -ge 2 ] || { echo "ERROR: --channel requires value" >&2; exit 1; }
      CHANNEL="$2"
      shift 2
      ;;
    --rollout-percent)
      [ $# -ge 2 ] || { echo "ERROR: --rollout-percent requires value" >&2; exit 1; }
      ROLLOUT_PERCENT="$2"
      shift 2
      ;;
    --signature)
      [ $# -ge 2 ] || { echo "ERROR: --signature requires value" >&2; exit 1; }
      SIGNATURE="$2"
      shift 2
      ;;
    --signature-base-url)
      [ $# -ge 2 ] || { echo "ERROR: --signature-base-url requires value" >&2; exit 1; }
      SIGNATURE_BASE_URL="$2"
      shift 2
      ;;
    --release-notes-url)
      [ $# -ge 2 ] || { echo "ERROR: --release-notes-url requires value" >&2; exit 1; }
      RELEASE_NOTES_URL="$2"
      shift 2
      ;;
    --strict-signature)
      STRICT_SIGNATURE=1
      shift
      ;;
    --no-strict-signature)
      STRICT_SIGNATURE=0
      shift
      ;;
    --help|-h)
      cat <<USAGE
Usage: $0 --base-url <url> [options]
  --strict-signature      Enforce signatures (default in CI=true/1)
  --no-strict-signature   Disable strict signature validation
USAGE
      exit 0
      ;;
    *) echo "Unknown arg: $1" >&2; exit 1 ;;
  esac
done

if [ -z "$BASE_URL" ]; then
  echo "ERROR: --base-url is required" >&2
  exit 1
fi

GEN="$SCRIPT_DIR/generate-update-manifest.sh"
VAL="$SCRIPT_DIR/validate-update-manifest.sh"
ARC="$SCRIPT_DIR/archive-update-manifest.sh"

MANIFEST="$PROJECT_ROOT/INSTALADOR/saida/update/update-manifest.json"

gen_args=(
  --base-url "$BASE_URL"
  --channel "$CHANNEL"
  --rollout-percent "$ROLLOUT_PERCENT"
  --signature "$SIGNATURE"
  --output "$MANIFEST"
)

if [ -n "$SIGNATURE_BASE_URL" ]; then
  gen_args+=(--signature-base-url "$SIGNATURE_BASE_URL")
fi

if [ -n "$RELEASE_NOTES_URL" ]; then
  gen_args+=(--release-notes-url "$RELEASE_NOTES_URL")
fi

"$GEN" "${gen_args[@]}"

if [ "$STRICT_SIGNATURE" -eq 1 ]; then
  "$VAL" --manifest "$MANIFEST" --strict-signature
else
  "$VAL" --manifest "$MANIFEST"
fi

"$ARC"

echo "Manifest published and archived: $MANIFEST"
if [ "$STRICT_SIGNATURE" -eq 1 ]; then
  echo "Signature policy: strict"
else
  echo "Signature policy: non-strict"
fi
