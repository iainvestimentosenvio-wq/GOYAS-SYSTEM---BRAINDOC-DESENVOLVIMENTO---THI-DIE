#!/bin/bash
# =============================================================================
# validate-version.sh - Valida que a versao esta consistente em todos os arquivos
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
VERSION_FILE="$SCRIPT_DIR/../version.env"

if [ ! -f "$VERSION_FILE" ]; then
    echo "ERRO: version.env nao encontrado"
    exit 1
fi

# Ler versao centralizada
VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"

echo "=== Validacao de Versao ==="
echo "Versao centralizada: $VERSION"
echo ""

ERRORS=0

# Funcao para verificar arquivo
check_file() {
    local file="$1"
    local pattern="$2"
    local description="$3"

    if [ -f "$file" ]; then
        if grep -q "$pattern" "$file"; then
            echo "[OK] $description"
        else
            echo "[ERRO] $description - versao incorreta"
            ERRORS=$((ERRORS + 1))
        fi
    else
        echo "[SKIP] $description - arquivo nao encontrado"
    fi
}

echo "Verificando arquivos de configuracao..."
echo ""

# version.props
check_file "$SCRIPT_DIR/../version.props" "<VersionPrefix>$VERSION</VersionPrefix>" "version.props"

# Variables.wxi (pode ser gerado dinamicamente, entao verificamos se existe)
WXI_FILE="$PROJECT_ROOT/INSTALADOR/windows/wix/Variables.wxi"
if [ -f "$WXI_FILE" ]; then
    if grep -q "Version = \"$VERSION\"" "$WXI_FILE"; then
        echo "[OK] Variables.wxi"
    else
        echo "[INFO] Variables.wxi - sera gerado no build com versao correta"
    fi
fi

echo ""
echo "Verificando artefatos de saida (se existirem)..."
echo ""

# AppImage
APPIMAGE="$PROJECT_ROOT/INSTALADOR/saida/appimage/Protons-${VERSION}-x86_64.AppImage"
if [ -f "$APPIMAGE" ]; then
    echo "[OK] AppImage: Protons-${VERSION}-x86_64.AppImage"
else
    echo "[INFO] AppImage nao encontrado (execute o build)"
fi

# DEB
DEB="$PROJECT_ROOT/INSTALADOR/saida/deb/protons_${VERSION}_amd64.deb"
if [ -f "$DEB" ]; then
    # Verificar versao interna do DEB
    DEB_VERSION="$(dpkg-deb --field "$DEB" Version 2>/dev/null || echo "")"
    if [ "$DEB_VERSION" = "$VERSION" ]; then
        echo "[OK] DEB: protons_${VERSION}_amd64.deb (Version: $DEB_VERSION)"
    else
        echo "[ERRO] DEB: versao interna ($DEB_VERSION) != esperada ($VERSION)"
        ERRORS=$((ERRORS + 1))
    fi
else
    echo "[INFO] DEB nao encontrado (execute o build)"
fi

# MSI
MSI="$PROJECT_ROOT/INSTALADOR/saida/windows/Protons-${VERSION}-x64.msi"
if [ -f "$MSI" ]; then
    echo "[OK] MSI: Protons-${VERSION}-x64.msi"
else
    echo "[INFO] MSI nao encontrado (execute o build no Windows)"
fi

# EXE (Inno)
EXE="$PROJECT_ROOT/INSTALADOR/saida/windows/ProtonsSetup-${VERSION}.exe"
if [ -f "$EXE" ]; then
    echo "[OK] EXE: ProtonsSetup-${VERSION}.exe"
else
    echo "[INFO] EXE nao encontrado (execute o build no Windows)"
fi

echo ""
echo "=== Resultado ==="

if [ $ERRORS -eq 0 ]; then
    echo "Todas as verificacoes passaram!"
    exit 0
else
    echo "Encontrados $ERRORS erros de versao"
    exit 1
fi
