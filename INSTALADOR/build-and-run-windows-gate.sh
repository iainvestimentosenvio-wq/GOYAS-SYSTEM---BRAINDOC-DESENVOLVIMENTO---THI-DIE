#!/usr/bin/env bash
# build-and-run-windows-gate.sh
# Reconstroi artefatos Windows via VM (EXE sempre, MSI quando stale) e executa run-windows-base-gate.sh
# Uso: bash build-and-run-windows-gate.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
if PROJECT_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null)"; then
  :
else
  PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd -P)"
fi
if [ -d "$PROJECT_ROOT/INSTALADOR" ]; then
  INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
else
  INSTALADOR_ROOT="$PROJECT_ROOT"
  PROJECT_ROOT="$(cd "$INSTALADOR_ROOT/.." && pwd -P)"
fi
QGA_LIB="$INSTALADOR_ROOT/comum/scripts/windows-qga-lib.sh"
CANARY_SCRIPT="$INSTALADOR_ROOT/run-windows-canary.sh"

usage() {
  cat <<'EOF'
Uso:
  bash build-and-run-windows-gate.sh [opcoes]

Opcoes:
  -h, --help                    Exibe ajuda e sai

Variaveis de ambiente:
  BUILD_VM                      VM para build na etapa inicial (default: win10-lite)
  QGA_PAYLOAD_TRANSPORT_MODE    iso-strict|iso|qga|auto para o gate base (default: iso-strict)
  PROTONS_SIGNATURE_PROFILE     technical|production para o gate base (default: technical)
  PROTONS_SKIP_CANARY           1 para pular canario automatico (default: 0)

Observacoes:
  - Rebuild do EXE sempre ocorre automaticamente na VM.
  - Rebuild do MSI ocorre automaticamente na VM quando freshness detectar stale.
  - Requer dotnet no host Linux para gerar publish win-x64 usado no bundle.
EOF
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

[ -f "$QGA_LIB" ] || { echo "ERRO: QGA lib nao encontrada: $QGA_LIB" >&2; exit 1; }
source "$QGA_LIB"

BUILD_VM="${BUILD_VM:-win10-lite}"
export QGA_PAYLOAD_TRANSPORT_MODE="${QGA_PAYLOAD_TRANSPORT_MODE:-iso-strict}"
export PROTONS_SIGNATURE_PROFILE="${PROTONS_SIGNATURE_PROFILE:-technical}"
PROTONS_SKIP_CANARY="${PROTONS_SKIP_CANARY:-0}"
BUILD_RUNID="BUILD-$(date -u +%Y%m%d%H%M%S)"
BUILD_DIR="$INSTALADOR_ROOT/saida/build-vm-$BUILD_RUNID"
GUEST_BUILD_ZIP="C:\\Windows\\Temp\\protons-build-${BUILD_RUNID}.zip"
GUEST_BUILD_ROOT="C:\\protons-build\\${BUILD_RUNID}"

VERSION="$(awk -F= '/^VERSION=/{gsub(/"/,"",$2); print $2; exit}' "$INSTALADOR_ROOT/comum/version.env")"
[ -n "$VERSION" ] || { echo "ERRO: VERSION nao definida em comum/version.env" >&2; exit 1; }

HOST_MSI="$INSTALADOR_ROOT/saida/windows/Protons-${VERSION}-x64.msi"
HOST_EXE="$INSTALADOR_ROOT/saida/windows/ProtonsSetup-${VERSION}.exe"
HOST_PUBLISH_DIR="$PROJECT_ROOT/Login/Protons.UI/bin/Release/net8.0/win-x64/publish"
HOST_RUNTIMECONFIG="$PROJECT_ROOT/Login/Protons.UI/bin/Release/net8.0/win-x64/Protons.UI.runtimeconfig.json"
NEED_MSI_REBUILD=0
PREFLIGHT_LOG="$BUILD_DIR/preflight-freshness.log"

mkdir -p "$BUILD_DIR"

echo "0. Preflight de frescor (informativo)..."
if ! preflight_out="$(bash "$INSTALADOR_ROOT/testes/windows/test-artifact-freshness.sh" --artifact msi 2>&1)"; then
  printf '%s\n' "$preflight_out" > "$PREFLIGHT_LOG"
  NEED_MSI_REBUILD=1
  echo "   MSI desatualizado detectado: rebuild automatico na VM sera executado."
  echo "   Detalhes do preflight em: $PREFLIGHT_LOG"
