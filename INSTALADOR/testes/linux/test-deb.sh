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

DEB_FILE="$1"

# Se não fornecido, usar caminho padrão com versão dinâmica
if [ -z "$DEB_FILE" ]; then
    DEB_FILE="$PROJECT_ROOT/INSTALADOR/saida/deb/protons_${VERSION}_amd64.deb"
    echo "Usando pacote DEB padrão: $DEB_FILE"
fi

if [ ! -f "$DEB_FILE" ]; then
    echo "❌ ERRO: Arquivo não encontrado: $DEB_FILE"
    echo "   Execute o build primeiro: bash INSTALADOR/linux/deb/build-deb.sh"
    exit 1
fi

echo "=== Teste de Pacote DEB Protons ==="
echo "Pacote: $DEB_FILE"
echo ""

# Teste 1: Estrutura do pacote
echo "[TESTE 1] Estrutura do pacote..."
CONTENTS_FILE="$(mktemp 2>/dev/null || mktemp -t protons-deb-contents)"
trap 'rm -f "$CONTENTS_FILE"' EXIT
dpkg-deb --contents "$DEB_FILE" > "$CONTENTS_FILE"
grep -q "usr/bin/protons" "$CONTENTS_FILE" || { echo "❌ FALHA: Symlink não encontrado"; exit 1; }
grep -q "opt/protons/Protons.UI" "$CONTENTS_FILE" || { echo "❌ FALHA: Executável não encontrado"; exit 1; }
grep -q "usr/share/applications/protons-login.desktop" "$CONTENTS_FILE" || { echo "❌ FALHA: Desktop file não encontrado"; exit 1; }
grep -q "usr/share/icons/hicolor/256x256/apps/protons.png" "$CONTENTS_FILE" || { echo "❌ FALHA: Ícone 256x256 não encontrado"; exit 1; }
echo "✅ PASS"

# Teste 2: Informações do pacote
echo ""
echo "[TESTE 2] Informações do pacote..."
dpkg-deb --info "$DEB_FILE"
echo "✅ PASS"

# Teste 3: Instalação (requer sudo)
echo ""
echo "[TESTE 3] Instalando pacote..."
SUDO_CMD=""
if [ "$EUID" -eq 0 ]; then
    SUDO_CMD=""
elif sudo -n true 2>/dev/null; then
    SUDO_CMD="sudo -n"
else
    echo "⚠️  SKIP: testes 3-6 requerem sudo (rode 'sudo -v' em terminal interativo e execute novamente)"
    exit 0
fi

${SUDO_CMD} dpkg -i "$DEB_FILE"
echo "✅ PASS"

# Teste 4: Verificação pós-instalação
echo ""
echo "[TESTE 4] Verificando instalação..."
[ -x /usr/bin/protons ] || { echo "❌ FALHA: Executável /usr/bin/protons não encontrado"; exit 1; }
[ -f /usr/share/applications/protons-login.desktop ] || { echo "❌ FALHA: Desktop file não encontrado"; exit 1; }
[ -d /opt/protons ] || { echo "❌ FALHA: Diretório de instalação não encontrado"; exit 1; }

# Validar Exec/Icon do .desktop
grep -q "^Exec=/usr/bin/protons" /usr/share/applications/protons-login.desktop || { echo "❌ FALHA: Exec incorreto no .desktop"; exit 1; }
grep -q "^Icon=protons" /usr/share/applications/protons-login.desktop || { echo "❌ FALHA: Icon incorreto no .desktop"; exit 1; }

echo "✅ PASS - Arquivos instalados corretamente"

# Teste 5: Execução (5 segundos)
echo ""
echo "[TESTE 5] Testando execução (5s timeout)..."
set +e
timeout 5 /usr/bin/protons &>/dev/null
RUN_RC=$?
set -e
if [ "$RUN_RC" -eq 0 ] || [ "$RUN_RC" -eq 124 ] || [ "$RUN_RC" -eq 143 ]; then
    echo "✅ PASS - Aplicação lançou (rc=$RUN_RC)"
else
    echo "❌ FALHA: Aplicação retornou código inesperado: $RUN_RC"
    exit 1
fi

# Teste 6: Desinstalação
echo ""
echo "[TESTE 6] Desinstalando pacote..."
${SUDO_CMD} dpkg -r protons
[ ! -x /usr/bin/protons ] || { echo "❌ FALHA: Executável ainda existe"; exit 1; }
echo "✅ PASS - Desinstalação limpa"

echo ""
echo "=========================================="
echo "✅ TODOS OS TESTES PASSARAM!"
echo "=========================================="
