#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/png"
DEFAULT_SOURCE="$PROJECT_ROOT/INSTALADOR/ativos/icons/source/logo_icone.png"
if [ ! -f "$DEFAULT_SOURCE" ]; then
    DEFAULT_SOURCE="$PROJECT_ROOT/Login/Protons.UI/Assets/Brand/logo_protons.png"
fi
SOURCE="${PROTONS_ICON_SOURCE:-$DEFAULT_SOURCE}"

while [ $# -gt 0 ]; do
    case "$1" in
        --source|-s)
            SOURCE="$2"
            shift 2
            ;;
        --help|-h)
            echo "Uso: $0 [--source CAMINHO]"
            exit 0
            ;;
        *)
            echo "❌ Parâmetro desconhecido: $1"
            exit 1
            ;;
    esac
done

echo "=== Gerador de Ícones Protons ==="
echo ""
echo "Fonte: $SOURCE"
echo "Destino: $OUTPUT_DIR"
echo ""

# Verificar se ImageMagick está instalado
if ! command -v convert &> /dev/null; then
    echo "❌ ERRO: ImageMagick não instalado"
    echo "   Instale com: sudo apt install imagemagick"
    exit 1
fi

# Verificar se fonte existe
if [ ! -f "$SOURCE" ]; then
    echo "❌ ERRO: Logo fonte não encontrado: $SOURCE"
    exit 1
fi

echo "Gerando ícones PNG para Linux..."
echo ""

# Criar versão quadrada (pad para 256x256)
TEMP_FILE="$OUTPUT_DIR/temp-256.png"
mkdir -p "$OUTPUT_DIR"
convert "$SOURCE" -background transparent -gravity center -extent 256x256 "$TEMP_FILE"

if [ $? -ne 0 ]; then
    echo "❌ ERRO: Falha ao criar versão quadrada"
    exit 1
fi

# Gerar todos os tamanhos para Linux
for SIZE in 16 32 48 64 128 256; do
    convert "$TEMP_FILE" -resize ${SIZE}x${SIZE} "$OUTPUT_DIR/${SIZE}x${SIZE}.png"

    if [ $? -eq 0 ]; then
        echo "  ✅ Gerado: ${SIZE}x${SIZE}.png"
    else
        echo "  ❌ ERRO ao gerar: ${SIZE}x${SIZE}.png"
        rm -f "$TEMP_FILE"
        exit 1
    fi
done

rm -f "$TEMP_FILE"

echo ""
echo "=========================================="
echo "✅ Ícones PNG gerados com sucesso!"
echo "   Localização: $OUTPUT_DIR"
echo "=========================================="
echo ""
echo "⚠️  IMPORTANTE: logo_protons.ico (Windows)"
echo ""
echo "Para gerar o ícone .ico para Windows:"
echo "  Opção 1 (Recomendada): Usar ImageMagick no Windows/WSL"
echo "  Opção 2: Converter online em https://convertio.co/png-ico/"
echo "  Opção 3: Executar script abaixo (pode ter problemas)"
echo ""
echo "Script para .ico (usar no Windows se possível):"
echo "-----------------------------------------------"
echo 'convert "$OUTPUT_DIR/16x16.png" \'
echo '        "$OUTPUT_DIR/32x32.png" \'
echo '        "$OUTPUT_DIR/48x48.png" \'
echo '        "$OUTPUT_DIR/64x64.png" \'
echo '        "$OUTPUT_DIR/128x128.png" \'
echo '        "$OUTPUT_DIR/256x256.png" \'
echo '        "'$PROJECT_ROOT'/INSTALADOR/windows/ativos/logo_protons.ico"'
echo ""
