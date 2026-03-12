#!/bin/bash
# =============================================================================
# init-dirs.sh - Cria estrutura de diretorios de saida para builds
# =============================================================================
# QB-03: Padroniza criacao de diretorios de saida
#
# Uso:
#   source ../comum/scripts/init-dirs.sh
#   ou
#   bash ../comum/scripts/init-dirs.sh
#
# A variavel PROJECT_ROOT deve estar definida antes de chamar este script
# =============================================================================

set -e

# Se PROJECT_ROOT nao estiver definido, tentar descobrir
if [ -z "$PROJECT_ROOT" ]; then
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
fi

# Estrutura de diretorios de saida
OUTPUT_BASE="$PROJECT_ROOT/INSTALADOR/saida"
OUTPUT_WINDOWS="$OUTPUT_BASE/windows"
OUTPUT_APPIMAGE="$OUTPUT_BASE/appimage"
OUTPUT_DEB="$OUTPUT_BASE/deb"
OUTPUT_METADATA="$OUTPUT_BASE/metadata"

# Funcao para criar diretorios
init_output_dirs() {
    echo "Inicializando estrutura de diretórios de saída..."

    # Criar diretorios principais
    mkdir -p "$OUTPUT_WINDOWS"
    mkdir -p "$OUTPUT_APPIMAGE"
    mkdir -p "$OUTPUT_DEB"
    mkdir -p "$OUTPUT_METADATA"

    echo "✅ Diretórios criados:"
    echo "   - $OUTPUT_WINDOWS"
    echo "   - $OUTPUT_APPIMAGE"
    echo "   - $OUTPUT_DEB"
    echo "   - $OUTPUT_METADATA"
}

# Exportar variaveis para uso nos scripts de build
export OUTPUT_BASE
export OUTPUT_WINDOWS
export OUTPUT_APPIMAGE
export OUTPUT_DEB
export OUTPUT_METADATA

# Se executado diretamente (nao via source), criar os diretorios
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    init_output_dirs
fi
