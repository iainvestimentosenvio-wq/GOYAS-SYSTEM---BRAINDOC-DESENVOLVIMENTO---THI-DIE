#!/bin/bash
# Gera arquivo .ico para Windows usando os PNGs gerados

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PNG_DIR="$SCRIPT_DIR/png"
OUTPUT_ICO="$PROJECT_ROOT/INSTALADOR/windows/ativos/logo_protons.ico"

echo "=== Gerador de Ícone .ico para Windows ==="
echo ""
echo "Origem: $PNG_DIR"
echo "Destino: $OUTPUT_ICO"
echo ""

# Verificar se ImageMagick está instalado
if ! command -v convert &> /dev/null; then
    echo "❌ ERRO: ImageMagick não instalado"
    echo "   Instale com: sudo apt install imagemagick"
    exit 1
fi

# Verificar se PNGs existem
if [ ! -f "$PNG_DIR/16x16.png" ]; then
    echo "❌ ERRO: Ícones PNG não encontrados"
    echo "   Execute primeiro: bash generate-icons.sh"
    exit 1
fi

echo "Gerando logo_protons.ico..."
echo ""

# Criar diretório de destino se não existir
mkdir -p "$(dirname "$OUTPUT_ICO")"

# Gerar .ico com múltiplos tamanhos
convert "$PNG_DIR/16x16.png" \
        "$PNG_DIR/32x32.png" \
        "$PNG_DIR/48x48.png" \
        "$PNG_DIR/64x64.png" \
        "$PNG_DIR/128x128.png" \
        "$PNG_DIR/256x256.png" \
        "$OUTPUT_ICO"

if [ $? -eq 0 ]; then
    echo "✅ Ícone .ico gerado com sucesso!"
    echo "   Localização: $OUTPUT_ICO"
    echo "   Tamanho: $(du -h "$OUTPUT_ICO" | cut -f1)"
    echo ""
    echo "⚠️  IMPORTANTE:"
    echo "   Transfira este arquivo para o ambiente Windows:"
    echo "   - Via USB/rede"
    echo "   - WSL: Acessível em \\wsl$\Ubuntu\..."
    echo ""
else
    echo "❌ ERRO ao gerar .ico"
    exit 1
fi
