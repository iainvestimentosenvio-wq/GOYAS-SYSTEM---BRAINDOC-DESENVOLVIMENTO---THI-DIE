#!/bin/bash
# Gerador de Ícones PROFISSIONAIS para Protons
# Cria ícones bonitos, modernos e com o logo bem visível

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/png"
DEFAULT_SOURCE="$PROJECT_ROOT/INSTALADOR/ativos/icons/source/logo_icone.png"
if [ ! -f "$DEFAULT_SOURCE" ]; then
    DEFAULT_SOURCE="$PROJECT_ROOT/Login/Protons.UI/Assets/Brand/logo_protons.png"
fi

STYLE="${PROTONS_ICON_STYLE:-futurista}"
SOURCE="${PROTONS_ICON_SOURCE:-$DEFAULT_SOURCE}"
LOGO_SIZE="${PROTONS_ICON_LOGO_SIZE:-}"
BG_START="${PROTONS_ICON_BG_START:-}"
BG_END="${PROTONS_ICON_BG_END:-}"
ACCENT="${PROTONS_ICON_ACCENT:-}"
ACCENT_GLOW="${PROTONS_ICON_ACCENT_GLOW:-}"
GLASS_FILL="${PROTONS_ICON_GLASS_FILL:-}"
GLASS_STROKE="${PROTONS_ICON_GLASS_STROKE:-}"

while [ $# -gt 0 ]; do
    case "$1" in
        --source|-s)
            SOURCE="$2"
            shift 2
            ;;
        --style)
            STYLE="$2"
            shift 2
            ;;
        --logo-size)
            LOGO_SIZE="$2"
            shift 2
            ;;
        --bg-start)
            BG_START="$2"
            shift 2
            ;;
        --bg-end)
            BG_END="$2"
            shift 2
            ;;
        --accent)
            ACCENT="$2"
            shift 2
            ;;
        --help|-h)
            echo "Uso: $0 [--source CAMINHO] [--style futurista|classic|simbolo] [--logo-size PX]"
            echo "          [--bg-start #HEX] [--bg-end #HEX] [--accent #HEX]"
            echo ""
            echo "Exemplo:"
            echo "  $0 --source /caminho/para/logo.png --style futurista"
            exit 0
            ;;
        *)
            echo "❌ Parâmetro desconhecido: $1"
            exit 1
            ;;
    esac
done

echo "╔═══════════════════════════════════════════════════════════╗"
echo "║     GERADOR DE ÍCONES PROFISSIONAIS - PROTONS             ║"
echo "╚═══════════════════════════════════════════════════════════╝"
echo ""
echo "📁 Fonte: $SOURCE"
echo "📂 Destino: $OUTPUT_DIR"
echo "🎨 Estilo: $STYLE"
echo ""

# Verificar se fonte existe
if [ ! -f "$SOURCE" ]; then
    echo "❌ ERRO: Logo fonte não encontrado: $SOURCE"
    exit 1
fi

# Verificar se ImageMagick está instalado (fallback em Python se não tiver)
if ! command -v convert &> /dev/null; then
    echo "⚠️  ImageMagick não instalado. Usando fallback em Python (Pillow)..."
    if ! command -v python3 &> /dev/null; then
        echo "❌ ERRO: Python3 não encontrado para fallback."
        exit 1
    fi

    PYTHON_BIN="python3"
    if ! "$PYTHON_BIN" - <<'PY'
try:
    from PIL import Image  # noqa: F401
except Exception as exc:
    raise SystemExit(1) from exc
PY
    then
        VENV_PY="$SCRIPT_DIR/.venv/bin/python"
        if [ -x "$VENV_PY" ] && "$VENV_PY" - <<'PY'
try:
    from PIL import Image  # noqa: F401
except Exception as exc:
    raise SystemExit(1) from exc
