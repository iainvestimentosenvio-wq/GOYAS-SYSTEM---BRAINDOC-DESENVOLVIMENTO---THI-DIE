#!/bin/bash
set -e

ARCH="amd64"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

# OB-01: Carregar utilitarios de log
LOG_UTILS="$PROJECT_ROOT/INSTALADOR/comum/scripts/log-utils.sh"
if [ -f "$LOG_UTILS" ]; then
    source "$LOG_UTILS"
    log_init "build-deb"
    setup_error_trap
else
    # Fallback se log-utils nao existir
    log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] [$1] ${*:2}"; }
    step_start() { log STEP "Iniciando: $1"; }
    step_end() { log OK "$1 concluido"; }
    generate_checksum() { sha256sum "$1" > "$1.sha256" 2>/dev/null; }
    log_finish() { :; }
    append_durations_to_buildinfo() { :; }
fi

require_cmd() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "❌ ERRO: comando obrigatório não encontrado: $1"
        exit 1
    }
}

require_dotnet8() {
    require_cmd dotnet
    if ! dotnet --list-sdks 2>/dev/null | cut -d' ' -f1 | grep -q '^8\.'; then
        echo "❌ ERRO: .NET SDK 8.x não encontrado"
        exit 1
    fi
}

resolve_path() {
    if command -v realpath >/dev/null 2>&1; then
        realpath "$1" 2>/dev/null && return
    fi
    if command -v readlink >/dev/null 2>&1; then
        readlink -f "$1" 2>/dev/null && return
    fi
    if command -v python >/dev/null 2>&1; then
        python - <<'PY' "$1" 2>/dev/null
import os, sys
path = sys.argv[1]
print(os.path.abspath(path))
PY
        return
    fi
    if command -v python3 >/dev/null 2>&1; then
        python3 - <<'PY' "$1" 2>/dev/null
import os, sys
path = sys.argv[1]
print(os.path.abspath(path))
PY
        return
    fi
    echo "❌ ERRO: nao foi possivel resolver path (instale coreutils ou python)" >&2
    return 1
}