else
  printf '%s\n' "$preflight_out" > "$PREFLIGHT_LOG"
  echo "   MSI ja estava atualizado."
fi
echo ""

echo "╔═══════════════════════════════════════════════════════════════════╗"
echo "║   BUILD EXE NA VM + VALIDACAO WINDOWS BASE GATE                  ║"
echo "╚═══════════════════════════════════════════════════════════════════╝"
echo ""
echo "  VM de build : $BUILD_VM"
echo "  Versao      : $VERSION"
echo "  Run ID      : $BUILD_RUNID"
echo "  Rebuild MSI : $NEED_MSI_REBUILD"
echo "  Log dir     : $BUILD_DIR"
echo ""

# ---------------------------------------------------------------------------
# 1. Garantir VM ligada
# ---------------------------------------------------------------------------
echo "1. Iniciando VM $BUILD_VM..."
state="$(sg libvirt -c "virsh domstate $BUILD_VM" 2>/dev/null | tr -d '\r' || true)"
if [ "$state" != "running" ]; then
  sg libvirt -c "virsh start $BUILD_VM" >/dev/null
  echo "   VM iniciada, aguardando boot..."
  sleep 10
else
  echo "   VM ja estava rodando."
fi

# ---------------------------------------------------------------------------
# 2. Aguardar QGA
# ---------------------------------------------------------------------------
echo "2. Aguardando QGA (max 180s)..."
started="$(date +%s)"
while :; do
  now="$(date +%s)"
  elapsed=$((now - started))
  if [ "$elapsed" -ge 180 ]; then
    echo ""
    echo "ERRO: QGA nao respondeu em 180s para $BUILD_VM." >&2
    echo "  Verifique se o qemu-guest-agent esta instalado e rodando na VM." >&2
    exit 1
  fi
  if qga_ping "$BUILD_VM" >/dev/null 2>&1; then
    echo "   QGA OK (${elapsed}s)"
    break
  fi
  printf "."
  sleep 4
done
echo ""

# ---------------------------------------------------------------------------
# 3. Preparar publish .NET no host Linux (fonte para MSI/Inno)
# ---------------------------------------------------------------------------
echo "3. Preparando publish .NET no host..."
if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERRO: dotnet nao encontrado no host Linux." >&2
  echo "Instale .NET SDK 8.x para gerar o payload win-x64 antes do build na VM." >&2
  exit 1
fi

host_publish_win64() {
  local use_no_build="${1:-1}"
  (
    set -euo pipefail
    cd "$PROJECT_ROOT"

    local ui_csproj="Login/Protons.UI/Protons.UI.csproj"
    local common_args=(
      -c Release
      -r win-x64
      --self-contained true
      -p:PublishSingleFile=false
      -p:IncludeNativeLibrariesForSelfExtract=true
      -p:ContinuousIntegrationBuild=true
      -p:Deterministic=true
      -p:DebugType=None
      -p:DebugSymbols=false
      -m:1
    )

    dotnet restore "$ui_csproj" -r win-x64 -m:1
    dotnet build "$ui_csproj" "${common_args[@]}"
    [ -f "$HOST_RUNTIMECONFIG" ] || {
      echo "runtimeconfig_missing_after_build: $HOST_RUNTIMECONFIG" >&2
      exit 1
    }

    if [ "$use_no_build" = "1" ]; then
      dotnet publish "$ui_csproj" "${common_args[@]}" --no-build
    else
      dotnet publish "$ui_csproj" "${common_args[@]}"
    fi
  )
}

clean_host_publish_intermediates() {
  local core_proj="$PROJECT_ROOT/Login/Protons.Core/Protons.Core.csproj"
  local infra_proj="$PROJECT_ROOT/Login/Protons.Infrastructure/Protons.Infrastructure.csproj"
  local ui_proj="$PROJECT_ROOT/Login/Protons.UI/Protons.UI.csproj"

  dotnet clean "$core_proj" -c Release || true
  dotnet clean "$infra_proj" -c Release || true
  dotnet clean "$ui_proj" -c Release || true

  rm -rf \
    "$PROJECT_ROOT/Login/Protons.Core/bin" \
    "$PROJECT_ROOT/Login/Protons.Core/obj" \
    "$PROJECT_ROOT/Login/Protons.Infrastructure/bin" \
    "$PROJECT_ROOT/Login/Protons.Infrastructure/obj" \
    "$PROJECT_ROOT/Login/Protons.UI/bin" \
    "$PROJECT_ROOT/Login/Protons.UI/obj" \
    "$HOST_PUBLISH_DIR"
}

