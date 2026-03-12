#!/bin/bash
# =============================================================================
# bump-version.sh - Atualiza a versao centralizada do projeto
# =============================================================================
# Uso:
#   ./bump-version.sh <nova-versao>
#   ./bump-version.sh 1.1.0
#   ./bump-version.sh patch   # 1.0.0 -> 1.0.1
#   ./bump-version.sh minor   # 1.0.0 -> 1.1.0
#   ./bump-version.sh major   # 1.0.0 -> 2.0.0
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VERSION_FILE="$SCRIPT_DIR/../version.env"
VERSION_PROPS="$SCRIPT_DIR/../version.props"

if [ ! -f "$VERSION_FILE" ]; then
    echo "ERRO: version.env nao encontrado: $VERSION_FILE"
    exit 1
fi

# Ler versao atual
CURRENT_VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"

if [ -z "$CURRENT_VERSION" ]; then
    echo "ERRO: VERSION nao definida em $VERSION_FILE"
    exit 1
fi

# Extrair major.minor.patch
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

# Processar argumento
NEW_VERSION="$1"

if [ -z "$NEW_VERSION" ]; then
    echo "Uso: $0 <nova-versao>"
    echo ""
    echo "Exemplos:"
    echo "  $0 1.1.0    # Define versao especifica"
    echo "  $0 patch    # Incrementa patch: $CURRENT_VERSION -> $MAJOR.$MINOR.$((PATCH + 1))"
    echo "  $0 minor    # Incrementa minor: $CURRENT_VERSION -> $MAJOR.$((MINOR + 1)).0"
    echo "  $0 major    # Incrementa major: $CURRENT_VERSION -> $((MAJOR + 1)).0.0"
    echo ""
    echo "Versao atual: $CURRENT_VERSION"
    exit 1
fi

case "$NEW_VERSION" in
    patch)
        NEW_VERSION="$MAJOR.$MINOR.$((PATCH + 1))"
        ;;
    minor)
        NEW_VERSION="$MAJOR.$((MINOR + 1)).0"
        ;;
    major)
        NEW_VERSION="$((MAJOR + 1)).0.0"
        ;;
    *)
        # Validar formato X.Y.Z
        if ! echo "$NEW_VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
            echo "ERRO: Formato de versao invalido: $NEW_VERSION"
            echo "      Use o formato X.Y.Z (ex: 1.2.3)"
            exit 1
        fi
        ;;
esac

# Extrair novos valores
IFS='.' read -r NEW_MAJOR NEW_MINOR NEW_PATCH <<< "$NEW_VERSION"

echo "=== Bump de Versao ==="
echo "Versao atual:  $CURRENT_VERSION"
echo "Nova versao:   $NEW_VERSION"
echo ""

# Atualizar version.env
echo "Atualizando $VERSION_FILE..."
sed -i "s/^VERSION=.*/VERSION=$NEW_VERSION/" "$VERSION_FILE"
sed -i "s/^VERSION_MAJOR=.*/VERSION_MAJOR=$NEW_MAJOR/" "$VERSION_FILE"
sed -i "s/^VERSION_MINOR=.*/VERSION_MINOR=$NEW_MINOR/" "$VERSION_FILE"
sed -i "s/^VERSION_PATCH=.*/VERSION_PATCH=$NEW_PATCH/" "$VERSION_FILE"

# Atualizar version.props
if [ -f "$VERSION_PROPS" ]; then
    echo "Atualizando $VERSION_PROPS..."
    sed -i "s/<VersionPrefix>.*<\/VersionPrefix>/<VersionPrefix>$NEW_VERSION<\/VersionPrefix>/" "$VERSION_PROPS"
fi

echo ""
echo "Versao atualizada para $NEW_VERSION"
echo ""
echo "Arquivos modificados:"
echo "  - $VERSION_FILE"
[ -f "$VERSION_PROPS" ] && echo "  - $VERSION_PROPS"
echo ""
echo "Proximo passo: execute os scripts de build para gerar artefatos com a nova versao"
echo "  Linux:   bash INSTALADOR/linux/appimage/build-appimage.sh"
echo "  Linux:   bash INSTALADOR/linux/deb/build-deb.sh"
echo "  Windows: .\\INSTALADOR\\windows\\scripts\\build-msi.ps1"
echo "  Windows: .\\INSTALADOR\\windows\\scripts\\build-inno.ps1"
