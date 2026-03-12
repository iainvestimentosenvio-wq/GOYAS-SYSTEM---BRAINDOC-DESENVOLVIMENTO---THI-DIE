#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"

PRIVATE_KEY=""
PUBLIC_KEY_OUT=""
SIGNATURES_DIR="$PROJECT_ROOT/INSTALADOR/saida/signatures"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

usage() {
  cat <<USAGE
Usage: $0 --private-key <pem> [options]
  --private-key <pem>     PEM private key used to sign artifacts
  --public-key-out <pem>  Optional output path for the derived public key
  --signatures-dir <dir>  Output directory for *.sig files
USAGE
}

while [ $# -gt 0 ]; do
  case "$1" in
    --private-key)
      [ $# -ge 2 ] || fail "--private-key requires a value"
      PRIVATE_KEY="$2"
      shift 2
      ;;
    --public-key-out)
      [ $# -ge 2 ] || fail "--public-key-out requires a value"
      PUBLIC_KEY_OUT="$2"
      shift 2
      ;;
    --signatures-dir)
      [ $# -ge 2 ] || fail "--signatures-dir requires a value"
      SIGNATURES_DIR="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
done

[ -n "$PRIVATE_KEY" ] || fail "--private-key is required"
[ -f "$PRIVATE_KEY" ] || fail "private key not found: $PRIVATE_KEY"
[ -f "$VERSION_FILE" ] || fail "version file not found: $VERSION_FILE"

require_cmd openssl
require_cmd base64
require_cmd mkdir
require_cmd grep
require_cmd cut

VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | head -n1 | cut -d= -f2 | tr -d '"')"
[ -n "$VERSION" ] || fail "VERSION missing in $VERSION_FILE"

MSI_PATH="$PROJECT_ROOT/INSTALADOR/saida/windows/Protons-${VERSION}-x64.msi"
EXE_PATH="$PROJECT_ROOT/INSTALADOR/saida/windows/ProtonsSetup-${VERSION}.exe"
DEB_PATH="$PROJECT_ROOT/INSTALADOR/saida/deb/protons_${VERSION}_amd64.deb"
APPIMAGE_PATH="$PROJECT_ROOT/INSTALADOR/saida/appimage/Protons-${VERSION}-x86_64.AppImage"

mkdir -p "$SIGNATURES_DIR/windows" "$SIGNATURES_DIR/deb" "$SIGNATURES_DIR/appimage"

sign_one() {
  local artifact_path="$1"
  local signature_path="$2"
  local raw_path="${signature_path}.raw"

  if [ ! -f "$artifact_path" ]; then
    echo "SKIP: artifact not found: $artifact_path"
    return 0
  fi

  openssl dgst -sha256 -sign "$PRIVATE_KEY" -out "$raw_path" "$artifact_path"
  base64 < "$raw_path" | tr -d '\n' > "$signature_path"
  echo >> "$signature_path"
  rm -f "$raw_path"
  echo "SIGNED: $artifact_path -> $signature_path"
  return 0
}

SIGNED_COUNT=0

if sign_one "$MSI_PATH" "$SIGNATURES_DIR/windows/$(basename "$MSI_PATH").sig"; then
  [ -f "$MSI_PATH" ] && SIGNED_COUNT=$((SIGNED_COUNT + 1))
fi
if sign_one "$EXE_PATH" "$SIGNATURES_DIR/windows/$(basename "$EXE_PATH").sig"; then
  [ -f "$EXE_PATH" ] && SIGNED_COUNT=$((SIGNED_COUNT + 1))
fi
if sign_one "$DEB_PATH" "$SIGNATURES_DIR/deb/$(basename "$DEB_PATH").sig"; then
  [ -f "$DEB_PATH" ] && SIGNED_COUNT=$((SIGNED_COUNT + 1))
fi
if sign_one "$APPIMAGE_PATH" "$SIGNATURES_DIR/appimage/$(basename "$APPIMAGE_PATH").sig"; then
  [ -f "$APPIMAGE_PATH" ] && SIGNED_COUNT=$((SIGNED_COUNT + 1))
fi

if [ "$SIGNED_COUNT" -le 0 ]; then
  fail "no artifacts were signed"
fi

if [ -n "$PUBLIC_KEY_OUT" ]; then
  mkdir -p "$(dirname "$PUBLIC_KEY_OUT")"
  openssl pkey -in "$PRIVATE_KEY" -pubout -out "$PUBLIC_KEY_OUT"
  echo "PUBLIC_KEY: $PUBLIC_KEY_OUT"
fi

echo "SIGNATURES_DIR: $SIGNATURES_DIR"
echo "SIGNED_COUNT: $SIGNED_COUNT"
