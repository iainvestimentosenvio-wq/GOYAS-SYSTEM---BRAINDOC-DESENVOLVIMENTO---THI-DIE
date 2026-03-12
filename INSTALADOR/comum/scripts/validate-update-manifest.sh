#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

MANIFEST_PATH="$PROJECT_ROOT/INSTALADOR/saida/update/update-manifest.json"
STRICT_SIGNATURE=0

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

is_abs_url() {
  [[ "$1" =~ ^(https://|file://).+ ]]
}

while [ $# -gt 0 ]; do
  case "$1" in
    --manifest)
      [ $# -ge 2 ] || fail "--manifest requires a value"
      MANIFEST_PATH="$2"
      shift 2
      ;;
    --strict-signature)
      STRICT_SIGNATURE=1
      shift
      ;;
    --help|-h)
      echo "Usage: $0 [--manifest <path>] [--strict-signature]"
      exit 0
      ;;
    *)
      if [ -f "$1" ] || [[ "$1" == *.json ]]; then
        MANIFEST_PATH="$1"
        shift
      else
        fail "Unknown argument: $1"
      fi
      ;;
  esac
done

[ -f "$MANIFEST_PATH" ] || fail "manifest not found: $MANIFEST_PATH"
command -v jq >/dev/null 2>&1 || fail "jq not found"

jq empty "$MANIFEST_PATH" >/dev/null

required_fields=(
  "version"
  "channel"
  "published_at_utc"
  "min_supported_version"
  "artifacts"
  "release_notes_url"
  "rollout_percent"
)

for field in "${required_fields[@]}"; do
  if ! jq -e --arg f "$field" 'has($f)' "$MANIFEST_PATH" >/dev/null; then
    fail "missing required field: $field"
  fi
done

channel="$(jq -r '.channel // empty' "$MANIFEST_PATH")"
case "$channel" in
  stable|beta) ;;
  *) fail "invalid channel: '$channel' (expected stable|beta)" ;;
esac

published_at_utc="$(jq -r '.published_at_utc // empty' "$MANIFEST_PATH")"
if ! [[ "$published_at_utc" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]]; then
  fail "invalid published_at_utc format: '$published_at_utc' (expected YYYY-MM-DDTHH:MM:SSZ)"
fi

release_notes_url="$(jq -r '.release_notes_url // empty' "$MANIFEST_PATH")"
is_abs_url "$release_notes_url" || fail "release_notes_url must be absolute (https:// or file://): '$release_notes_url'"

rollout_percent="$(jq -r '.rollout_percent // empty' "$MANIFEST_PATH")"
[[ "$rollout_percent" =~ ^[0-9]+$ ]] || fail "rollout_percent must be integer 0..100"
if [ "$rollout_percent" -lt 0 ] || [ "$rollout_percent" -gt 100 ]; then
  fail "rollout_percent out of range: $rollout_percent"
fi

artifacts_type="$(jq -r '.artifacts | type' "$MANIFEST_PATH")"
[ "$artifacts_type" = "array" ] || fail "artifacts must be an array"

artifacts_len="$(jq -r '.artifacts | length' "$MANIFEST_PATH")"
[ "$artifacts_len" -gt 0 ] || fail "artifacts list must not be empty"

idx=0
while IFS= read -r artifact; do
  idx=$((idx + 1))

  platform="$(jq -r '.platform // empty' <<<"$artifact")"
  case "$platform" in
    windows|linux) ;;
    *) fail "artifacts[$idx].platform invalid: '$platform'" ;;
  esac

  type="$(jq -r '.type // empty' <<<"$artifact")"
  case "$type" in
    msi|exe|deb|appimage) ;;
    *) fail "artifacts[$idx].type invalid: '$type'" ;;
  esac

  url="$(jq -r '.url // empty' <<<"$artifact")"
  is_abs_url "$url" || fail "artifacts[$idx].url must be absolute (https:// or file://): '$url'"

  sha256="$(jq -r '.sha256 // empty' <<<"$artifact")"
  [[ "$sha256" =~ ^[A-Fa-f0-9]{64}$ ]] || fail "artifacts[$idx].sha256 invalid (expected 64 hex chars)"

  size_bytes="$(jq -r '.size_bytes // empty' <<<"$artifact")"
  [[ "$size_bytes" =~ ^[0-9]+$ ]] || fail "artifacts[$idx].size_bytes must be integer > 0"
  [ "$size_bytes" -gt 0 ] || fail "artifacts[$idx].size_bytes must be > 0"

  signature="$(jq -r '.signature // empty' <<<"$artifact")"
  [ -n "$signature" ] || fail "artifacts[$idx].signature must not be empty"

  if [ "$signature" = "UNSIGNED" ]; then
    if [ "$STRICT_SIGNATURE" -eq 1 ]; then
      fail "artifacts[$idx].signature is UNSIGNED but strict signature mode is enabled"
    fi
  else
    is_abs_url "$signature" || fail "artifacts[$idx].signature must be absolute URL or UNSIGNED"
  fi
done < <(jq -c '.artifacts[]' "$MANIFEST_PATH")

if [ "$STRICT_SIGNATURE" -eq 1 ]; then
  echo "OK: manifest is valid (strict signature mode): $MANIFEST_PATH"
else
  echo "OK: manifest is valid: $MANIFEST_PATH"
fi
