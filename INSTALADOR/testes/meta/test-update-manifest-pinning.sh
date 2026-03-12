#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
CHECK_UPDATE="$INSTALADOR_ROOT/comum/scripts/check-update.sh"
VERSION_FILE="$INSTALADOR_ROOT/comum/version.env"

command -v jq >/dev/null 2>&1 || { echo "FAIL: jq nao encontrado" >&2; exit 1; }
command -v openssl >/dev/null 2>&1 || { echo "FAIL: openssl nao encontrado" >&2; exit 1; }
command -v sha256sum >/dev/null 2>&1 || { echo "FAIL: sha256sum nao encontrado" >&2; exit 1; }

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

CURRENT_VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | head -n1 | cut -d= -f2 | tr -d '"')"

assert_fails() {
  local desc="$1"
  shift
  if "$@" >/dev/null 2>&1; then
    echo "FAIL: $desc (esperado FAIL)" >&2
    exit 1
  fi
  echo "PASS: $desc"
}

ARTIFACT="$TMP_DIR/artifact.bin"
printf 'update-pinning-test-%s\n' "$(date -u +%s)" > "$ARTIFACT"
ART_SHA="$(sha256sum "$ARTIFACT" | awk '{print $1}')"
ART_SIZE="$(stat -c '%s' "$ARTIFACT")"

PRIV_A="$TMP_DIR/private-a.pem"
PUB_A="$TMP_DIR/public-a.pem"
PRIV_B="$TMP_DIR/private-b.pem"
PUB_B="$TMP_DIR/public-b.pem"

openssl genpkey -algorithm RSA -out "$PRIV_A" -pkeyopt rsa_keygen_bits:2048 >/dev/null 2>&1
openssl pkey -in "$PRIV_A" -pubout -out "$PUB_A" >/dev/null 2>&1
openssl genpkey -algorithm RSA -out "$PRIV_B" -pkeyopt rsa_keygen_bits:2048 >/dev/null 2>&1
openssl pkey -in "$PRIV_B" -pubout -out "$PUB_B" >/dev/null 2>&1

SIG_RAW="$TMP_DIR/artifact.sig.raw"
SIG_B64="$TMP_DIR/artifact.sig"
openssl dgst -sha256 -sign "$PRIV_A" -out "$SIG_RAW" "$ARTIFACT"
base64 < "$SIG_RAW" | tr -d '\n' > "$SIG_B64"

NOW_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
MANIFEST_VALID="$TMP_DIR/manifest-valid.json"

jq -n \
  --arg version "9.9.9" \
  --arg channel "stable" \
  --arg published_at_utc "$NOW_UTC" \
  --arg min_supported_version "$CURRENT_VERSION" \
  --arg release_notes_url "https://example.com/release-notes/9.9.9" \
  --arg artifact_url "file://$ARTIFACT" \
  --arg artifact_sha "$ART_SHA" \
  --arg artifact_sig "file://$SIG_B64" \
  --argjson artifact_size "$ART_SIZE" \
  '{
    version: $version,
    channel: $channel,
    published_at_utc: $published_at_utc,
    min_supported_version: $min_supported_version,
    release_notes_url: $release_notes_url,
    rollout_percent: 100,
    artifacts: [
      {
        platform: "linux",
        type: "appimage",
        url: $artifact_url,
        sha256: $artifact_sha,
        size_bytes: $artifact_size,
        signature: $artifact_sig
      }
    ]
  }' > "$MANIFEST_VALID"

# Baseline positivo para garantir fixture correta.
"$CHECK_UPDATE" \
  --manifest "$MANIFEST_VALID" \
  --platform linux \
  --type appimage \
  --require-signature \
  --public-key "$PUB_A" \
  --download-dir "$TMP_DIR/download-ok" >/dev/null
echo "PASS: baseline assinatura valida"

# 1) Chave publica divergente -> FAIL.
assert_fails \
  "chave publica divergente rejeitada" \
  "$CHECK_UPDATE" \
  --manifest "$MANIFEST_VALID" \
  --platform linux \
  --type appimage \
  --require-signature \
  --public-key "$PUB_B" \
  --download-dir "$TMP_DIR/download-badkey"

# 2) Modo estrito sem chave publica -> FAIL.
assert_fails \
  "modo estrito sem public key rejeitado" \
  "$CHECK_UPDATE" \
  --manifest "$MANIFEST_VALID" \
  --platform linux \
  --type appimage \
  --require-signature \
  --download-dir "$TMP_DIR/download-nokey"

# 3) Assinatura invalida -> FAIL.
SIG_BAD="$TMP_DIR/artifact-bad.sig"
printf 'not-a-valid-signature' > "$SIG_BAD"
MANIFEST_BAD_SIG="$TMP_DIR/manifest-bad-sig.json"
jq --arg sig "file://$SIG_BAD" '.artifacts[0].signature = $sig' "$MANIFEST_VALID" > "$MANIFEST_BAD_SIG"

assert_fails \
  "assinatura invalida rejeitada" \
  "$CHECK_UPDATE" \
  --manifest "$MANIFEST_BAD_SIG" \
  --platform linux \
  --type appimage \
  --require-signature \
  --public-key "$PUB_A" \
  --download-dir "$TMP_DIR/download-badsig"

# 4) Versao regressiva -> nao atualiza.
MANIFEST_OLD="$TMP_DIR/manifest-old-version.json"
jq --arg v "0.0.1" '.version = $v' "$MANIFEST_VALID" > "$MANIFEST_OLD"

output="$("$CHECK_UPDATE" \
  --manifest "$MANIFEST_OLD" \
  --platform linux \
  --type appimage \
  --require-signature \
  --public-key "$PUB_A" \
  --download-dir "$TMP_DIR/download-old" 2>&1)"

echo "$output" | rg -q "No update required" || {
  echo "FAIL: versao regressiva deveria retornar sem update" >&2
  echo "$output" >&2
  exit 1
}
echo "PASS: versao regressiva nao atualiza"

echo "PASS: cenarios de pinning/signature validados"
