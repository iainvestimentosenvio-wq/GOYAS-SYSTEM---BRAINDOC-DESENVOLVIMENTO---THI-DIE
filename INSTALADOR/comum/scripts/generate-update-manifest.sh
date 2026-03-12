#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"

CHANNEL="stable"
MANIFEST_OUT=""
BASE_URL=""
RELEASE_NOTES_URL=""
MIN_SUPPORTED_VERSION=""
ROLLOUT_PERCENT="100"
SIGNATURE="UNSIGNED"
SIGNATURE_BASE_URL=""

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

is_abs_url() {
  [[ "$1" =~ ^(https://|file://).+ ]]
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --channel)
      [ $# -ge 2 ] || fail "--channel requires a value"
      CHANNEL="$2"
      shift 2
      ;;
    --output)
      [ $# -ge 2 ] || fail "--output requires a value"
      MANIFEST_OUT="$2"
      shift 2
      ;;
    --base-url)
      [ $# -ge 2 ] || fail "--base-url requires a value"
      BASE_URL="$2"
      shift 2
      ;;
    --release-notes-url)
      [ $# -ge 2 ] || fail "--release-notes-url requires a value"
      RELEASE_NOTES_URL="$2"
      shift 2
      ;;
    --min-supported-version)
      [ $# -ge 2 ] || fail "--min-supported-version requires a value"
      MIN_SUPPORTED_VERSION="$2"
      shift 2
      ;;
    --rollout-percent)
      [ $# -ge 2 ] || fail "--rollout-percent requires a value"
      ROLLOUT_PERCENT="$2"
      shift 2
      ;;
    --signature)
      [ $# -ge 2 ] || fail "--signature requires a value"
      SIGNATURE="$2"
      shift 2
      ;;
    --signature-base-url)
      [ $# -ge 2 ] || fail "--signature-base-url requires a value"
      SIGNATURE_BASE_URL="$2"
      shift 2
      ;;
    --help|-h)
      cat <<USAGE
Usage: $0 --base-url <https://...|file://...> [options]
  --channel <stable|beta>
  --rollout-percent <0..100>
  --signature <UNSIGNED|absolute-url>
  --signature-base-url <absolute-url>   # if set, overrides --signature as <base>/<relative-artifact-path>.sig
USAGE
      exit 0
      ;;
    *) fail "unknown argument: $1" ;;
  esac
done

[ -n "$BASE_URL" ] || fail "--base-url is required"
is_abs_url "$BASE_URL" || fail "--base-url must be absolute (https:// or file://)"
BASE_URL="${BASE_URL%/}"

case "$CHANNEL" in
  stable|beta) ;;
  *) fail "invalid --channel '$CHANNEL' (expected stable|beta)" ;;
esac

[[ "$ROLLOUT_PERCENT" =~ ^[0-9]+$ ]] || fail "--rollout-percent must be integer 0..100"
if [ "$ROLLOUT_PERCENT" -lt 0 ] || [ "$ROLLOUT_PERCENT" -gt 100 ]; then
  fail "--rollout-percent out of range: $ROLLOUT_PERCENT"
fi

if [ -n "$SIGNATURE_BASE_URL" ]; then
  is_abs_url "$SIGNATURE_BASE_URL" || fail "--signature-base-url must be absolute"
  SIGNATURE_BASE_URL="${SIGNATURE_BASE_URL%/}"
else
  if [ "$SIGNATURE" != "UNSIGNED" ]; then
    is_abs_url "$SIGNATURE" || fail "--signature must be UNSIGNED or absolute URL"
  fi
fi

[ -f "$VERSION_FILE" ] || fail "version file not found: $VERSION_FILE"

require_cmd jq
require_cmd sha256sum
require_cmd stat
require_cmd grep
require_cmd date

VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | head -n1 | cut -d= -f2 | tr -d '"')"
[ -n "$VERSION" ] || fail "VERSION missing in $VERSION_FILE"

if [ -z "$MIN_SUPPORTED_VERSION" ]; then
  MIN_SUPPORTED_VERSION="$VERSION"
fi

if [ -z "$RELEASE_NOTES_URL" ]; then
  RELEASE_NOTES_URL="$BASE_URL/release-notes/v$VERSION"
fi
is_abs_url "$RELEASE_NOTES_URL" || fail "release notes URL must be absolute"

OUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/update"
mkdir -p "$OUT_DIR"

if [ -z "$MANIFEST_OUT" ]; then
  MANIFEST_OUT="$OUT_DIR/update-manifest.json"
fi

TMP_ARTIFACTS="$(mktemp)"
trap 'rm -f "$TMP_ARTIFACTS"' EXIT

add_artifact() {
  local platform="$1"
  local type="$2"
  local path="$3"
  local relative_path="$4"

  if [ ! -f "$path" ]; then
    return
  fi

  local sha size signature_value url
  sha="$(sha256sum "$path" | awk '{print $1}')"
  size="$(stat -c '%s' "$path")"
  url="$BASE_URL/$relative_path"

  if [ -n "$SIGNATURE_BASE_URL" ]; then
    signature_value="$SIGNATURE_BASE_URL/$relative_path.sig"
  else
    signature_value="$SIGNATURE"
  fi

  jq -nc \
    --arg platform "$platform" \
    --arg type "$type" \
    --arg url "$url" \
    --arg sha256 "$sha" \
    --arg signature "$signature_value" \
    --argjson size_bytes "$size" \
    '{platform:$platform,type:$type,url:$url,sha256:$sha256,size_bytes:$size_bytes,signature:$signature}' >> "$TMP_ARTIFACTS"
}

add_artifact "windows" "msi" "$PROJECT_ROOT/INSTALADOR/saida/windows/Protons-${VERSION}-x64.msi" "windows/Protons-${VERSION}-x64.msi"
add_artifact "windows" "exe" "$PROJECT_ROOT/INSTALADOR/saida/windows/ProtonsSetup-${VERSION}.exe" "windows/ProtonsSetup-${VERSION}.exe"
add_artifact "linux" "deb" "$PROJECT_ROOT/INSTALADOR/saida/deb/protons_${VERSION}_amd64.deb" "deb/protons_${VERSION}_amd64.deb"
add_artifact "linux" "appimage" "$PROJECT_ROOT/INSTALADOR/saida/appimage/Protons-${VERSION}-x86_64.AppImage" "appimage/Protons-${VERSION}-x86_64.AppImage"

artifacts_count="$(wc -l < "$TMP_ARTIFACTS" | tr -d ' ')"
if [ "$artifacts_count" -eq 0 ]; then
  fail "no artifacts found to include in manifest"
fi

artifacts_json="$(jq -s '.' "$TMP_ARTIFACTS")"
published_at_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

jq -n \
  --arg version "$VERSION" \
  --arg channel "$CHANNEL" \
  --arg published_at_utc "$published_at_utc" \
  --arg min_supported_version "$MIN_SUPPORTED_VERSION" \
  --arg release_notes_url "$RELEASE_NOTES_URL" \
  --argjson rollout_percent "$ROLLOUT_PERCENT" \
  --argjson artifacts "$artifacts_json" \
  '{
    version: $version,
    channel: $channel,
    published_at_utc: $published_at_utc,
    min_supported_version: $min_supported_version,
    artifacts: $artifacts,
    release_notes_url: $release_notes_url,
    rollout_percent: $rollout_percent
  }' > "$MANIFEST_OUT"

echo "Manifest generated: $MANIFEST_OUT"
