#!/bin/bash
# COMANDOS RÁPIDOS - Finalização Instaladores Protons
# Execute linha por linha ou rode todo o script

set -e  # Parar em caso de erro

echo "╔══════════════════════════════════════════════════════════════╗"
echo "║     FINALIZAÇÃO INSTALADORES PROTONS - COMANDOS RÁPIDOS      ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""

# Navegar para o diretorio correto (raiz do projeto)
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PROJECT_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
cd "$PROJECT_ROOT"

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 1: Instalando ImageMagick"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
sudo apt install imagemagick -y
echo "✅ ImageMagick instalado"
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 2: Gerando ícones PNG"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/ativos/icons/generate-icons.sh
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 3: Gerando ícone .ico para Windows"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/ativos/icons/generate-ico.sh
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 4: Rebuild DEB com ícones"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/linux/deb/build-deb.sh
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 5: Instalando DEB"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
sudo dpkg -i INSTALADOR/saida/deb/protons_1.0.0_amd64.deb
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 6: Verificando instalação"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
which protons
ls -lh /opt/protons/Protons.UI
ls -lh /usr/share/applications/protons-login.desktop
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 7: Testando DEB"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "⚠️  Teste MANUAL necessário:"
echo "   1. Abrir menu de aplicações"
echo "   2. Buscar 'Protons'"
echo "   3. Verificar ícone"
echo "   4. Lançar app"
echo ""
read -p "Pressione ENTER após testar o app..."
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 8: Baixando appimagetool"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if ! command -v appimagetool &> /dev/null; then
    wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x appimagetool-x86_64.AppImage
    sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
    echo "✅ appimagetool instalado"
else
    echo "✅ appimagetool já está instalado"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 9: Gerando AppImage"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/linux/appimage/build-appimage.sh
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 10: Testando AppImage"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
chmod +x INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage
echo "⚠️  Teste MANUAL necessário:"
echo "   Execute: ./INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage"
echo ""
read -p "Pressione ENTER após testar o AppImage..."
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 11: Executando testes automatizados"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Testando DEB..."
bash INSTALADOR/testes/linux/test-deb.sh INSTALADOR/saida/deb/protons_1.0.0_amd64.deb
echo ""
echo "Testando AppImage..."
bash INSTALADOR/testes/linux/test-appimage.sh INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage
echo ""

echo "╔══════════════════════════════════════════════════════════════╗"
echo "║                   ✅ LINUX FINALIZADO!                        ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""
echo "📦 ARQUIVOS GERADOS:"
echo "   ✅ INSTALADOR/saida/deb/protons_1.0.0_amd64.deb"
echo "   ✅ INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage"
echo "   ✅ INSTALADOR/windows/ativos/logo_protons.ico"
echo ""
echo "📸 PRÓXIMO PASSO:"
echo "   Tirar screenshots e salvar em INSTALADOR/evidencias/linux/"
echo "   • Menu de aplicações mostrando Protons"
echo "   • App rodando"
echo "   • Diretório ~/.local/share/Protons"
echo ""
echo "🪟 WINDOWS:"
echo "   Siga o guia em: INSTALADOR/GUIA_FINALIZACAO.md"
echo "   Seção Windows (requer ambiente Windows)"
echo ""
