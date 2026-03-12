#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"
LOG_DIR="$PROJECT_ROOT/INSTALADOR/saida/update/logs"
mkdir -p "$LOG_DIR"

MANIFEST=""
PLATFORM="linux"
TYPE="appimage"
DOWNLOAD_DIR="$PROJECT_ROOT/INSTALADOR/saida/update/downloads"
APPLY=0
REQUIRE_SIGNATURE=0
FAIL_ON_UNSIGNED=0
PUBLIC_KEY=""

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

is_abs_url() {
  [[ "$1" =~ ^(https://|file://).+ ]]
}

log_jsonl="$LOG_DIR/update-check-$(date -u +%Y%m%dT%H%M%SZ).jsonl"

log_event() {
  local event="$1"
  local result="$2"
  local artifact="$3"
  local details="$4"

  jq -nc \
    --arg timestamp_utc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg event "$event" \
    --arg result "$result" \
    --arg artifact "$artifact" \
    --arg details "$details" \
    '{timestamp_utc:$timestamp_utc,event:$event,result:$result,artifact:$artifact,details:$details}' >> "$log_jsonl"
}

fetch_url() {
  local url="$1"
  local output="$2"

  if ! is_abs_url "$url"; then
    return 10
  fi

  if [[ "$url" == file://* ]]; then
    local local_path
    local_path="${url#file://}"
    cp "$local_path" "$output"
    return 0
  fi

  if [[ "$url" == https://* ]]; then
    if command -v curl >/dev/null 2>&1; then
      curl -fsSL "$url" -o "$output"
      return 0
    fi
    if command -v wget >/dev/null 2>&1; then
      wget -qO "$output" "$url"
      return 0
    fi
    return 11
  fi

  return 12
}

version_gt() {
  [ "$(printf '%s\n' "$1" "$2" | sort -V | tail -n1)" = "$1" ] && [ "$1" != "$2" ]
}

while [ $# -gt 0 ]; do
  case "$1" in
    --manifest)
      [ $# -ge 2 ] || fail "--manifest requires a value"
      MANIFEST="$2"
      shift 2
      ;;
    --platform)
      [ $# -ge 2 ] || fail "--platform requires a value"
      PLATFORM="$2"
      shift 2
      ;;
    --type)
      [ $# -ge 2 ] || fail "--type requires a value"
      TYPE="$2"
      shift 2
      ;;
    --download-dir)
      [ $# -ge 2 ] || fail "--download-dir requires a value"
      DOWNLOAD_DIR="$2"
      shift 2
      ;;
    --apply)
      APPLY=1
      shift
      ;;
    --require-signature)
      REQUIRE_SIGNATURE=1
      FAIL_ON_UNSIGNED=1
      shift
      ;;
    --fail-on-unsigned)
      FAIL_ON_UNSIGNED=1
      shift
      ;;
    --public-key)
      [ $# -ge 2 ] || fail "--public-key requires a value"
      PUBLIC_KEY="$2"
      shift 2
      ;;
    --help|-h)
      cat <<USAGE
Usage: $0 [options]
  --manifest <path>
  --platform <windows|linux>
  --type <msi|exe|deb|appimage>
  --download-dir <path>
  --require-signature
  --public-key <pem>
  --fail-on-unsigned
USAGE
      exit 0
      ;;
    *) fail "Unknown arg: $1" ;;
  esac
done

if [ -z "$MANIFEST" ]; then
  MANIFEST="$PROJECT_ROOT/INSTALADOR/saida/update/update-manifest.json"
fi

[ -f "$VERSION_FILE" ] || fail "version file not found: $VERSION_FILE"
[ -f "$MANIFEST" ] || fail "manifest not found: $MANIFEST"
command -v jq >/dev/null 2>&1 || fail "jq not found"
command -v sha256sum >/dev/null 2>&1 || fail "sha256sum not found"

if [ -n "$PUBLIC_KEY" ] && [ ! -f "$PUBLIC_KEY" ]; then
  fail "public key not found: $PUBLIC_KEY"
fi

CURRENT_VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | head -n1 | cut -d= -f2 | tr -d '"')"
TARGET_VERSION="$(jq -r '.version // empty' "$MANIFEST")"
[ -n "$TARGET_VERSION" ] || fail "manifest version is empty"

if version_gt "$TARGET_VERSION" "$CURRENT_VERSION"; then
  log_event "check" "update_available" "$PLATFORM/$TYPE" "Current=$CURRENT_VERSION Target=$TARGET_VERSION"
else
  log_event "check" "up_to_date" "$PLATFORM/$TYPE" "Current=$CURRENT_VERSION Target=$TARGET_VERSION"
  echo "No update required. Current=$CURRENT_VERSION Target=$TARGET_VERSION"
  echo "Log: $log_jsonl"
  exit 0
fi

artifact_json="$(jq -c --arg p "$PLATFORM" --arg t "$TYPE" '.artifacts[] | select(.platform==$p and .type==$t)' "$MANIFEST" | head -n1)"
if [ -z "$artifact_json" ]; then
  log_event "resolve_artifact" "fail" "$PLATFORM/$TYPE" "Artifact not found in manifest"
  fail "artifact $PLATFORM/$TYPE not found in manifest"
fi

url="$(jq -r '.url // empty' <<<"$artifact_json")"
sha="$(jq -r '.sha256 // empty' <<<"$artifact_json")"
signature_ref="$(jq -r '.signature // empty' <<<"$artifact_json")"

if ! is_abs_url "$url"; then
  log_event "resolve_artifact" "fail" "$PLATFORM/$TYPE" "Artifact URL must be absolute: $url"
  fail "artifact URL must be absolute (https:// or file://): $url"
fi

[[ "$sha" =~ ^[A-Fa-f0-9]{64}$ ]] || fail "manifest sha256 is invalid for $PLATFORM/$TYPE"

mkdir -p "$DOWNLOAD_DIR"
out_file="$DOWNLOAD_DIR/$(basename "${url%%\?*}")"

set +e
fetch_url "$url" "$out_file"
fetch_rc=$?
set -e
if [ "$fetch_rc" -ne 0 ]; then
  log_event "download" "fail" "$url" "Failed to fetch artifact (code=$fetch_rc)"
  fail "failed to fetch artifact from $url (code=$fetch_rc)"
fi

log_event "download" "ok" "$out_file" "Artifact downloaded"

actual_sha="$(sha256sum "$out_file" | awk '{print $1}')"
if [ "$actual_sha" != "$sha" ]; then
  log_event "verify_hash" "fail" "$out_file" "SHA mismatch expected=$sha actual=$actual_sha"
  fail "sha256 mismatch for downloaded artifact"
fi
log_event "verify_hash" "ok" "$out_file" "SHA verified"

if [ -z "$signature_ref" ]; then
  log_event "verify_signature" "fail" "$out_file" "Signature reference is empty"
  fail "manifest signature reference is empty"
fi

if [ "$signature_ref" = "UNSIGNED" ]; then
  if [ "$FAIL_ON_UNSIGNED" -eq 1 ] || [ "$REQUIRE_SIGNATURE" -eq 1 ]; then
    log_event "verify_signature" "fail" "$out_file" "Artifact marked as UNSIGNED"
    fail "artifact is UNSIGNED but strict signature policy is enabled"
  fi
  log_event "verify_signature" "skipped" "$out_file" "Artifact marked as UNSIGNED"
else
  if ! is_abs_url "$signature_ref"; then
    log_event "verify_signature" "fail" "$out_file" "Signature URL must be absolute: $signature_ref"
    fail "signature must be absolute URL or UNSIGNED"
  fi

  sig_file="$DOWNLOAD_DIR/$(basename "${signature_ref%%\?*}")"
  set +e
  fetch_url "$signature_ref" "$sig_file"
  sig_fetch_rc=$?
  set -e
  if [ "$sig_fetch_rc" -ne 0 ]; then
    log_event "verify_signature" "fail" "$signature_ref" "Failed to fetch signature (code=$sig_fetch_rc)"
    fail "failed to fetch signature file"
  fi

  if [ "$REQUIRE_SIGNATURE" -eq 1 ] && [ -z "$PUBLIC_KEY" ]; then
    log_event "verify_signature" "fail" "$sig_file" "--require-signature requires --public-key"
    fail "--require-signature requires --public-key"
  fi

  if [ -n "$PUBLIC_KEY" ]; then
    command -v openssl >/dev/null 2>&1 || fail "openssl not found"

    sig_bin="$sig_file.bin"
    if base64 -d "$sig_file" > "$sig_bin" 2>/dev/null; then
      :
    else
      cp "$sig_file" "$sig_bin"
    fi

    if openssl dgst -sha256 -verify "$PUBLIC_KEY" -signature "$sig_bin" "$out_file" >/dev/null 2>&1; then
      log_event "verify_signature" "ok" "$out_file" "Signature verified with public key"
    else
      log_event "verify_signature" "fail" "$out_file" "Signature verification failed"
      fail "signature verification failed"
    fi
  else
    log_event "verify_signature" "skipped" "$out_file" "Signature file present but no public key provided"
  fi
fi

echo "Update package verified: $out_file"

if [ "$APPLY" -eq 1 ]; then
  log_event "apply" "blocked" "$out_file" "Apply not automated in this script; use platform-specific installer commands"
  echo "Apply step intentionally blocked here; execute platform-specific install command manually."
fi

echo "Log: $log_jsonl"
