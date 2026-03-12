# Build EXE usando Inno Setup
# Requer: Inno Setup 6.7+ instalado
# Opcional: Certificado Code Signing para assinatura

$ErrorActionPreference = "Stop"

function Test-RunningOnWindows {
    try {
        return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows
        )
    } catch {
        return ($env:OS -eq 'Windows_NT')
    }
}

if (-not (Test-RunningOnWindows)) {
    Write-Host "❌ ERRO: build-inno.ps1 deve ser executado em Windows." -ForegroundColor Red
    Write-Host "   Motivo: Inno Setup (ISCC.exe) e toolchain de assinatura sao Windows-only." -ForegroundColor Yellow
    Write-Host "   Acao: execute este script dentro de ambiente Windows (host/VM)." -ForegroundColor Gray
    exit 1
}

# Caminhos (definir antes de carregar log utils)
$ScriptDir = Split-Path -Parent $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

# OB-01: Carregar utilitarios de log
$LogUtilsPath = Join-Path $ProjectRoot "INSTALADOR\comum\scripts\Log-Utils.ps1"
if (Test-Path $LogUtilsPath) {
    . $LogUtilsPath
    Initialize-BuildLog -ScriptName "build-inno" -LogDir (Join-Path $ProjectRoot "INSTALADOR\saida\logs")
} else {
    # Fallback se Log-Utils nao existir
    function Write-Log { param($Level, $Message); Write-Host "[$Level] $Message" }
    function Start-BuildStep { param($StepName); Write-Host "[STEP] Iniciando: $StepName" }
    function Complete-BuildStep { param($StepName); Write-Host "[OK] $StepName concluido" }
    function New-FileChecksum { param($FilePath); return $null }
    function Complete-BuildLog { param($Status, $Artifact, $ArtifactSize, $Checksum) }
    function Add-DurationsToBuildInfo { param($BuildInfoPath) }
}

Write-Log INFO "=== Build Protons EXE (Inno Setup) ==="

# Verificar Inno Setup
Start-BuildStep -StepName "Verificacao de pre-requisitos"
$MinimumInnoVersion = [version]'6.7.0.0'
$InnoPath = $null
$InnoPaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe"
)

foreach ($path in $InnoPaths) {
    if (Test-Path $path) {
        $InnoPath = $path
        break
    }
}

if (-not $InnoPath) {
    # Tentar no PATH
    $inPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($inPath) {
        $InnoPath = $inPath.Source
    }
}

if (-not $InnoPath) {
    Write-Host "❌ ERRO: Inno Setup não encontrado" -ForegroundColor Red
    Write-Host "   Instale de: https://jrsoftware.org/isinfo.php" -ForegroundColor Gray
    exit 1
}

$InnoDetectedVersion = $null
$InnoVersionRaw = $null
try {
    $innoInfo = Get-Item -LiteralPath $InnoPath
    $InnoVersionRaw = if ($innoInfo.VersionInfo.ProductVersion) { $innoInfo.VersionInfo.ProductVersion } else { $innoInfo.VersionInfo.FileVersion }
    if ($InnoVersionRaw -match '(\d+\.\d+\.\d+(\.\d+)?)') {
        $InnoDetectedVersion = [version]$Matches[1]
    }
} catch {
}

if (-not $InnoDetectedVersion) {
    Write-Host "❌ ERRO: nao foi possivel detectar a versao do Inno Setup em $InnoPath" -ForegroundColor Red
    Write-Host "   Versao minima exigida: $MinimumInnoVersion" -ForegroundColor Gray
    exit 1
}

if ($InnoDetectedVersion -lt $MinimumInnoVersion) {
    Write-Host "❌ ERRO: Inno Setup desatualizado ($InnoDetectedVersion)." -ForegroundColor Red
    Write-Host "   Versao minima exigida para UX dinamica: $MinimumInnoVersion" -ForegroundColor Gray
    Write-Host "   Atualize em: https://jrsoftware.org/isinfo.php" -ForegroundColor Gray
    exit 1
}

Write-Log OK "Inno Setup encontrado: $InnoPath"
Write-Log INFO "Versao detectada do Inno Setup: $InnoDetectedVersion (raw: $InnoVersionRaw)"
Complete-BuildStep -StepName "Verificacao de pre-requisitos"

# Caminhos
$IssFile = Join-Path $ProjectRoot "INSTALADOR\windows\innosetup\protons-setup.iss"
$PrepareUxAssetsScript = Join-Path $ProjectRoot "INSTALADOR\windows\scripts\prepare-inno-ux-assets.ps1"
$WizardBackLightPath = Join-Path $ProjectRoot "INSTALADOR\windows\ativos\installer\wizard_back_light.png"
$WizardBackDarkPath = Join-Path $ProjectRoot "INSTALADOR\windows\ativos\installer\wizard_back_dark.png"
$WizardSmallLogoLightPath = Join-Path $ProjectRoot "INSTALADOR\windows\ativos\installer\wizard_small_logo_light.png"
$WizardSmallLogoDarkPath = Join-Path $ProjectRoot "INSTALADOR\windows\ativos\installer\wizard_small_logo_dark.png"

