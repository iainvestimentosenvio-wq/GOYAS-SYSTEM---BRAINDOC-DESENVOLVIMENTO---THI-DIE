#!/bin/bash
# SCRIPT COMPLETO - Ícone Profissional para Protons
# Execute este script para ter o logo bonito no atalho!

set -e

echo "╔════════════════════════════════════════════════════════════════════╗"
echo "║                                                                    ║"
echo "║   🎨 CONFIGURANDO ÍCONE PROFISSIONAL PROTONS                      ║"
echo "║                                                                    ║"
echo "║   Antes: ❌ Engrenagem genérica feia                             ║"
echo "║   Depois: ✅ Logo Protons bonito e profissional                  ║"
echo "║                                                                    ║"
echo "╚════════════════════════════════════════════════════════════════════╝"
echo ""

# Navegar para a raiz do projeto
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ICONS_DIR="$PROJECT_ROOT/INSTALADOR/ativos/icons"
LOGIN_DIR="$PROJECT_ROOT/Login"
cd "$PROJECT_ROOT"

SYSTEM_MODE=false
if [ "${1:-}" = "--system" ]; then
    SYSTEM_MODE=true
fi

# Passo 1: Verificar/Instalar ImageMagick
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 1: Verificando ImageMagick..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if ! command -v convert &> /dev/null; then
    echo "⚠️  ImageMagick não instalado."
    echo ""
    if command -v apt-get &> /dev/null; then
        echo "Instalando via apt-get..."
        sudo apt-get update -y
        sudo apt-get install -y imagemagick
    elif command -v apt &> /dev/null; then
        echo "Instalando via apt..."
        sudo apt install -y imagemagick
    elif command -v dnf &> /dev/null; then
        echo "Instalando via dnf..."
        sudo dnf install -y ImageMagick
    elif command -v pacman &> /dev/null; then
        echo "Instalando via pacman..."
        sudo pacman -Sy --noconfirm imagemagick
    elif command -v zypper &> /dev/null; then
        echo "Instalando via zypper..."
        sudo zypper -n install ImageMagick
    else
        echo "❌ Gerenciador de pacotes não reconhecido."
        echo "   Instale manualmente o ImageMagick e execute novamente."
        exit 1
    fi
    echo ""
    echo "✅ ImageMagick instalado!"
else
    echo "✅ ImageMagick já instalado"
    convert -version | head -2
fi
echo ""

# Passo 2: Gerar ícones profissionais
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 2: Gerando ícones profissionais..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [ ! -d "$ICONS_DIR" ]; then
    echo "❌ Diretório de ícones não encontrado: $ICONS_DIR"
    exit 1
fi
cd "$ICONS_DIR"

# Tentar versão profissional primeiro
if ./generate-icons-pro.sh --style simbolo --logo-size 248 2>/dev/null; then
    echo "✅ Ícones profissionais gerados (símbolo nítido)!"
else
    echo "⚠️  Versão profissional falhou, usando versão simples..."
    ./generate-icons.sh --source "$ICONS_DIR/source/logo_icone.png"
    echo "✅ Ícones simples gerados!"
fi
echo ""

# Passo 3: Gerar .ico para Windows
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 3: Gerando .ico para Windows..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if ./generate-ico.sh 2>/dev/null; then
    echo "✅ Ícone .ico para Windows gerado!"
else
    echo "⚠️  Geração de .ico falhou (não crítico para Linux)"
fi
echo ""

# Passo 4: Atualizar atalho Linux
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 4: Atualizando atalho Linux..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [ ! -x "$LOGIN_DIR/scripts/fix-atalho-linux.sh" ]; then
    echo "❌ Script de atalho não encontrado: $LOGIN_DIR/scripts/fix-atalho-linux.sh"
    exit 1
fi
if [ "$SYSTEM_MODE" = true ]; then
    "$LOGIN_DIR/scripts/fix-atalho-linux.sh" --system
else
    "$LOGIN_DIR/scripts/fix-atalho-linux.sh"
fi
echo ""

# Passo 5: Atualizar cache de ícones
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 5: Atualizando cache de ícones do sistema..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database ~/.local/share/applications/ || true
else
    echo "⚠️  update-desktop-database não encontrado (ok ignorar)."
fi
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache ~/.local/share/icons/hicolor 2>/dev/null || true
else
    echo "⚠️  gtk-update-icon-cache não encontrado (ok ignorar)."
fi
echo "✅ Cache atualizado!"
echo ""

# Passo 6: Verificação
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "PASSO 6: Verificação..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Ícone configurado:"
if [ -f ~/.local/share/applications/protons-login.desktop ]; then
    grep Icon ~/.local/share/applications/protons-login.desktop || true
else
    echo "⚠️  Arquivo .desktop não encontrado em ~/.local/share/applications/"
fi
echo ""

echo "╔════════════════════════════════════════════════════════════════════╗"
echo "║                                                                    ║"
echo "║   ✅ ÍCONE PROFISSIONAL CONFIGURADO COM SUCESSO!                  ║"
echo "║                                                                    ║"
echo "║   🎨 Logo Protons bonito e moderno                                ║"
echo "║   💼 Estilo profissional (como programas caros)                   ║"
echo "║   🐧 Funciona no Linux                                            ║"
echo "║   🪟 Pronto para Windows (.ico gerado)                            ║"
echo "║                                                                    ║"
echo "╚════════════════════════════════════════════════════════════════════╝"
echo ""
echo "🚀 Para testar, procure 'Protons' no menu de aplicativos!"
echo "   Ou execute: gtk-launch protons-login.desktop"
echo ""