requires_full_publish_retry() {
  local log_file="$1"
  grep -Eqi "runtimeconfig_missing_after_build|CS0006|MSB3030|metadata file .* could not be found|Reference assemblies|ref assembly|ref/" "$log_file"
}

classify_publish_failure() {
  local log_file="$1"
  if grep -Eqi "A compatible \\.NET SDK was not found|No \\.NET SDKs were found|NETSDK|dotnet: command not found" "$log_file"; then
    printf '%s' "missing_sdk"
    return 0
  fi
  if grep -q "/home/" "$log_file" && grep -q "/srv/" "$log_file"; then
    printf '%s' "path_alias"
    return 0
  fi
  if grep -Eqi "runtimeconfig_missing_after_build|CS0006|MSB3030|metadata file .* could not be found|Reference assemblies|ref assembly|ref/" "$log_file"; then
    printf '%s' "stale_intermediate"
    return 0
  fi
  printf '%s' "unknown_publish_failure"
}

PUBLISH_LOG="$BUILD_DIR/host-dotnet-publish.log"
if ! host_publish_win64 1 >"$PUBLISH_LOG" 2>&1; then
  echo "WARN: dotnet publish (tentativa 1 --no-build) falhou; limpeza deterministica e retry..." | tee -a "$PUBLISH_LOG" >&2
  clean_host_publish_intermediates >>"$PUBLISH_LOG" 2>&1 || true
  if ! host_publish_win64 1 >>"$PUBLISH_LOG" 2>&1; then
    if requires_full_publish_retry "$PUBLISH_LOG"; then
      echo "WARN: detectado erro de runtimeconfig/ref assembly; retry final sem --no-build..." | tee -a "$PUBLISH_LOG" >&2
      clean_host_publish_intermediates >>"$PUBLISH_LOG" 2>&1 || true
      if ! host_publish_win64 0 >>"$PUBLISH_LOG" 2>&1; then
        PUBLISH_FAILURE_CLASS="$(classify_publish_failure "$PUBLISH_LOG")"
        echo "ERRO: dotnet publish no host falhou apos fallback sem --no-build." >&2
        echo "  Causa classificada: $PUBLISH_FAILURE_CLASS" >&2
        echo "  Veja: $PUBLISH_LOG" >&2
        exit 1
      fi
    else
      PUBLISH_FAILURE_CLASS="$(classify_publish_failure "$PUBLISH_LOG")"
      echo "ERRO: dotnet publish no host falhou apos retry com --no-build." >&2
      echo "  Causa classificada: $PUBLISH_FAILURE_CLASS" >&2
      echo "  Veja: $PUBLISH_LOG" >&2
      exit 1
    fi
  fi
fi

[ -d "$HOST_PUBLISH_DIR" ] || {
  echo "ERRO: diretorio publish nao encontrado apos dotnet publish: $HOST_PUBLISH_DIR" >&2
  exit 1
}
echo "   Publish pronto em: $HOST_PUBLISH_DIR"
echo ""

# ---------------------------------------------------------------------------
# 4. Montar bundle de build
# ---------------------------------------------------------------------------
echo "4. Montando bundle de build..."
BUNDLE_SRC="$BUILD_DIR/bundle-src"
BUNDLE_ZIP="$BUILD_DIR/protons-build-${BUILD_RUNID}.zip"

mkdir -p \
  "$BUNDLE_SRC/INSTALADOR/windows/scripts" \
  "$BUNDLE_SRC/INSTALADOR/windows/innosetup" \
  "$BUNDLE_SRC/INSTALADOR/windows/wix" \
  "$BUNDLE_SRC/INSTALADOR/windows/ativos" \
  "$BUNDLE_SRC/INSTALADOR/windows/ativos/installer" \
  "$BUNDLE_SRC/INSTALADOR/comum/scripts" \
  "$BUNDLE_SRC/INSTALADOR/saida/windows" \
  "$BUNDLE_SRC/Login/Protons.UI/Assets/Brand" \
  "$BUNDLE_SRC/Login/Protons.UI/bin/Release/net8.0/win-x64/publish"

