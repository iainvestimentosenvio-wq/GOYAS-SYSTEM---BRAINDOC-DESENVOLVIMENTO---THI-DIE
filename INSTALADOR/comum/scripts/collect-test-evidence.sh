#!/bin/bash
# =============================================================================
# collect-test-evidence.sh - Coleta evidencias de testes (Linux)
# =============================================================================
# Gera logs e evidencias padronizadas para auditoria e revisao
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"

# Timestamp UTC
TS="$(date -u +"%Y%m%dT%H%M%SZ")"
OUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/test-logs/$TS"
mkdir -p "$OUT_DIR"
LOG="$OUT_DIR/collect.log"

log() {
  echo "[$(date -u +"%Y-%m-%dT%H:%M:%SZ")] $*" | tee -a "$LOG"
}

# Versao
if [ -f "$VERSION_FILE" ]; then
  VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
else
  VERSION="1.0.0"
fi

APPIMAGE="$PROJECT_ROOT/INSTALADOR/saida/appimage/Protons-${VERSION}-x86_64.AppImage"
DEB_FILE="$PROJECT_ROOT/INSTALADOR/saida/deb/protons_${VERSION}_amd64.deb"

log "=== Coleta de Evidencias - Protons (Linux) ==="
log "Versao: $VERSION"
log "Saida: $OUT_DIR"
log ""

# Ambiente
log "Coletando ambiente..."
{
  echo "uname:"; uname -a || true
  echo ""; echo "id:"; id || true
  echo ""; echo "groups:"; groups || true
  echo ""; echo "date_utc:"; date -u +"%Y-%m-%dT%H:%M:%SZ" || true
  if command -v lsb_release >/dev/null 2>&1; then
    echo ""; lsb_release -a || true
  fi
  if command -v dotnet >/dev/null 2>&1; then
    echo ""; dotnet --info || true
  fi
  if command -v appimagetool >/dev/null 2>&1; then
    echo ""; appimagetool --version || true
  fi
  if command -v dpkg-deb >/dev/null 2>&1; then
    echo ""; dpkg-deb --version || true
  fi
} > "$OUT_DIR/env.txt" 2>&1

# Artefatos
log "Coletando info de artefatos..."
{
  [ -f "$APPIMAGE" ] && ls -lh "$APPIMAGE" || echo "AppImage nao encontrado: $APPIMAGE"
  [ -f "$DEB_FILE" ] && ls -lh "$DEB_FILE" || echo "DEB nao encontrado: $DEB_FILE"
} > "$OUT_DIR/artifacts.txt" 2>&1

# Checksums
log "Gerando checksums (se artefatos existirem)..."
{
  if command -v sha256sum >/dev/null 2>&1; then
    [ -f "$APPIMAGE" ] && sha256sum "$APPIMAGE" || true
    [ -f "$DEB_FILE" ] && sha256sum "$DEB_FILE" || true
  else
    echo "sha256sum nao encontrado"
  fi
} > "$OUT_DIR/checksums.txt" 2>&1

# AppImage evidencia
if [ -f "$APPIMAGE" ]; then
  log "Extraindo AppImage para evidencias..."
  mkdir -p "$OUT_DIR/appimage"
  (cd "$OUT_DIR" && "$APPIMAGE" --appimage-extract >/dev/null 2>&1 || true)
  if [ -d "$OUT_DIR/squashfs-root" ]; then
    # Copiar arquivos relevantes
    cp "$OUT_DIR/squashfs-root/AppRun" "$OUT_DIR/appimage/AppRun" 2>/dev/null || true
    cp "$OUT_DIR/squashfs-root/protons-login.desktop" "$OUT_DIR/appimage/protons-login.desktop" 2>/dev/null || true
    cp "$OUT_DIR/squashfs-root/usr/share/applications/protons-login.desktop" "$OUT_DIR/appimage/protons-login.desktop.usr" 2>/dev/null || true
    cp "$OUT_DIR/squashfs-root/usr/share/icons/hicolor/256x256/apps/protons.png" "$OUT_DIR/appimage/protons.png" 2>/dev/null || true

    {
      echo "AppRun:"; grep -n "Protons.UI" "$OUT_DIR/squashfs-root/AppRun" || true
      echo ""; echo "Desktop (root):"; grep -n "^Exec=\|^Icon=\|^StartupWMClass=" "$OUT_DIR/squashfs-root/protons-login.desktop" || true
      echo ""; echo "Desktop (usr/share):"; grep -n "^Exec=\|^Icon=\|^StartupWMClass=" "$OUT_DIR/squashfs-root/usr/share/applications/protons-login.desktop" || true
      echo ""; echo "Icon exists:"; [ -f "$OUT_DIR/squashfs-root/usr/share/icons/hicolor/256x256/apps/protons.png" ] && echo "yes" || echo "no"
    } > "$OUT_DIR/appimage/appimage-checks.txt" 2>&1

    rm -rf "$OUT_DIR/squashfs-root"
  else
    echo "Falha ao extrair AppImage" > "$OUT_DIR/appimage/appimage-checks.txt"
  fi
fi

# DEB evidencia
if [ -f "$DEB_FILE" ] && command -v dpkg-deb >/dev/null 2>&1; then
  log "Coletando evidencias DEB..."
  mkdir -p "$OUT_DIR/deb"
  dpkg-deb --info "$DEB_FILE" > "$OUT_DIR/deb/deb-info.txt" 2>&1 || true
  dpkg-deb --contents "$DEB_FILE" > "$OUT_DIR/deb/deb-contents.txt" 2>&1 || true
  dpkg-deb -x "$DEB_FILE" "$OUT_DIR/deb/extract" >/dev/null 2>&1 || true
  if [ -d "$OUT_DIR/deb/extract" ]; then
    cp "$OUT_DIR/deb/extract/usr/share/applications/protons-login.desktop" "$OUT_DIR/deb/protons-login.desktop" 2>/dev/null || true
    cp "$OUT_DIR/deb/extract/usr/share/icons/hicolor/256x256/apps/protons.png" "$OUT_DIR/deb/protons.png" 2>/dev/null || true
    cp "$OUT_DIR/deb/extract/usr/share/pixmaps/protons.png" "$OUT_DIR/deb/protons-pixmaps.png" 2>/dev/null || true
    {
      echo "Desktop:"; grep -n "^Exec=\|^Icon=" "$OUT_DIR/deb/extract/usr/share/applications/protons-login.desktop" || true
      echo ""; echo "Symlink /usr/bin/protons (from package contents):"; grep -n "usr/bin/protons" "$OUT_DIR/deb/deb-contents.txt" || true
    } > "$OUT_DIR/deb/deb-checks.txt" 2>&1
  fi
fi

# build-info (se existir)
if [ -f "$PROJECT_ROOT/INSTALADOR/saida/metadata/build-info.txt" ]; then
  cp "$PROJECT_ROOT/INSTALADOR/saida/metadata/build-info.txt" "$OUT_DIR/build-info.txt" || true
fi

log "Coleta concluida. Evidencias em: $OUT_DIR"
