#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

# QB-06: Ler versao do arquivo centralizado
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"
if [ -f "$VERSION_FILE" ]; then
    VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
    echo "Versão detectada: $VERSION"
else
    VERSION="1.0.0"
    echo "⚠️  version.env não encontrado, usando versão padrão: $VERSION"
fi

APPIMAGE="$1"

# Se não fornecido, usar caminho padrão com versão dinâmica
if [ -z "$APPIMAGE" ]; then
    APPIMAGE="$PROJECT_ROOT/INSTALADOR/saida/appimage/Protons-${VERSION}-x86_64.AppImage"
    echo "Usando AppImage padrão: $APPIMAGE"
fi

if [ ! -f "$APPIMAGE" ]; then
    echo "❌ ERRO: Arquivo não encontrado: $APPIMAGE"
    echo "   Execute o build primeiro: bash INSTALADOR/linux/appimage/build-appimage.sh"
    exit 1
fi

echo "=== Teste de AppImage Protons ==="
echo "AppImage: $APPIMAGE"
echo ""

# Teste 1: Permissão de execução
echo "[TESTE 1] Verificando permissão de execução..."
[ -x "$APPIMAGE" ] || { echo "❌ FALHA: Não executável"; exit 1; }
echo "✅ PASS"

# Teste 2: Estrutura AppImage (extração)
echo ""
echo "[TESTE 2] Verificando estrutura AppImage..."
"$APPIMAGE" --appimage-extract >/dev/null 2>&1 || true
[ -f squashfs-root/AppRun ] || { echo "❌ FALHA: AppRun não encontrado"; exit 1; }
[ -f squashfs-root/protons-login.desktop ] || { echo "❌ FALHA: Desktop file não encontrado"; exit 1; }
[ -x squashfs-root/usr/bin/Protons.UI ] || { echo "❌ FALHA: Executável não encontrado"; exit 1; }

# Verificar AppRun aponta para Protons.UI
grep -q "usr/bin/Protons.UI" squashfs-root/AppRun || { echo "❌ FALHA: AppRun não aponta para Protons.UI"; exit 1; }

# Verificar Exec/Icon no desktop file
grep -q "^Exec=Protons.UI" squashfs-root/protons-login.desktop || { echo "❌ FALHA: Exec incorreto no .desktop"; exit 1; }
grep -q "^Icon=protons" squashfs-root/protons-login.desktop || { echo "❌ FALHA: Icon incorreto no .desktop"; exit 1; }

# Verificar ícone existe dentro do AppImage
[ -f squashfs-root/usr/share/icons/hicolor/256x256/apps/protons.png ] || { echo "❌ FALHA: Ícone protons.png não encontrado"; exit 1; }

rm -rf squashfs-root
echo "✅ PASS"

# Teste 3: Execução (5 segundos)
echo ""
echo "[TESTE 3] Testando execução (5s timeout)..."
set +e
timeout 5 "$APPIMAGE" &>/dev/null
RUN_RC=$?
set -e
if [ "$RUN_RC" -eq 0 ] || [ "$RUN_RC" -eq 124 ] || [ "$RUN_RC" -eq 143 ]; then
    echo "✅ PASS - Aplicação lançou (rc=$RUN_RC)"
else
    echo "❌ FALHA: Aplicação retornou código inesperado: $RUN_RC"
    exit 1
fi

# Teste 4: Criação de diretório de dados
echo ""
echo "[TESTE 4] Verificando diretório de dados..."
[ -d "$HOME/.local/share/Protons" ] || { echo "❌ FALHA: Diretório de dados não criado"; exit 1; }
echo "✅ PASS - Diretório ~/.local/share/Protons criado"

# Teste 5: Portabilidade (pode mover arquivo)
echo ""
echo "[TESTE 5] Testando portabilidade..."
TEMP_DIR=$(mktemp -d)
cp "$APPIMAGE" "$TEMP_DIR/"
chmod +x "$TEMP_DIR/$(basename "$APPIMAGE")"
set +e
timeout 2 "$TEMP_DIR/$(basename "$APPIMAGE")" &>/dev/null
PORT_RC=$?
set -e
if [ "$PORT_RC" -ne 0 ] && [ "$PORT_RC" -ne 124 ] && [ "$PORT_RC" -ne 143 ]; then
    echo "❌ FALHA: Teste de portabilidade retornou código inesperado: $PORT_RC"
    rm -rf "$TEMP_DIR"
    exit 1
fi
rm -rf "$TEMP_DIR"
echo "✅ PASS - AppImage funciona em diretório diferente"

echo ""
echo "=========================================="
echo "✅ TODOS OS TESTES PASSARAM!"
echo "=========================================="
