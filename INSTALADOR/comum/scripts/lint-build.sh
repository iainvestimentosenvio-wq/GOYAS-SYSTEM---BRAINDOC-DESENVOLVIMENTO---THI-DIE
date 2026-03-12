#!/bin/bash
# =============================================================================
# lint-build.sh - Valida configuração antes de executar builds
# =============================================================================
# QB-04: Script de lint para validação de qualidade
#
# Verifica:
#   - Existência de arquivos obrigatórios
#   - Consistência de versão
#   - Dependências necessárias
#   - Padrões de código
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$SCRIPT_DIR/../version.env"

echo "=== Lint de Build - Protons ==="
echo ""

ERRORS=0
WARNINGS=0

# Função para reportar erro
error() {
    echo "❌ ERRO: $1"
    ERRORS=$((ERRORS + 1))
}

# Função para reportar warning
warn() {
    echo "⚠️  AVISO: $1"
    WARNINGS=$((WARNINGS + 1))
}

# Função para reportar sucesso
ok() {
    echo "✅ $1"
}

# =============================================================================
# 1. Verificar arquivos de configuração obrigatórios
# =============================================================================
echo "[1/5] Verificando arquivos de configuração..."

[ -f "$VERSION_FILE" ] && ok "version.env existe" || error "version.env não encontrado"
[ -f "$SCRIPT_DIR/../version.props" ] && ok "version.props existe" || error "version.props não encontrado"
[ -f "$PROJECT_ROOT/Login/global.json" ] && ok "global.json existe" || warn "global.json não encontrado (SDK não fixado)"

echo ""

# =============================================================================
# 2. Verificar consistência de versão
# =============================================================================
echo "[2/5] Verificando consistência de versão..."

if [ -f "$VERSION_FILE" ]; then
    VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
    ok "Versão centralizada: $VERSION"

    # Verificar version.props
    if [ -f "$SCRIPT_DIR/../version.props" ]; then
        PROPS_VERSION="$(grep -oP '(?<=<VersionPrefix>)[^<]+' "$SCRIPT_DIR/../version.props" || echo "")"
        if [ "$PROPS_VERSION" = "$VERSION" ]; then
            ok "version.props consistente"
        else
            error "version.props inconsistente: $PROPS_VERSION (esperado: $VERSION)"
        fi
    fi
fi

echo ""

# =============================================================================
# 3. Verificar estrutura de diretórios
# =============================================================================
echo "[3/5] Verificando estrutura de diretórios..."

[ -d "$PROJECT_ROOT/INSTALADOR/windows/wix" ] && ok "Diretório WiX existe" || warn "Diretório WiX não encontrado"
[ -d "$PROJECT_ROOT/INSTALADOR/windows/innosetup" ] && ok "Diretório Inno Setup existe" || warn "Diretório Inno Setup não encontrado"
[ -d "$PROJECT_ROOT/INSTALADOR/linux/appimage" ] && ok "Diretório AppImage existe" || warn "Diretório AppImage não encontrado"
[ -d "$PROJECT_ROOT/INSTALADOR/linux/deb" ] && ok "Diretório DEB existe" || warn "Diretório DEB não encontrado"
[ -d "$PROJECT_ROOT/Login/Protons.UI" ] && ok "Projeto Protons.UI existe" || error "Projeto Protons.UI não encontrado"

echo ""

# =============================================================================
# 4. Verificar arquivos de build essenciais
# =============================================================================
echo "[4/5] Verificando arquivos de build..."

# WiX
[ -f "$PROJECT_ROOT/INSTALADOR/windows/wix/Product.wxs" ] && ok "Product.wxs existe" || error "Product.wxs não encontrado"
[ -f "$PROJECT_ROOT/INSTALADOR/windows/wix/Components.wxs" ] && ok "Components.wxs existe" || error "Components.wxs não encontrado"
[ -f "$PROJECT_ROOT/INSTALADOR/windows/wix/Features.wxs" ] && ok "Features.wxs existe" || error "Features.wxs não encontrado"

# Inno Setup
[ -f "$PROJECT_ROOT/INSTALADOR/windows/innosetup/protons-setup.iss" ] && ok "protons-setup.iss existe" || error "protons-setup.iss não encontrado"

# AppImage
[ -f "$PROJECT_ROOT/INSTALADOR/linux/appimage/AppRun" ] && ok "AppRun existe" || error "AppRun não encontrado"

# DEB
[ -d "$PROJECT_ROOT/INSTALADOR/linux/deb/protons-template" ] && ok "Template DEB existe" || error "Template DEB não encontrado"

# Projeto .NET
[ -f "$PROJECT_ROOT/Login/Protons.UI/Protons.UI.csproj" ] && ok "Protons.UI.csproj existe" || error "Protons.UI.csproj não encontrado"

echo ""

# =============================================================================
# 5. Verificar por versões hardcoded (anti-pattern)
# =============================================================================
echo "[5/5] Verificando por versões hardcoded..."

# Lista de arquivos para verificar
FILES_TO_CHECK=(
    "$PROJECT_ROOT/INSTALADOR/windows/wix/Protons.wixproj"
    "$PROJECT_ROOT/INSTALADOR/windows/wix/Variables.wxi"
)

HARDCODED_PATTERN='[0-9]+\.[0-9]+\.[0-9]+'

for file in "${FILES_TO_CHECK[@]}"; do
    if [ -f "$file" ]; then
        # Verificar se é arquivo gerado automaticamente
        if grep -q "ARQUIVO GERADO AUTOMATICAMENTE" "$file" 2>/dev/null; then
            ok "$(basename "$file") é gerado automaticamente"
        else
            # Verificar versões hardcoded (exceto em comentários)
            if grep -v "^[[:space:]]*#" "$file" | grep -v "^[[:space:]]*<\!--" | grep -qE "$HARDCODED_PATTERN"; then
                warn "$(basename "$file") pode conter versão hardcoded"
            else
                ok "$(basename "$file") não tem versão hardcoded visível"
            fi
        fi
    fi
done

echo ""

# =============================================================================
# Resumo
# =============================================================================
echo "==========================================="
echo "RESUMO DO LINT"
echo "==========================================="
echo "Erros:   $ERRORS"
echo "Avisos:  $WARNINGS"
echo ""

if [ $ERRORS -gt 0 ]; then
    echo "❌ Build NÃO está pronto - corrija os erros acima"
    exit 1
elif [ $WARNINGS -gt 0 ]; then
    echo "⚠️  Build pode prosseguir, mas considere os avisos"
    exit 0
else
    echo "✅ Build está pronto para execução!"
    exit 0
fi
