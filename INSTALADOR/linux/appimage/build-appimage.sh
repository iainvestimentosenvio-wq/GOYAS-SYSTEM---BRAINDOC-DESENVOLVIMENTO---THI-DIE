#!/bin/bash
set -e

ARCH="x86_64"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

# OB-01: Carregar utilitarios de log
LOG_UTILS="$PROJECT_ROOT/INSTALADOR/comum/scripts/log-utils.sh"
if [ -f "$LOG_UTILS" ]; then
    source "$LOG_UTILS"
    log_init "build-appimage"
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
APPDIR="$SCRIPT_DIR/AppDir"

# QB-03: Inicializar estrutura de diretórios de saída
INIT_DIRS_SCRIPT="$PROJECT_ROOT/INSTALADOR/comum/scripts/init-dirs.sh"
if [ -f "$INIT_DIRS_SCRIPT" ]; then
    source "$INIT_DIRS_SCRIPT"
    OUTPUT_DIR="$OUTPUT_APPIMAGE"
else
    echo "⚠️  init-dirs.sh não encontrado, criando diretórios manualmente"
    OUTPUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/appimage"
    mkdir -p "$OUTPUT_DIR"
    mkdir -p "$PROJECT_ROOT/INSTALADOR/saida/metadata"
fi

log INFO "=== Protons AppImage Builder ==="
log INFO "Version: $VERSION"
log INFO "Architecture: $ARCH"

# Pre-requisitos
step_start "Verificacao de pre-requisitos"
require_cmd chmod
require_cmd cp
require_cmd cat
require_cmd mkdir
require_cmd du
require_cmd cut
require_cmd grep
require_dotnet8
step_end "Verificacao de pre-requisitos"

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

# === SS-01: Verificacao de versao e checksum do appimagetool ===
# Versao fixa para garantir reproducibilidade e seguranca (supply chain)
APPIMAGETOOL_VERSION="13"
APPIMAGETOOL_SHA256="df3baf5ca5facbecfc2f3fa6713c29ab9cefa8fd8c1eac5d283b79cab33e4acb"
APPIMAGETOOL_URL="https://github.com/AppImage/AppImageKit/releases/download/${APPIMAGETOOL_VERSION}/obsolete-appimagetool-x86_64.AppImage"
APPIMAGETOOL_LOCAL="$PROJECT_ROOT/.tools/appimagetool-${APPIMAGETOOL_VERSION}-x86_64.AppImage"
APPIMAGETOOL_BIN="appimagetool"

verify_appimagetool() {
    local tool_path
    tool_path="$(command -v appimagetool 2>/dev/null)"

    if [ -z "$tool_path" ]; then
        echo "⚠️  appimagetool não encontrado no PATH"
    fi

    # Verificar se sha256sum esta disponivel
    if ! command -v sha256sum >/dev/null 2>&1; then
        echo "⚠️  sha256sum não encontrado - verificação de checksum ignorada"
        echo "   Instale coreutils para habilitar verificação de integridade"
        if [ -n "$tool_path" ]; then
            APPIMAGETOOL_BIN="$tool_path"
            return 0
        fi
    fi

    # Se houver appimagetool no PATH, validar checksum
    if [ -n "$tool_path" ] && command -v sha256sum >/dev/null 2>&1; then
        echo "Verificando integridade do appimagetool no PATH..."
        local actual_sha
        actual_sha="$(sha256sum "$tool_path" | cut -d' ' -f1)"
        if [ "$actual_sha" = "$APPIMAGETOOL_SHA256" ]; then
            APPIMAGETOOL_BIN="$tool_path"
            echo "✅ appimagetool v$APPIMAGETOOL_VERSION verificado (SHA256 OK)"
            return 0
        fi
    fi

    # Baixar versão fixa se checksum não bate ou não há appimagetool
    mkdir -p "$PROJECT_ROOT/.tools"
    echo "Baixando appimagetool v$APPIMAGETOOL_VERSION..."
    if command -v curl >/dev/null 2>&1; then
        curl -L -o "$APPIMAGETOOL_LOCAL" "$APPIMAGETOOL_URL"
    else
        wget -O "$APPIMAGETOOL_LOCAL" "$APPIMAGETOOL_URL"
    fi
    chmod +x "$APPIMAGETOOL_LOCAL"

    if command -v sha256sum >/dev/null 2>&1; then
        local actual_sha_local
        actual_sha_local="$(sha256sum "$APPIMAGETOOL_LOCAL" | cut -d' ' -f1)"
        if [ "$actual_sha_local" != "$APPIMAGETOOL_SHA256" ]; then
            echo "❌ ERRO: checksum do appimagetool baixado inválido"
            echo "   Esperado: $APPIMAGETOOL_SHA256"
            echo "   Obtido:   $actual_sha_local"
            echo ""
            echo "   Para ignorar (não recomendado), defina: SKIP_APPIMAGETOOL_CHECK=1"
            if [ "${SKIP_APPIMAGETOOL_CHECK:-0}" = "1" ]; then
                echo "⚠️  SKIP_APPIMAGETOOL_CHECK=1 - continuando sem verificação (INSEGURO)"
                APPIMAGETOOL_BIN="$APPIMAGETOOL_LOCAL"
                return 0
            fi
            exit 1
        fi
    fi

    APPIMAGETOOL_BIN="$APPIMAGETOOL_LOCAL"
    echo "✅ appimagetool v$APPIMAGETOOL_VERSION verificado (SHA256 OK)"
}