PY
        then
            PYTHON_BIN="$VENV_PY"
        else
            echo "❌ ERRO: Pillow não instalado."
            echo "   Crie um venv e instale:"
            echo "   python3 -m venv $SCRIPT_DIR/.venv"
            echo "   $SCRIPT_DIR/.venv/bin/pip install pillow"
            exit 1
        fi
    fi

    PY_ARGS=(--source "$SOURCE" --style "$STYLE")
    [ -n "$LOGO_SIZE" ] && PY_ARGS+=(--logo-size "$LOGO_SIZE")
    [ -n "$BG_START" ] && PY_ARGS+=(--bg-start "$BG_START")
    [ -n "$BG_END" ] && PY_ARGS+=(--bg-end "$BG_END")
    [ -n "$ACCENT" ] && PY_ARGS+=(--accent "$ACCENT")
    [ -n "$ACCENT_GLOW" ] && PY_ARGS+=(--accent-glow "$ACCENT_GLOW")
    [ -n "$GLASS_FILL" ] && PY_ARGS+=(--glass-fill "$GLASS_FILL")
    [ -n "$GLASS_STROKE" ] && PY_ARGS+=(--glass-stroke "$GLASS_STROKE")

    "$PYTHON_BIN" "$SCRIPT_DIR/generate-icons-pro.py" "${PY_ARGS[@]}"
    exit $?
fi

echo "✨ Gerando ícones profissionais PNG..."
echo ""

mkdir -p "$OUTPUT_DIR"

# ÍCONE PROFISSIONAL - Logo grande, centralizado, com base futurista
echo "🎨 Criando versão base 256x256 (estilo profissional)..."

BASE_SIZE=256
BG_FILE="$OUTPUT_DIR/bg.png"
GLOW_FILE="$OUTPUT_DIR/bg-glow.png"
RING_FILE="$OUTPUT_DIR/bg-ring.png"
GLASS_FILE="$OUTPUT_DIR/bg-glass.png"
LOGO_FILE="$OUTPUT_DIR/logo-resized.png"
LOGO_GLOW_FILE="$OUTPUT_DIR/logo-glow.png"
COMPOSITE_FILE="$OUTPUT_DIR/temp-256.png"

case "$STYLE" in
    futurista)
        BG_START="${BG_START:-#0B1020}"
        BG_END="${BG_END:-#123556}"
        ACCENT="${ACCENT:-#4CC9F0}"
        ACCENT_GLOW="${ACCENT_GLOW:-rgba(76,201,240,0.55)}"
        GLASS_FILL="${GLASS_FILL:-rgba(255,255,255,0.10)}"
        GLASS_STROKE="${GLASS_STROKE:-rgba(255,255,255,0.20)}"
        LOGO_SIZE="${LOGO_SIZE:-200}"
        ;;
    classic|profissional)
        BG_START="${BG_START:-#E8F4FF}"
        BG_END="${BG_END:-#FFFFFF}"
        ACCENT="${ACCENT:-#2D7BD4}"
        ACCENT_GLOW="${ACCENT_GLOW:-rgba(45,123,212,0.35)}"
        GLASS_FILL="${GLASS_FILL:-rgba(255,255,255,0.00)}"
        GLASS_STROKE="${GLASS_STROKE:-rgba(255,255,255,0.00)}"
        LOGO_SIZE="${LOGO_SIZE:-192}"
        ;;
    simbolo|clean|only)
        LOGO_SIZE="${LOGO_SIZE:-248}"
        ;;
    *)
        echo "❌ Estilo inválido: $STYLE"
        echo "   Use: futurista | classic | simbolo"
        exit 1
        ;;
esac

if [ "$STYLE" = "simbolo" ] || [ "$STYLE" = "clean" ] || [ "$STYLE" = "only" ]; then
    # Fundo transparente (apenas simbolo)
    convert -size ${BASE_SIZE}x${BASE_SIZE} xc:none "$BG_FILE"
else
    # Fundo base
    convert -size ${BASE_SIZE}x${BASE_SIZE} \
        gradient:"$BG_START-$BG_END" \
        "$BG_FILE"
fi

if [ "$STYLE" = "futurista" ]; then
    # Glow suave no fundo
    convert -size ${BASE_SIZE}x${BASE_SIZE} xc:none \
        -fill "$ACCENT_GLOW" \
        -draw "circle 190,70 190,20" \
        -blur 0x28 \
        "$GLOW_FILE"

    convert "$BG_FILE" "$GLOW_FILE" -compose screen -composite "$BG_FILE"

    # Anel sutil (sensacao de tecnologia)
    convert -size ${BASE_SIZE}x${BASE_SIZE} xc:none \
        -stroke "$ACCENT_GLOW" -strokewidth 3 -fill none \
        -draw "circle 128,128 128,30" \
        "$RING_FILE"

    convert "$BG_FILE" "$RING_FILE" -compose screen -composite "$BG_FILE"

    # Placa de vidro no centro
    convert -size ${BASE_SIZE}x${BASE_SIZE} xc:none \
        -fill "$GLASS_FILL" -stroke "$GLASS_STROKE" -strokewidth 1 \
        -draw "roundrectangle 26,26 230,230 28,28" \
        "$GLASS_FILE"

    convert "$BG_FILE" "$GLASS_FILE" -compose over -composite "$BG_FILE"
