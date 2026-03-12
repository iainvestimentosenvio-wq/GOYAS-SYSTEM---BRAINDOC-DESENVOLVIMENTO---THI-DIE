#!/bin/bash
# FINALIZAÇÃO INSTALADORES PROTONS
# Execute este script para criar os instaladores Linux completos

set -e  # Parar em caso de erro

echo "╔══════════════════════════════════════════════════════════════════════════════╗"
echo "║              FINALIZAÇÃO INSTALADORES PROTONS - LINUX                        ║"
echo "║                                                                              ║"
echo "║  Diretório de dados (novo padrão):                                          ║"
echo "║  • Linux: ~/.local/share/Protons                                            ║"
echo "║  • Windows: %AppData%\\Protons                                               ║"
echo "╚══════════════════════════════════════════════════════════════════════════════╝"
echo ""

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PROJECT_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
cd "$PROJECT_ROOT"

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 1: Instalando ImageMagick"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if ! command -v convert &> /dev/null; then
    echo "ImageMagick não instalado. Instalando..."
    sudo apt install imagemagick -y
    echo "✅ ImageMagick instalado"
else
    echo "✅ ImageMagick já instalado"
fi
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 2: Gerando ícones PNG (símbolo grande e nítido)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/ativos/icons/generate-icons-pro.sh --style simbolo --logo-size 248
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 3: Gerando ícone .ico para Windows"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/ativos/icons/generate-ico.sh
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 4: Rebuild aplicação (código atualizado)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Compilando solução completa..."
dotnet build Login/Protons.sln -c Release --no-incremental
echo "✅ Build concluído"
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 5: Build DEB com ícones"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/linux/deb/build-deb.sh
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 6: Instalando DEB"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Instalando pacote DEB..."
sudo dpkg -i INSTALADOR/saida/deb/protons_1.0.0_amd64.deb
echo "✅ DEB instalado"
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 7: Verificando instalação"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Verificando comandos e arquivos..."
which protons
ls -lh /opt/protons/Protons.UI
ls -lh /usr/share/applications/protons-login.desktop
echo ""
echo "⚠️  TESTE MANUAL NECESSÁRIO:"
echo "   1. Abrir menu de aplicações"
echo "   2. Buscar 'Protons'"
echo "   3. Verificar ícone correto"
echo "   4. Lançar app"
echo "   5. Verificar criação de: ~/.local/share/Protons/"
echo ""
read -p "Pressione ENTER após testar o app..."
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 8: Validando diretório de dados"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
EXPECTED="$HOME/.local/share/Protons"
if [ -d "$EXPECTED" ]; then
    echo "✅ Diretório de dados criado corretamente: $EXPECTED"
    ls -la "$EXPECTED"
else
    echo "❌ ERRO: Diretório não foi criado em $EXPECTED"
    echo "   Verifique se o app foi executado"
fi
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 9: Instalando appimagetool"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if ! command -v appimagetool &> /dev/null; then
    echo "Baixando appimagetool..."
    wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x appimagetool-x86_64.AppImage
    sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
    echo "✅ appimagetool instalado"
else
    echo "✅ appimagetool já instalado"
fi
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 10: Gerando AppImage"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
bash INSTALADOR/linux/appimage/build-appimage.sh
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "PASSO 11: Testando AppImage"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
chmod +x INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage
echo "⚠️  Teste MANUAL:"
echo "   Execute: ./INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage"
echo "   Validar que usa o mesmo diretório: ~/.local/share/Protons/"
echo ""
read -p "Pressione ENTER após testar o AppImage..."
echo ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo "╔══════════════════════════════════════════════════════════════════════════════╗"
echo "║                        ✅ LINUX FINALIZADO!                                  ║"
echo "╚══════════════════════════════════════════════════════════════════════════════╝"
echo ""
echo "📦 ARQUIVOS GERADOS:"
echo "   ✅ INSTALADOR/saida/deb/protons_1.0.0_amd64.deb"
echo "   ✅ INSTALADOR/saida/appimage/Protons-1.0.0-x86_64.AppImage"
echo "   ✅ INSTALADOR/windows/ativos/logo_protons.ico"
echo ""
echo "📁 DIRETÓRIO DE DADOS:"
echo "   ✅ ~/.local/share/Protons/ (Linux padrão XDG)"
echo ""
echo "📸 PRÓXIMO PASSO:"
echo "   Tirar screenshots e salvar em INSTALADOR/evidencias/linux/"
echo "   • Menu de aplicações mostrando Protons com ícone"
echo "   • App rodando"
echo "   • Diretório ~/.local/share/Protons/"
echo ""
echo "🪟 WINDOWS:"
echo "   Siga o guia: INSTALADOR/GUIA_FINALIZACAO.md"
echo "   Arquivos WiX atualizados e prontos para build"
echo ""
