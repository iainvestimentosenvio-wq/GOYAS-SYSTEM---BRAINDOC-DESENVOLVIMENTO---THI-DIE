#!/usr/bin/env bash
# Corrige o atalho (.desktop) no Linux quando o caminho do projeto tem espaço (ex: PROJETO PROTONS).
# O Exec e o Icon precisam estar entre aspas para o launcher interpretar corretamente.
# Uso: a partir da raiz do projeto Login: ./scripts/fix-atalho-linux.sh

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROTONS_ROOT="$(cd "$PROJECT_ROOT/.." && pwd)"
PUBLISH_EXE="$PROJECT_ROOT/Protons.UI/bin/Release/net8.0/linux-x64/publish/Protons.UI"
ICON_SOURCE_DIR="$PROTONS_ROOT/INSTALADOR/ativos/icons/png"
DESKTOP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
DESKTOP_FILE="$DESKTOP_DIR/protons-login.desktop"
ICON_NAME="protons"
SYSTEM_MODE=false
FORCE_USER=false

if [ "${1:-}" = "--system" ]; then
  SYSTEM_MODE=true
fi
if [ "${1:-}" = "--user" ]; then
  FORCE_USER=true
fi

if [ ! -f "$PUBLISH_EXE" ]; then
  echo "❌ Publicação não encontrada. Execute antes:"
  echo "  dotnet publish Protons.UI/Protons.UI.csproj -c Release -r linux-x64 --self-contained true"
  exit 1
fi

if [ ! -f "$ICON_SOURCE_DIR/256x256.png" ]; then
  echo "❌ Ícone não encontrado em: $ICON_SOURCE_DIR/256x256.png"
  echo "Verifique se o diretório INSTALADOR/ativos/icons/png existe e gere os ícones."
  exit 1
fi

if [ "$SYSTEM_MODE" = true ]; then
  if [ "${EUID:-$(id -u)}" != "0" ]; then
    echo "❌ --system requer sudo (root)."
    exit 1
  fi
  ICON_TARGET_ROOT="/usr/share/icons/hicolor"
else
  if [ -f "/usr/share/applications/protons-login.desktop" ] && [ "$FORCE_USER" = false ]; then
    echo "⚠️  Atalho do sistema já existe em /usr/share/applications/protons-login.desktop"
    echo "✅ Nenhuma ação necessária para o usuário final."
    echo "Se realmente quiser um atalho por usuário, execute com --user."
    exit 0
  fi
  ICON_TARGET_ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
fi

for SIZE in 16 32 48 64 128 256; do
  TARGET_DIR="$ICON_TARGET_ROOT/${SIZE}x${SIZE}/apps"
  mkdir -p "$TARGET_DIR"
  cp "$ICON_SOURCE_DIR/${SIZE}x${SIZE}.png" "$TARGET_DIR/$ICON_NAME.png"
done

mkdir -p "$DESKTOP_DIR"
cat > "$DESKTOP_FILE" << DESKTOP
[Desktop Entry]
Name=Protons - Login
Comment=Sistema de Login local
Exec=/usr/bin/protons
Icon=$ICON_NAME
Terminal=false
Type=Application
Categories=Office;Utility;
StartupNotify=true
StartupWMClass=Protons.UI
DESKTOP

chmod +x "$DESKTOP_FILE"
update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t -f "$ICON_TARGET_ROOT" 2>/dev/null || true
fi
echo "✅ Atalho atualizado: $DESKTOP_FILE"
echo "✅ Ícone instalado no tema: $ICON_NAME"
echo "✅ Caminhos entre aspas (suporta espaços no nome das pastas)"
echo ""
echo "Para testar: gtk-launch protons-login.desktop"
