#!/bin/bash
# Build completo dos instaladores Linux (DEB + AppImage)
# Execute a partir da raiz do projeto: bash INSTALADOR/build-linux-completo.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$PROJECT_ROOT"

echo "Build instaladores Linux - DEB e AppImage"
echo ""

# 1. ImageMagick
if ! command -v convert &>/dev/null; then
    echo "Instalando ImageMagick..."
    sudo apt install imagemagick -y
fi

# 2. Ícones PNG
bash INSTALADOR/ativos/icons/generate-icons-pro.sh --style simbolo --logo-size 248

# 3. Ícone .ico Windows
bash INSTALADOR/ativos/icons/generate-ico.sh

# 4. Build Release
dotnet build Login/Protons.sln -c Release --no-incremental

# 5. DEB
bash INSTALADOR/linux/deb/build-deb.sh

# 6. Instalar DEB (opcional)
echo "Instalar DEB? (s/N)"
read -r r
if [[ "$r" =~ ^[sS] ]]; then
    sudo dpkg -i INSTALADOR/saida/deb/protons_1.0.0_amd64.deb
fi

# 7. appimagetool
if ! command -v appimagetool &>/dev/null; then
    wget -q https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x appimagetool-x86_64.AppImage
    sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
fi

# 8. AppImage
bash INSTALADOR/linux/appimage/build-appimage.sh

echo ""
echo "Arquivos gerados:"
echo "  - INSTALADOR/saida/deb/protons_1.0.0_amd64.deb"
echo "  - INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage"
echo "  - INSTALADOR/windows/ativos/logo_protons.ico"
echo ""
echo "Windows: ver INSTALADOR/CHECKLIST.md e documentos em INSTALADOR/documentos/"