Write-Log INFO "Diretório do projeto: $ProjectRoot"
Write-Log INFO "Arquivo .iss: $IssFile"

# QB-03: Inicializar estrutura de diretórios de saída
$InitDirsScript = Join-Path $ProjectRoot "INSTALADOR\comum\scripts\Init-Dirs.ps1"
if (Test-Path $InitDirsScript) {
    . $InitDirsScript -ProjectRoot $ProjectRoot
    $OutputDir = $OutputWindows
} else {
    Write-Host "⚠️  Init-Dirs.ps1 não encontrado, criando diretórios manualmente" -ForegroundColor Yellow
    $OutputDir = "$ProjectRoot\INSTALADOR\saida\windows"
    $OutputMetadata = "$ProjectRoot\INSTALADOR\saida\metadata"
    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
    if (-not (Test-Path $OutputMetadata)) { New-Item -ItemType Directory -Path $OutputMetadata -Force | Out-Null }
}
Write-Host ""

# === VC-02: Ler versao do arquivo centralizado ===
$VersionFile = Join-Path $ProjectRoot "INSTALADOR\comum\version.env"
if (-not (Test-Path $VersionFile)) {
    Write-Host "❌ ERRO: version.env nao encontrado: $VersionFile" -ForegroundColor Red
    exit 1
}

$VersionContent = Get-Content $VersionFile
$VERSION = ($VersionContent | Select-String '^VERSION=' | ForEach-Object { ($_ -replace '^VERSION=', '').Trim('"') })
$PRODUCT_NAME = ($VersionContent | Select-String '^PRODUCT_NAME=' | ForEach-Object { ($_ -replace '^PRODUCT_NAME=', '').Trim('"') })
$PUBLISHER = ($VersionContent | Select-String '^PUBLISHER=' | ForEach-Object { ($_ -replace '^PUBLISHER=', '').Trim('"') })

if (-not $VERSION) {
    Write-Host "❌ ERRO: VERSION nao definida em $VersionFile" -ForegroundColor Red
    exit 1
}

Write-Host "Versao: $VERSION" -ForegroundColor Green
Write-Host "Produto: $PRODUCT_NAME" -ForegroundColor Green
Write-Host "Publisher: $PUBLISHER" -ForegroundColor Green
Write-Host ""

$env:PROTONS_VERSION = $VERSION

# === RP-01: Reprodutibilidade ===
$env:TZ = "UTC"

$hasGitRepo = Test-Path (Join-Path $ProjectRoot ".git")
$epoch = if ($hasGitRepo -and (Get-Command git -ErrorAction SilentlyContinue)) {
  try { [int](& git log -1 --format=%ct 2>$null) } catch { [int][double]::Parse((Get-Date -UFormat %s)) }
} else {
  [int][double]::Parse((Get-Date -UFormat %s))
}
$env:SOURCE_DATE_EPOCH = "$epoch"

$BuildMetadataDir = Join-Path $ProjectRoot "INSTALADOR\saida\metadata"
if (-not (Test-Path $BuildMetadataDir)) { New-Item -ItemType Directory -Path $BuildMetadataDir | Out-Null }
$gitCommit = "unknown"
if ($hasGitRepo -and (Get-Command git -ErrorAction SilentlyContinue)) {
  try { $gitCommit = (& git rev-parse --short HEAD 2>$null).Trim() } catch {}
}
$gitDirty = 1
if ($hasGitRepo -and (Get-Command git -ErrorAction SilentlyContinue)) {
  try { $gitDirty = if ((& git status --porcelain 2>$null).Length -eq 0) { 0 } else { 1 } } catch {}
}
$buildUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

@(
  "VERSION=$VERSION",
  "SOURCE_DATE_EPOCH=$env:SOURCE_DATE_EPOCH",
  "GIT_COMMIT=$gitCommit",
  "GIT_DIRTY=$gitDirty",
  "BUILD_UTC=$buildUtc"
) | Set-Content -Path (Join-Path $BuildMetadataDir "build-info.txt") -Encoding UTF8

# Verificar se .iss existe
if (-not (Test-Path $IssFile)) {
    Write-Host "❌ ERRO: Arquivo .iss não encontrado: $IssFile" -ForegroundColor Red
    exit 1
}

# Preparar assets visuais do instalador (UX PT-BR)
if (-not (Test-Path $PrepareUxAssetsScript)) {
    Write-Host "❌ ERRO: Script de assets UX nao encontrado: $PrepareUxAssetsScript" -ForegroundColor Red
    exit 1
}

