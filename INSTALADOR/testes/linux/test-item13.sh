#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"

# Versao
if [ -f "$VERSION_FILE" ]; then
  VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
else
  VERSION="1.0.0"
fi

DEB_NEW="$PROJECT_ROOT/INSTALADOR/saida/deb/protons_${VERSION}_amd64.deb"
DEB_OLD="$PROJECT_ROOT/INSTALADOR/saida/te06/protons_0.9.9_amd64.deb"
APPIMAGE="$PROJECT_ROOT/INSTALADOR/saida/appimage/Protons-${VERSION}-x86_64.AppImage"

TS="$(date -u +"%Y%m%dT%H%M%SZ")"
OUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/test-logs/$TS/item13"
mkdir -p "$OUT_DIR"
LOG="$OUT_DIR/item13.log"
exec > >(tee -a "$LOG") 2>&1

echo "=== Item 13 - Desinstalacao e Rollback (Linux) ==="
echo "Versao: $VERSION"
echo "Log: $LOG"
echo ""

# Verificar arquivos necessarios
[ -f "$DEB_NEW" ] || { echo "❌ DEB nao encontrado: $DEB_NEW"; exit 1; }
[ -f "$APPIMAGE" ] || { echo "❌ AppImage nao encontrado: $APPIMAGE"; exit 1; }

SUDO_CMD=""
if [ "$EUID" -eq 0 ]; then
  SUDO_CMD=""
elif sudo -n true 2>/dev/null; then
  SUDO_CMD="sudo -n"
else
  echo "⚠️  SKIP: testes com DEB requerem sudo (rode 'sudo -v' em terminal interativo e execute novamente)"
  exit 0
fi

DEB_OK=0
PURGE_OK=0
APP_OK=0
REINSTALL_OK=0
ROLLBACK_OK=0

run_app_with_timeout() {
  local seconds="$1"
  shift
  set +e
  timeout "$seconds" "$@" &>/dev/null
  local rc=$?
  set -e
  if [ "$rc" -eq 0 ] || [ "$rc" -eq 124 ] || [ "$rc" -eq 143 ]; then
    return 0
  fi
  echo "❌ Execucao retornou codigo inesperado: $rc ($*)"
  return 1
}

# DR-01 DEB
TEST_HOME_DEB="$(mktemp -d)"
DATA_DIR_DEB="$TEST_HOME_DEB/.local/share/Protons"
SENTINEL_DEB="$DATA_DIR_DEB/dr01_sentinel.txt"
echo "DR-01 DEB: HOME isolado: $TEST_HOME_DEB"

${SUDO_CMD} dpkg -i "$DEB_NEW"
HOME="$TEST_HOME_DEB" run_app_with_timeout 5 /usr/bin/protons
mkdir -p "$DATA_DIR_DEB"
echo "dr01-sentinel" > "$SENTINEL_DEB"

${SUDO_CMD} dpkg -r protons

if [ ! -e /opt/protons ] && [ ! -e /usr/bin/protons ] && \
   [ ! -e /usr/share/applications/protons-login.desktop ] && \
   [ -f "$SENTINEL_DEB" ]; then
  DEB_OK=1
fi

${SUDO_CMD} dpkg -P protons || true
if [ -f "$SENTINEL_DEB" ]; then
  PURGE_OK=1
fi

# DR-01 AppImage
TEST_HOME_APP="$(mktemp -d)"
DATA_DIR_APP="$TEST_HOME_APP/.local/share/Protons"
echo "DR-01 AppImage: HOME isolado: $TEST_HOME_APP"
HOME="$TEST_HOME_APP" run_app_with_timeout 5 "$APPIMAGE"

APPIMAGE_BAK="$(mktemp)"
cp -f "$APPIMAGE" "$APPIMAGE_BAK"
rm -f "$APPIMAGE"
if [ -d "$DATA_DIR_APP" ]; then
  APP_OK=1
fi
mv -f "$APPIMAGE_BAK" "$APPIMAGE"
chmod +x "$APPIMAGE"

# DR-02 Reinstall mesma versao
${SUDO_CMD} dpkg -i "$DEB_NEW"
HOME="$TEST_HOME_DEB" run_app_with_timeout 5 /usr/bin/protons
if [ -f "$SENTINEL_DEB" ]; then
  REINSTALL_OK=1
fi

# DR-02 Rollback
if [ -f "$DEB_OLD" ]; then
  ${SUDO_CMD} dpkg -i "$DEB_OLD"
  HOME="$TEST_HOME_DEB" run_app_with_timeout 5 /usr/bin/protons
  ${SUDO_CMD} dpkg -i "$DEB_NEW"
  ${SUDO_CMD} dpkg -i "$DEB_OLD"
  if [ -f "$SENTINEL_DEB" ]; then
    ROLLBACK_OK=1
  fi
else
  echo "⚠️  Pacote antigo nao encontrado: $DEB_OLD (rollback SKIP)"
fi

# Cleanup
${SUDO_CMD} dpkg -r protons || true
${SUDO_CMD} dpkg -P protons || true

# Evidencias
EVID_LINE="$(bash "$PROJECT_ROOT/INSTALADOR/comum/scripts/collect-test-evidence.sh" | rg -m1 'Evidencias em:' || true)"
echo ""
echo "RESULTADOS:"
echo "DEB_UNINSTALL_OK=$DEB_OK"
echo "DEB_PURGE_DATA_OK=$PURGE_OK"
echo "APPIMAGE_DATA_OK=$APP_OK"
echo "REINSTALL_OK=$REINSTALL_OK"
echo "ROLLBACK_OK=$ROLLBACK_OK"
echo "$EVID_LINE"

echo ""
echo "Obs:"
echo "- TEST_HOME_DEB: $TEST_HOME_DEB"
echo "- TEST_HOME_APP: $TEST_HOME_APP"