# Executar verificação
verify_appimagetool

# QB-01: Validar que publish foi bem-sucedido
step_start "Validacao de publish"
if [ ! -d "$PUBLISH_DIR" ]; then
    log ERROR "Diretório publish não encontrado: $PUBLISH_DIR"
    log INFO "Execute primeiro: dotnet publish -c Release -r linux-x64 --self-contained"
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
    log ERROR "Arquivos obrigatórios ausentes no publish:"
    for f in $MISSING_FILES; do
        log ERROR "   - $f"
    done
    exit 1
fi

PUBLISH_FILE_COUNT=$(find "$PUBLISH_DIR" -maxdepth 1 -type f | wc -l)
PUBLISH_SIZE=$(du -sh "$PUBLISH_DIR" | cut -f1)
log OK "Publish validado: $PUBLISH_FILE_COUNT arquivos, $PUBLISH_SIZE"
step_end "Validacao de publish"

# Limpar builds anteriores
step_start "Preparacao de ambiente"
log INFO "Limpando builds anteriores..."
safe_rm "$APPDIR" "$OUTPUT_DIR"

# Criar estrutura AppDir
log INFO "Criando estrutura AppDir..."
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# Copiar binários
log INFO "Copiando binários..."
cp -r "$PUBLISH_DIR/"* "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/Protons.UI"

# Copiar AppRun
log INFO "Copiando AppRun..."
cp "$SCRIPT_DIR/AppRun" "$APPDIR/"
chmod +x "$APPDIR/AppRun"

# Criar desktop file
log INFO "Criando desktop file..."
cat > "$APPDIR/protons-login.desktop" <<EOF
[Desktop Entry]
Version=1.1
Type=Application
Name=Protons - Login
GenericName=Sistema de Login
Comment=Autenticação e gerenciamento de usuários
Exec=Protons.UI
Icon=protons
Terminal=false
Categories=Office;Utility;System;
Keywords=login;auth;authentication;
StartupNotify=true
StartupWMClass=Protons.UI
EOF

cp "$APPDIR/protons-login.desktop" "$APPDIR/usr/share/applications/"

# Copiar ícone (se existir)
ICON_SOURCE="$PROJECT_ROOT/INSTALADOR/ativos/icons/png/256x256.png"
if [ -f "$ICON_SOURCE" ]; then
    log INFO "Copiando ícone..."
    cp "$ICON_SOURCE" "$APPDIR/protons.png"
    cp "$ICON_SOURCE" "$APPDIR/usr/share/icons/hicolor/256x256/apps/protons.png"
else
    log WARN "Ícone 256x256.png não encontrado - AppImage será criado sem ícone"
fi
step_end "Preparacao de ambiente"

# Criar AppImage
step_start "Empacotamento AppImage"
mkdir -p "$OUTPUT_DIR"
# appimagetool (mksquashfs) pode falhar se SOURCE_DATE_EPOCH estiver setado
# pois ela tambem usa flags de timestamp. Para evitar erro, desabilitar apenas
# nesta etapa e restaurar em seguida.
APPIMAGE_SDE="$SOURCE_DATE_EPOCH"
unset SOURCE_DATE_EPOCH
log WARN "SOURCE_DATE_EPOCH desabilitado temporariamente para empacotamento AppImage"
ARCH="$ARCH" run_with_timeout 600 "$APPIMAGETOOL_BIN" "$APPDIR" "$OUTPUT_DIR/Protons-${VERSION}-${ARCH}.AppImage"

APPIMAGE_RESULT=$?
export SOURCE_DATE_EPOCH="$APPIMAGE_SDE"
step_end "Empacotamento AppImage"

if [ $APPIMAGE_RESULT -eq 0 ]; then
    ARTIFACT="$OUTPUT_DIR/Protons-${VERSION}-${ARCH}.AppImage"
    chmod +x "$ARTIFACT"

    # OB-03: Gerar checksum SHA256
    step_start "Geracao de checksum"
    CHECKSUM=$(generate_checksum "$ARTIFACT")
    step_end "Geracao de checksum"

    # Obter tamanho do artefato
    ARTIFACT_SIZE=$(du -h "$ARTIFACT" | cut -f1)

    # OB-02: Adicionar duracoes ao build-info.txt
    append_durations_to_buildinfo "$BUILD_METADATA_DIR/build-info.txt"

    # Finalizar log com resumo
    log_finish "SUCCESS" "$ARTIFACT" "$ARTIFACT_SIZE" "$CHECKSUM"

    log INFO ""
    log INFO "Para executar: ./$ARTIFACT"
else
    log ERROR "Falha ao criar AppImage"
    log_finish "FAILED"
    exit 1
fi