Start-BuildStep -StepName "Preparacao assets UX Inno"
Write-Log STEP "Gerando assets visuais do instalador..."
& $PrepareUxAssetsScript -ProjectRoot $ProjectRoot
if ($LASTEXITCODE -ne 0) {
    Write-Log ERROR "Falha ao preparar assets UX do Inno (codigo $LASTEXITCODE)"
    Complete-BuildLog -Status "FAILED"
    exit 1
}
foreach ($assetPath in @($WizardBackLightPath, $WizardBackDarkPath, $WizardSmallLogoLightPath, $WizardSmallLogoDarkPath)) {
    if (-not (Test-Path -LiteralPath $assetPath)) {
        Write-Log ERROR "Asset UX esperado nao encontrado: $assetPath"
        Complete-BuildLog -Status "FAILED"
        exit 1
    }
    $assetItem = Get-Item -LiteralPath $assetPath
    Write-Log INFO "Asset UX: $assetPath ($($assetItem.Length) bytes)"
}
Write-Log INFO "Perfil visual ativo: Windows 11 dinamico (light/dark)"
Complete-BuildStep -StepName "Preparacao assets UX Inno"

# Verificar certificado para assinatura
$CertPath = $env:PROTONS_CERT_PATH
$CertPass = $env:PROTONS_CERT_PASS
$SigningEnabled = $false

if ($CertPath -and (Test-Path $CertPath)) {
    Write-Host "✅ Certificado encontrado: $CertPath" -ForegroundColor Green
    Write-Host "   Assinatura digital será aplicada" -ForegroundColor Green
    $SigningEnabled = $true

    # Criar arquivo .iss temporário com assinatura habilitada
    $IssContent = Get-Content $IssFile -Raw
    $IssContent = $IssContent -replace "; SignTool=", "SignTool="
    $IssContent = $IssContent -replace "; SignedUninstaller=", "SignedUninstaller="

    $TempIss = "$env:TEMP\protons-setup-signed.iss"
    $IssContent | Set-Content $TempIss -Encoding UTF8
    $IssFile = $TempIss
} else {
    Write-Host "⚠️  Certificado não configurado - EXE NÃO será assinado" -ForegroundColor Yellow
    Write-Host "   Para assinar, defina:" -ForegroundColor Gray
    Write-Host "   `$env:PROTONS_CERT_PATH = 'C:\caminho\certificado.pfx'" -ForegroundColor Gray
    Write-Host "   `$env:PROTONS_CERT_PASS = 'senha'" -ForegroundColor Gray
}
Write-Host ""

# Compilar com versao centralizada (VC-02)
Start-BuildStep -StepName "Compilacao Inno Setup"
Write-Log STEP "Compilando instalador versao $VERSION..."
Set-Location (Split-Path -Parent $IssFile)

# Passar versao como define para o Inno Setup
& $InnoPath "/DAppVersion=$VERSION" "/DAppPublisher=$PUBLISHER" $IssFile
if ($LASTEXITCODE -ne 0) {
    Write-Log ERROR "Falha na compilação do Inno Setup (código $LASTEXITCODE)"
    Complete-BuildLog -Status "FAILED"
    exit 1
}

Write-Log OK "Compilação concluída"
Complete-BuildStep -StepName "Compilacao Inno Setup"

# Verificar resultado
$ExeFile = "$OutputDir\ProtonsSetup-$VERSION.exe"
if (Test-Path $ExeFile) {
    # OB-03: Gerar checksum SHA256
    Start-BuildStep -StepName "Checksum"
    $checksum = New-FileChecksum -FilePath $ExeFile
    Complete-BuildStep -StepName "Checksum"

    # Obter tamanho
    $artifactSize = "$([math]::Round((Get-Item $ExeFile).Length / 1MB, 2)) MB"

    if ($SigningEnabled) {
        Write-Log OK "EXE assinado digitalmente"

        # Verificar assinatura
        Start-BuildStep -StepName "Verificacao de assinatura"
        $SignTool = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -match "x64" } |
                    Select-Object -First 1 -ExpandProperty FullName

        if ($SignTool) {
            & $SignTool verify /pa $ExeFile 2>&1 | ForEach-Object { Write-Log INFO "   $_" }
        }
        Complete-BuildStep -StepName "Verificacao de assinatura"
    } else {
        Write-Log WARN "EXE NÃO assinado (certificado não configurado)"
    }

    # OB-02: Adicionar duracoes ao build-info.txt
    $BuildInfoPath = Join-Path $ProjectRoot "INSTALADOR\saida\metadata\build-info.txt"
    Add-DurationsToBuildInfo -BuildInfoPath $BuildInfoPath

    # Finalizar log com resumo
    Complete-BuildLog -Status "SUCCESS" -Artifact $ExeFile -ArtifactSize $artifactSize -Checksum $checksum

    Write-Log INFO "-"
    Write-Log INFO "Para testar a instalação: $ExeFile"
} else {
    Write-Log ERROR "EXE não foi gerado"
    Complete-BuildLog -Status "FAILED"
    exit 1
}