fi

# Redimensionar logo para ocupar o espaco principal
convert "$SOURCE" \
    -resize ${LOGO_SIZE}x${LOGO_SIZE} \
    -background transparent \
    -gravity center \
    -extent ${LOGO_SIZE}x${LOGO_SIZE} \
    "$LOGO_FILE"

if [ "$STYLE" = "futurista" ]; then
    # Glow do logo (neon sutil)
    convert "$LOGO_FILE" \
        \( +clone -background "$ACCENT" -shadow 55x14+0+0 \) \
        +swap -background none -layers merge +repage \
        "$LOGO_GLOW_FILE"

    convert "$BG_FILE" "$LOGO_GLOW_FILE" \
        -gravity center -compose screen -composite \
        "$COMPOSITE_FILE"
elif [ "$STYLE" = "classic" ] || [ "$STYLE" = "profissional" ]; then
    # Sombra classica
    convert "$BG_FILE" \
        \( "$LOGO_FILE" \
           \( +clone -background black -shadow 20x4+0+2 \) \
           +swap -background none -layers merge +repage \) \
        -gravity center -composite \
    "$COMPOSITE_FILE"
else
    # Apenas simbolo: fundo transparente sem sombra
    cp "$BG_FILE" "$COMPOSITE_FILE"
fi

# Compor logo final
convert "$COMPOSITE_FILE" "$LOGO_FILE" \
    -gravity center -compose over -composite \
    "$COMPOSITE_FILE"

# Adicionar borda arredondada sutil (opcional, estilo moderno)
if [ "$STYLE" = "simbolo" ] || [ "$STYLE" = "clean" ] || [ "$STYLE" = "only" ]; then
    cp "$COMPOSITE_FILE" "$OUTPUT_DIR/256x256.png"
else
    convert "$COMPOSITE_FILE" \
        \( +clone -alpha extract \
           -draw 'fill black polygon 0,0 0,8 8,0 fill white circle 8,8 8,0' \
           \( +clone -flip \) -compose Multiply -composite \
           \( +clone -flop \) -compose Multiply -composite \
        \) -alpha off -compose CopyOpacity -composite \
        "$OUTPUT_DIR/256x256.png"
fi

if [ $? -eq 0 ]; then
    echo "  ✅ Base 256x256 gerada (com gradiente e sombra)"
else
    echo "  ⚠️  Erro ao criar versão profissional, usando versão simples..."
    # Fallback: versão simples sem gradiente
    convert "$SOURCE" \
        -background white \
        -gravity center \
        -resize 192x192 \
        -extent 256x256 \
        "$OUTPUT_DIR/256x256.png"
fi

# Limpar arquivos temporários
rm -f "$BG_FILE" "$GLOW_FILE" "$RING_FILE" "$GLASS_FILE" "$LOGO_FILE" "$LOGO_GLOW_FILE" "$COMPOSITE_FILE"

# Gerar todos os tamanhos a partir do 256x256
echo ""
echo "📐 Gerando todos os tamanhos..."
for SIZE in 16 32 48 64 128; do
    convert "$OUTPUT_DIR/256x256.png" -resize ${SIZE}x${SIZE} "$OUTPUT_DIR/${SIZE}x${SIZE}.png"

    if [ $? -eq 0 ]; then
        echo "  ✅ ${SIZE}x${SIZE}.png"
    else
        echo "  ❌ ERRO: ${SIZE}x${SIZE}.png"
        exit 1
    fi
done

echo ""
echo "╔═══════════════════════════════════════════════════════════╗"
echo "║  ✅ ÍCONES PROFISSIONAIS GERADOS COM SUCESSO!             ║"
echo "╚═══════════════════════════════════════════════════════════╝"
echo ""
echo "📂 Localização: $OUTPUT_DIR"
echo ""
echo "🐧 Para atualizar o atalho Linux:"
echo "   cd $PROJECT_ROOT/Login"
echo "   ./scripts/fix-atalho-linux.sh"
echo ""
echo "🪟 Para Windows (.ico):"
echo "   Execute: ./generate-ico.sh"
echo ""