safe_rm() {
    for target in "$@"; do
        if [ -z "$target" ] || [ "$target" = "/" ]; then
            echo "❌ ERRO: caminho inválido para rm -rf"
            exit 1
        fi
        local resolved
        resolved="$(resolve_path "$target")"
        if [ -z "$resolved" ]; then
            echo "❌ ERRO: não foi possível resolver caminho: $target"
            exit 1
        fi
        case "$resolved" in
            "$SCRIPT_DIR"/*|"$PROJECT_ROOT/INSTALADOR"/*) ;;
            *)
                echo "❌ ERRO: tentativa de remover caminho fora do escopo permitido: $resolved"
                exit 1
                ;;
        esac
        rm -rf "$target"
    done
}

run_with_timeout() {
    local timeout_sec="$1"
    shift
    if command -v timeout >/dev/null 2>&1; then
        timeout "$timeout_sec" "$@"
    else
        echo "⚠️  timeout não encontrado; executando sem limite: $*"
        "$@"
    fi
}

# === VC-02: Ler versao do arquivo centralizado ===
VERSION_FILE="$PROJECT_ROOT/INSTALADOR/comum/version.env"
if [ -f "$VERSION_FILE" ]; then
    VERSION="$(grep -E '^VERSION=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
    PRODUCT_NAME="$(grep -E '^PRODUCT_NAME=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
    PUBLISHER="$(grep -E '^PUBLISHER=' "$VERSION_FILE" | cut -d= -f2 | tr -d '"')"
else
    echo "❌ ERRO: version.env nao encontrado: $VERSION_FILE"
    exit 1
fi

if [ -z "$VERSION" ]; then
    echo "❌ ERRO: VERSION nao definida em $VERSION_FILE"
    exit 1
fi

PUBLISH_DIR="$PROJECT_ROOT/Login/Protons.UI/bin/Release/net8.0/linux-x64/publish"
BUILD_DIR="$SCRIPT_DIR/build"

# QB-03: Inicializar estrutura de diretórios de saída
INIT_DIRS_SCRIPT="$PROJECT_ROOT/INSTALADOR/comum/scripts/init-dirs.sh"
if [ -f "$INIT_DIRS_SCRIPT" ]; then
    source "$INIT_DIRS_SCRIPT"
    OUTPUT_DIR="$OUTPUT_DEB"
else
    echo "⚠️  init-dirs.sh não encontrado, criando diretórios manualmente"
    OUTPUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/deb"
    mkdir -p "$OUTPUT_DIR"
    mkdir -p "$PROJECT_ROOT/INSTALADOR/saida/metadata"
fi

echo "=== Protons Debian Package Builder ==="
echo "Version: $VERSION"
echo "Architecture: $ARCH"
echo ""

# Pre-requisitos
require_cmd dpkg-deb
require_cmd sed
require_cmd gzip
require_cmd du
require_cmd cut
require_cmd ln
require_cmd chmod
require_cmd cp
require_cmd mkdir
require_cmd date
require_cmd cat
require_cmd grep
require_dotnet8

# === RP-01: Reprodutibilidade ===
export TZ=UTC
export LC_ALL=C
export LANG=C

if command -v git >/dev/null 2>&1; then
    export SOURCE_DATE_EPOCH="$(git log -1 --format=%ct 2>/dev/null || date +%s)"
else
    export SOURCE_DATE_EPOCH="$(date +%s)"
fi

BUILD_METADATA_DIR="$PROJECT_ROOT/INSTALADOR/saida/metadata"
mkdir -p "$BUILD_METADATA_DIR"
GIT_COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
if command -v git >/dev/null 2>&1 && git diff --quiet 2>/dev/null; then
    GIT_DIRTY=0
else
    GIT_DIRTY=1
fi
BUILD_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

cat > "$BUILD_METADATA_DIR/build-info.txt" <<EOF
VERSION=${VERSION}
SOURCE_DATE_EPOCH=${SOURCE_DATE_EPOCH}
GIT_COMMIT=${GIT_COMMIT}
GIT_DIRTY=${GIT_DIRTY}
BUILD_UTC=${BUILD_UTC}
EOF

# QB-01: Validar que publish foi bem-sucedido
echo "Validando diretório de publish..."
if [ ! -d "$PUBLISH_DIR" ]; then
    echo "❌ ERRO: Diretório publish não encontrado: $PUBLISH_DIR"
    echo "   Execute primeiro: dotnet publish -c Release -r linux-x64 --self-contained"
    exit 1
fi

# Verificar arquivos obrigatórios
REQUIRED_FILES="Protons.UI Protons.UI.dll Protons.UI.runtimeconfig.json"
MISSING_FILES=""
for file in $REQUIRED_FILES; do
    if [ ! -f "$PUBLISH_DIR/$file" ]; then
        MISSING_FILES="$MISSING_FILES $file"
    fi
done

if [ -n "$MISSING_FILES" ]; then
    echo "❌ ERRO: Arquivos obrigatórios ausentes no publish:"
    for f in $MISSING_FILES; do
        echo "   - $f"
    done
    exit 1
fi

PUBLISH_FILE_COUNT=$(find "$PUBLISH_DIR" -maxdepth 1 -type f | wc -l)
PUBLISH_SIZE=$(du -sh "$PUBLISH_DIR" | cut -f1)
echo "✅ Publish validado: $PUBLISH_FILE_COUNT arquivos, $PUBLISH_SIZE"
echo ""

# Limpar builds anteriores
echo "Limpando builds anteriores..."
safe_rm "$BUILD_DIR" "$OUTPUT_DIR"

# Copiar template
echo "Copiando template..."
cp -r "$SCRIPT_DIR/protons-template" "$BUILD_DIR"

# Copiar binários para /opt/protons
echo "Copiando binários..."
mkdir -p "$BUILD_DIR/opt/protons"
cp -r "$PUBLISH_DIR/"* "$BUILD_DIR/opt/protons/"
# SS-02: Remover simbolos de debug (.pdb) do pacote de entrega
find "$BUILD_DIR/opt/protons" -type f -iname '*.pdb' -delete
PDB_REMOVED=$(find "$BUILD_DIR/opt/protons" -type f -iname '*.pdb' | wc -l)
if [ "$PDB_REMOVED" -eq 0 ]; then
    echo "  ✅ Simbolos de debug (.pdb) removidos do pacote"
else
    echo "  ❌ ERRO: ainda existem arquivos .pdb apos remocao" >&2
    exit 1
fi
chmod +x "$BUILD_DIR/opt/protons/Protons.UI"

# Criar symlink em /usr/bin
echo "Criando symlink..."
mkdir -p "$BUILD_DIR/usr/bin"
ln -s /opt/protons/Protons.UI "$BUILD_DIR/usr/bin/protons"

# Copiar ícones (se existirem)
ICON_SOURCE="$PROJECT_ROOT/INSTALADOR/ativos/icons/png"
if [ -d "$ICON_SOURCE" ]; then
    echo "Copiando ícones..."
    for SIZE in 16 32 48 64 128 256; do
        if [ -f "$ICON_SOURCE/${SIZE}x${SIZE}.png" ]; then
            mkdir -p "$BUILD_DIR/usr/share/icons/hicolor/${SIZE}x${SIZE}/apps"
            cp "$ICON_SOURCE/${SIZE}x${SIZE}.png" "$BUILD_DIR/usr/share/icons/hicolor/${SIZE}x${SIZE}/apps/protons.png"
            echo "  ✅ ${SIZE}x${SIZE}.png"
        else
            echo "  ⚠️  ${SIZE}x${SIZE}.png não encontrado"
        fi
    done

    # Pixmap (legacy fallback)
    if [ -f "$ICON_SOURCE/48x48.png" ]; then
        mkdir -p "$BUILD_DIR/usr/share/pixmaps"
        cp "$ICON_SOURCE/48x48.png" "$BUILD_DIR/usr/share/pixmaps/protons.png"
    fi
else
    echo "⚠️  Ícones não encontrados - pacote será criado sem ícones"
    echo "   Execute primeiro: bash INSTALADOR/ativos/icons/generate-icons.sh"
fi

# Criar diretório de documentação
mkdir -p "$BUILD_DIR/usr/share/doc/protons"

# Criar copyright
cat > "$BUILD_DIR/usr/share/doc/protons/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: protons
Source: https://example.com

Files: *
Copyright: 2026 Your Organization
License: [Your License]
EOF

# Criar changelog
CHANGELOG_DATE="$(date -R)"
if [ -n "${SOURCE_DATE_EPOCH:-}" ]; then
    CHANGELOG_DATE="$(date -R -u -d "@${SOURCE_DATE_EPOCH}" 2>/dev/null || date -R)"
fi

cat > "$BUILD_DIR/usr/share/doc/protons/changelog.Debian" <<EOF
protons ($VERSION) stable; urgency=low

  * Initial release
  * Login module with local authentication
  * User management and audit logging
  * Self-contained .NET 8 application

 -- Your Organization <support@example.com>  ${CHANGELOG_DATE}
EOF
gzip -9 -n "$BUILD_DIR/usr/share/doc/protons/changelog.Debian"

# Atualizar control file version
echo "Atualizando control file..."
sed -i "s/^Version:.*/Version: $VERSION/" "$BUILD_DIR/DEBIAN/control"

# Definir permissões corretas
echo "Definindo permissões..."
# Remover setgid/setuid bits
chmod -R u-s,g-s "$BUILD_DIR"
chmod 755 "$BUILD_DIR/DEBIAN"
chmod 644 "$BUILD_DIR/DEBIAN/control"
chmod 755 "$BUILD_DIR/DEBIAN/postinst"
chmod 755 "$BUILD_DIR/DEBIAN/postrm"
chmod -R 755 "$BUILD_DIR/opt/protons"
chmod 644 "$BUILD_DIR/usr/share/applications/protons-login.desktop"

# Calcular installed size
INSTALLED_SIZE=$(du -sk "$BUILD_DIR" | cut -f1)
if grep -q "^Installed-Size:" "$BUILD_DIR/DEBIAN/control"; then
    sed -i "s/^Installed-Size:.*/Installed-Size: $INSTALLED_SIZE/" "$BUILD_DIR/DEBIAN/control"
else
    echo "Installed-Size: $INSTALLED_SIZE" >> "$BUILD_DIR/DEBIAN/control"
fi

# Criar pacote
step_start "Empacotamento DEB"
mkdir -p "$OUTPUT_DIR"
run_with_timeout 600 dpkg-deb --build "$BUILD_DIR" "$OUTPUT_DIR/protons_${VERSION}_${ARCH}.deb"

DEB_RESULT=$?
step_end "Empacotamento DEB"

if [ $DEB_RESULT -eq 0 ]; then
    ARTIFACT="$OUTPUT_DIR/protons_${VERSION}_${ARCH}.deb"

    # OB-03: Gerar checksum SHA256
    step_start "Geracao de checksum"
    CHECKSUM=$(generate_checksum "$ARTIFACT")
    step_end "Geracao de checksum"

    # Obter tamanho do artefato
    ARTIFACT_SIZE=$(du -h "$ARTIFACT" | cut -f1)

    # OB-02: Adicionar duracoes ao build-info.txt
    append_durations_to_buildinfo "$BUILD_METADATA_DIR/build-info.txt"

    # Exibir informacoes do pacote
    log INFO "Informacoes do pacote:"
    dpkg-deb --info "$ARTIFACT" 2>/dev/null | while read line; do log INFO "  $line"; done

    # Finalizar log com resumo
    log_finish "SUCCESS" "$ARTIFACT" "$ARTIFACT_SIZE" "$CHECKSUM"

    log INFO ""
    log INFO "Para instalar: sudo dpkg -i $ARTIFACT"
else
    log ERROR "Falha ao criar pacote DEB"
    log_finish "FAILED"
    exit 1
fi