# Fontes do instalador
cp "$INSTALADOR_ROOT/windows/innosetup/protons-setup.iss" \
   "$BUNDLE_SRC/INSTALADOR/windows/innosetup/"
cp "$INSTALADOR_ROOT/windows/scripts/build-msi.ps1" \
   "$BUNDLE_SRC/INSTALADOR/windows/scripts/"
cp "$INSTALADOR_ROOT/windows/scripts/build-inno.ps1" \
   "$BUNDLE_SRC/INSTALADOR/windows/scripts/"
cp "$INSTALADOR_ROOT/windows/scripts/prepare-inno-ux-assets.ps1" \
   "$BUNDLE_SRC/INSTALADOR/windows/scripts/"
cp "$INSTALADOR_ROOT/windows/wix/Product.wxs" \
   "$BUNDLE_SRC/INSTALADOR/windows/wix/"
cp "$INSTALADOR_ROOT/windows/wix/Components.wxs" \
   "$BUNDLE_SRC/INSTALADOR/windows/wix/"
cp "$INSTALADOR_ROOT/windows/wix/Features.wxs" \
   "$BUNDLE_SRC/INSTALADOR/windows/wix/"
cp "$INSTALADOR_ROOT/comum/version.env" \
   "$BUNDLE_SRC/INSTALADOR/comum/version.env"
cp "$INSTALADOR_ROOT/comum/scripts/Log-Utils.ps1" \
   "$BUNDLE_SRC/INSTALADOR/comum/scripts/" 2>/dev/null || true
cp "$INSTALADOR_ROOT/comum/scripts/Init-Dirs.ps1" \
   "$BUNDLE_SRC/INSTALADOR/comum/scripts/" 2>/dev/null || true
cp "$INSTALADOR_ROOT/windows/ativos/logo_protons.ico" \
   "$BUNDLE_SRC/INSTALADOR/windows/ativos/" 2>/dev/null || true

# Assets de marca necessarios para prepare-inno-ux-assets.ps1
cp "$PROJECT_ROOT/Login/Protons.UI/Assets/Brand/login_bg.png" \
   "$BUNDLE_SRC/Login/Protons.UI/Assets/Brand/"
cp "$PROJECT_ROOT/Login/Protons.UI/Assets/Brand/logo_protons.png" \
   "$BUNDLE_SRC/Login/Protons.UI/Assets/Brand/"
cp -a "$HOST_PUBLISH_DIR/." \
   "$BUNDLE_SRC/Login/Protons.UI/bin/Release/net8.0/win-x64/publish/"

# Empacotar
(cd "$BUNDLE_SRC" && zip -qr "$BUNDLE_ZIP" .)
BUNDLE_SIZE="$(wc -c < "$BUNDLE_ZIP" | tr -d ' ')"
echo "   Bundle criado: ${BUNDLE_SIZE} bytes ($(( BUNDLE_SIZE / 1024 )) KB)"
echo ""

# ---------------------------------------------------------------------------
# 5. Upload do bundle para a VM
# ---------------------------------------------------------------------------
echo "5. Fazendo upload do bundle para $BUILD_VM..."
if ! QGA_WRITE_CHUNK_SIZE="${QGA_WRITE_CHUNK_SIZE:-65536}" \
QGA_WRITE_PROGRESS_EVERY_MB="${QGA_WRITE_PROGRESS_EVERY_MB:-1}" \
qga_write_file "$BUILD_VM" "$BUNDLE_ZIP" "$GUEST_BUILD_ZIP" \
  2>&1 | tee "$BUILD_DIR/upload.log"; then
  echo "ERRO: upload falhou. Veja: $BUILD_DIR/upload.log" >&2
  exit 1
fi
echo "   Upload concluido."
echo ""

# ---------------------------------------------------------------------------
# 6. Garantir Inno Setup >= 6.3 na VM (necessario para WizardStyle dynamic)
#    6.2.2 rejeita "WizardStyle=modern dynamic" como invalido.
#    Se versao for antiga, baixa e instala o instalador oficial silenciosamente.
# ---------------------------------------------------------------------------
echo "6. Verificando versao do Inno Setup na VM..."

INNO_CHECK_PS='$reg = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" -ErrorAction SilentlyContinue
if (-not $reg) { $reg = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" -ErrorAction SilentlyContinue }
$ver = if ($reg) { $reg.DisplayVersion } else { "0.0.0" }
$minVer = [version]"6.3.0"
$curVer = try { [version]$ver } catch { [version]"0.0.0" }
"version=$ver|needs_update=" + ($curVer -lt $minVer).ToString().ToLower()'

ENCODED_CHECK=$(python3 -c "import base64,sys; print(base64.b64encode(sys.argv[1].encode('utf-16le')).decode('ascii'))" "$INNO_CHECK_PS")
PID_CHECK=$(qga_guest_exec "$BUILD_VM" "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" \
  "-NoProfile" "-ExecutionPolicy" "Bypass" "-EncodedCommand" "$ENCODED_CHECK" 2>/dev/null)
qga_wait_exec "$BUILD_VM" "$PID_CHECK" 120 >/dev/null 2>&1
INNO_STATUS="$QGA_LAST_STDOUT"
echo "   $INNO_STATUS"

if echo "$INNO_STATUS" | grep -q "needs_update=true"; then
  echo "   Inno Setup desatualizado. Instalando versao atual via download..."

  INNO_INSTALL_PS='$ErrorActionPreference = "Stop"
$installer = "$env:TEMP\innosetup-latest.exe"
Write-Output "downloading_inno_setup"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
(New-Object System.Net.WebClient).DownloadFile("https://jrsoftware.org/download.php/is.exe", $installer)
Write-Output "download_ok size=$((Get-Item $installer).Length)"
Write-Output "installing_silent"
$proc = Start-Process -FilePath $installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-" -Wait -PassThru
Write-Output "install_exitcode=$($proc.ExitCode)"
if ($proc.ExitCode -ne 0) { throw "inno_install_failed: exitcode=" + $proc.ExitCode }
$reg = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" -ErrorAction SilentlyContinue
Write-Output "installed_version=$($reg.DisplayVersion)"'

  ENCODED_INST=$(python3 -c "import base64,sys; print(base64.b64encode(sys.argv[1].encode('utf-16le')).decode('ascii'))" "$INNO_INSTALL_PS")
  PID_INST=$(qga_guest_exec "$BUILD_VM" "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" \
    "-NoProfile" "-ExecutionPolicy" "Bypass" "-EncodedCommand" "$ENCODED_INST" 2>/dev/null)

  echo "   PID instalacao: $PID_INST (aguardando até 5 min)..."
  if ! qga_wait_exec "$BUILD_VM" "$PID_INST" 300; then
    echo "ERRO: instalacao do Inno Setup falhou (exitcode=${QGA_LAST_EXITCODE:-?})." >&2
    echo "  $QGA_LAST_STDOUT" >&2
    exit 1
  fi
  echo "   $QGA_LAST_STDOUT"
  echo "   Inno Setup atualizado."
else
  echo "   Inno Setup OK, sem atualizacao necessaria."
fi
echo ""

# ---------------------------------------------------------------------------
# 7. Rebuild de artefatos na VM (MSI quando stale + EXE)
# ---------------------------------------------------------------------------
echo "7. Rebuild de artefatos na VM (pode levar alguns minutos)..."

# Ler versao e publisher do version.env para passar ao ISCC
_VERSION="$VERSION"
_PUBLISHER="$(awk -F= '/^PUBLISHER=/{gsub(/"/,"",$2); print $2; exit}' "$INSTALADOR_ROOT/comum/version.env")"

BUILD_PS="\$ErrorActionPreference = 'Stop'
\$zip  = '${GUEST_BUILD_ZIP}'
\$base = '${GUEST_BUILD_ROOT}'
\$appVersion = '${_VERSION}'
\$appPublisher = '${_PUBLISHER}'
\$rebuildMsi = ('${NEED_MSI_REBUILD}' -eq '1')

# Extrair bundle
if (-not (Test-Path \$zip)) { throw 'bundle_zip_not_found: ' + \$zip }
if (Test-Path \$base) { Remove-Item -Path \$base -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path \$base -Force | Out-Null
Expand-Archive -Path \$zip -DestinationPath \$base -Force
Write-Output 'bundle_extracted=OK'

# Criar pasta de saida
\$outputDir = Join-Path \$base 'INSTALADOR\\saida\\windows'
New-Item -ItemType Directory -Path \$outputDir -Force | Out-Null

# Rebuild MSI quando stale (sem acao manual)
\$msiBuildScript = Join-Path \$base 'INSTALADOR\\windows\\scripts\\build-msi.ps1'
if (\$rebuildMsi) {
  \$dotnetReady = \$false
  \$dotnetCandidates = @('dotnet.exe', 'C:\\Program Files\\dotnet\\dotnet.exe', 'C:\\dotnet\\dotnet.exe')
  foreach (\$candidate in \$dotnetCandidates) {
    try {
      \$list = & \$candidate --list-sdks 2>\$null
      if (\$LASTEXITCODE -eq 0 -and \$list -match '^8\\.') {
        \$dotnetReady = \$true
        \$candidatePath = [System.IO.Path]::GetDirectoryName((Get-Command \$candidate -ErrorAction SilentlyContinue).Source)
        if (-not \$candidatePath) {
          \$candidatePath = [System.IO.Path]::GetDirectoryName(\$candidate)
        }
        if (\$candidatePath -and -not (\$env:PATH -like \"*\$candidatePath*\")) {
          \$env:PATH = \"\$candidatePath;\$env:PATH\"
        }
        break
      }
    } catch {
    }
  }

  if (-not \$dotnetReady) {
    Write-Output 'dotnet_sdk8_install=START'
    \$dotnetInstallDir = 'C:\\dotnet'
    \$dotnetInstaller = Join-Path \$env:TEMP 'dotnet-install.ps1'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    (New-Object System.Net.WebClient).DownloadFile('https://dot.net/v1/dotnet-install.ps1', \$dotnetInstaller)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File \$dotnetInstaller -Channel 8.0 -InstallDir \$dotnetInstallDir -NoPath
    if (\$LASTEXITCODE -ne 0) { throw 'dotnet_sdk8_install_failed: exitcode=' + \$LASTEXITCODE }
    if (-not (Test-Path (Join-Path \$dotnetInstallDir 'dotnet.exe'))) { throw 'dotnet_sdk8_not_found_after_install' }
    \$env:PATH = \"\$dotnetInstallDir;\$env:PATH\"
    \$dotnetCheck = & (Join-Path \$dotnetInstallDir 'dotnet.exe') --list-sdks 2>\$null
    if (\$LASTEXITCODE -ne 0 -or \$dotnetCheck -notmatch '^8\\.') { throw 'dotnet_sdk8_validation_failed' }
    Write-Output 'dotnet_sdk8_install=OK'
  } else {
    Write-Output 'dotnet_sdk8_present=OK'
  }

  if (-not (Test-Path \$msiBuildScript)) { throw 'build_msi_script_not_found: ' + \$msiBuildScript }
  Write-Output 'running_build_msi'
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File \$msiBuildScript -SkipPublish
  if (\$LASTEXITCODE -ne 0) { throw 'build_msi_failed: exitcode=' + \$LASTEXITCODE }
  \$msi = Join-Path \$outputDir \"Protons-\$appVersion-x64.msi\"
  if (-not (Test-Path \$msi)) { throw 'msi_not_found_after_build: ' + \$msi }
  \$msiSize = (Get-Item \$msi).Length
  Write-Output \"msi_ready=\$msi size=\$msiSize\"
} else {
  Write-Output 'running_build_msi=SKIP (already fresh)'
}

# Gerar assets visuais (prepare-inno-ux-assets.ps1)
\$prepareScript = Join-Path \$base 'INSTALADOR\\windows\\scripts\\prepare-inno-ux-assets.ps1'
if (Test-Path \$prepareScript) {
  Write-Output 'running_prepare_ux_assets'
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File \$prepareScript -ProjectRoot \$base
  if (\$LASTEXITCODE -ne 0) { throw 'prepare_ux_assets_failed: exitcode=' + \$LASTEXITCODE }
  Write-Output 'prepare_ux_assets=OK'
} else {
  Write-Output 'prepare_ux_assets_script_not_found_skipping'
}

# Localizar ISCC
\$isccPaths = @(
  'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe',
  'C:\\Program Files\\Inno Setup 6\\ISCC.exe',
  \"\$env:LOCALAPPDATA\\Programs\\Inno Setup 6\\ISCC.exe\"
)
\$iscc = \$null
foreach (\$p in \$isccPaths) {
  if (Test-Path \$p) { \$iscc = \$p; break }
}
if (-not \$iscc) { throw 'ISCC.exe nao encontrado' }
Write-Output \"iscc_found=\$iscc\"

# Compilar
\$issFile = Join-Path \$base 'INSTALADOR\\windows\\innosetup\\protons-setup.iss'
if (-not (Test-Path \$issFile)) { throw 'iss_file_not_found: ' + \$issFile }
Write-Output 'running_iscc'
& \$iscc \"/DAppVersion=\$appVersion\" \"/DAppPublisher=\$appPublisher\" \"/O\$outputDir\" \$issFile
if (\$LASTEXITCODE -ne 0) { throw 'iscc_failed: exitcode=' + \$LASTEXITCODE }
Write-Output 'iscc=OK'

# Verificar EXE gerado
\$exe = Join-Path \$outputDir \"ProtonsSetup-\$appVersion.exe\"
if (-not (Test-Path \$exe)) {
  # ISCC pode ter colocado em subpasta Output/
  \$exeAlt = Join-Path \$outputDir \"Output\\ProtonsSetup-\$appVersion.exe\"
  if (Test-Path \$exeAlt) {
    Copy-Item \$exeAlt \$exe -Force
    Write-Output 'exe_moved_from_Output_subdir'
  } else {
    throw 'exe_not_found_after_build: ' + \$exe
  }
}
\$size = (Get-Item \$exe).Length
Write-Output \"exe_ready=\$exe size=\$size\""

ENCODED="$(python3 -c "
import base64, sys
raw = sys.argv[1]
print(base64.b64encode(raw.encode('utf-16le')).decode('ascii'))
" "$BUILD_PS")"

EXEC_PID="$(qga_guest_exec "$BUILD_VM" \
  "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" \
  "-NoProfile" "-ExecutionPolicy" "Bypass" "-EncodedCommand" "$ENCODED" \
  2>"$BUILD_DIR/guest-exec.err.log")" || {
  echo "ERRO: qga_guest_exec falhou. Veja: $BUILD_DIR/guest-exec.err.log" >&2
  exit 1
}
echo "   PID guest: $EXEC_PID"

if ! qga_wait_exec "$BUILD_VM" "$EXEC_PID" 600; then
  {
    echo "STDOUT: ${QGA_LAST_STDOUT:-}"
    echo "STDERR: ${QGA_LAST_STDERR:-}"
    echo "EXITCODE: ${QGA_LAST_EXITCODE:-}"
  } > "$BUILD_DIR/build-result.log"
  echo "ERRO: rebuild de artefatos na VM falhou ou timeout (exitcode=${QGA_LAST_EXITCODE:-?})." >&2
  echo "  STDOUT: ${QGA_LAST_STDOUT:-}" >&2
  echo "  STDERR: ${QGA_LAST_STDERR:-}" >&2
  echo "  Log: $BUILD_DIR/build-result.log" >&2
  exit 1
fi

{
  echo "EXITCODE: ${QGA_LAST_EXITCODE:-}"
  echo "STDOUT: ${QGA_LAST_STDOUT:-}"
  echo "STDERR: ${QGA_LAST_STDERR:-}"
} > "$BUILD_DIR/build-result.log"

if [ "${QGA_LAST_EXITCODE:-1}" != "0" ]; then
  echo "ERRO: rebuild de artefatos na VM retornou exitcode=${QGA_LAST_EXITCODE:-?}" >&2
  echo "  STDOUT: ${QGA_LAST_STDOUT:-}" >&2
  echo "  STDERR: ${QGA_LAST_STDERR:-}" >&2
  exit 1
fi
echo "   Rebuild na VM concluido (exitcode=0)."
echo ""

# ---------------------------------------------------------------------------
# 8. Baixar artefatos para o host
# ---------------------------------------------------------------------------
echo "8. Baixando artefatos para o host..."
if [ "$NEED_MSI_REBUILD" = "1" ]; then
  GUEST_MSI="${GUEST_BUILD_ROOT}\\INSTALADOR\\saida\\windows\\Protons-${VERSION}-x64.msi"
  qga_read_file "$BUILD_VM" "$GUEST_MSI" "$BUILD_DIR/Protons-${VERSION}-x64.msi" \
    >"$BUILD_DIR/download-msi.log" 2>&1 || {
    echo "ERRO: download do MSI falhou. Veja: $BUILD_DIR/download-msi.log" >&2
    exit 1
  }

  if [ ! -s "$BUILD_DIR/Protons-${VERSION}-x64.msi" ]; then
    echo "ERRO: MSI baixado esta vazio ou ausente." >&2
    exit 1
  fi

  cp "$BUILD_DIR/Protons-${VERSION}-x64.msi" "$HOST_MSI"
  sha256sum "$HOST_MSI" > "${HOST_MSI}.sha256"
  MSI_SIZE="$(wc -c < "$HOST_MSI" | tr -d ' ')"
  echo "   MSI salvo: $HOST_MSI"
  echo "   Tamanho  : $(( MSI_SIZE / 1024 )) KB"
  echo "   SHA256   : $(cut -d' ' -f1 "${HOST_MSI}.sha256")"
else
  echo "   MSI nao precisou de rebuild (ja estava fresco)."
fi

GUEST_EXE="${GUEST_BUILD_ROOT}\\INSTALADOR\\saida\\windows\\ProtonsSetup-${VERSION}.exe"

qga_read_file "$BUILD_VM" "$GUEST_EXE" "$BUILD_DIR/ProtonsSetup-${VERSION}.exe" \
  >"$BUILD_DIR/download-exe.log" 2>&1 || {
  echo "ERRO: download do EXE falhou. Veja: $BUILD_DIR/download-exe.log" >&2
  exit 1
}

if [ ! -s "$BUILD_DIR/ProtonsSetup-${VERSION}.exe" ]; then
  echo "ERRO: EXE baixado esta vazio ou ausente." >&2
  exit 1
fi

cp "$BUILD_DIR/ProtonsSetup-${VERSION}.exe" "$HOST_EXE"
sha256sum "$HOST_EXE" > "${HOST_EXE}.sha256"
EXE_SIZE="$(wc -c < "$HOST_EXE" | tr -d ' ')"
echo "   EXE salvo: $HOST_EXE"
echo "   Tamanho  : $(( EXE_SIZE / 1024 )) KB"
echo "   SHA256   : $(cut -d' ' -f1 "${HOST_EXE}.sha256")"
echo ""

# ---------------------------------------------------------------------------
# 9. Verificar freshness check (sem bypass por timestamp)
# ---------------------------------------------------------------------------
echo "9. Verificando freshness check..."
if bash "$INSTALADOR_ROOT/testes/windows/test-artifact-freshness.sh"; then
  echo ""
else
  echo ""
  echo "ERRO: freshness check falhou apos rebuild automatico na VM." >&2
  echo "Verifique logs: $BUILD_DIR/build-result.log" >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# 10. Executar canario (default)
# ---------------------------------------------------------------------------
if [ "$PROTONS_SKIP_CANARY" = "1" ]; then
  echo "10. Canary automatico ignorado via PROTONS_SKIP_CANARY=1."
  echo ""
else
  [ -x "$CANARY_SCRIPT" ] || {
    echo "ERRO: script de canario ausente ou sem permissao de execucao: $CANARY_SCRIPT" >&2
    exit 1
  }
  echo "10. Executando canario automatico..."
  if ! PROTONS_SIGNATURE_PROFILE="$PROTONS_SIGNATURE_PROFILE" bash "$CANARY_SCRIPT"; then
    echo "ERRO: canario falhou. Gate base bloqueado por fail-fast." >&2
    exit 1
  fi
  echo "   Canary concluido com sucesso."
  echo ""
fi

# ---------------------------------------------------------------------------
# 11. Executar run-windows-base-gate.sh
# ---------------------------------------------------------------------------
echo "11. Iniciando run-windows-base-gate.sh..."
echo ""
exec bash "$INSTALADOR_ROOT/run-windows-base-gate.sh"
