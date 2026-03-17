# =============================================================================
# Lint-Build.ps1 - Valida configuração antes de executar builds
# =============================================================================
# QB-04: Script de lint para validação de qualidade
#
# Verifica:
#   - Existência de arquivos obrigatórios
#   - Consistência de versão
#   - Dependências necessárias
#   - Padrões de código
# =============================================================================

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$VersionFile = Join-Path $ScriptDir "comum\version.env"

Write-Host "=== Lint de Build - Protons ===" -ForegroundColor Cyan
Write-Host ""

$script:Errors = 0
$script:Warnings = 0

function Write-Error-Item {
    param([string]$Message)
    Write-Host "❌ ERRO: $Message" -ForegroundColor Red
    $script:Errors++
}

function Write-Warning-Item {
    param([string]$Message)
    Write-Host "⚠️  AVISO: $Message" -ForegroundColor Yellow
    $script:Warnings++
}

function Write-Ok {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

# =============================================================================
# 1. Verificar arquivos de configuração obrigatórios
# =============================================================================
Write-Host "[1/5] Verificando arquivos de configuração..." -ForegroundColor White

if (Test-Path $VersionFile) { Write-Ok "version.env existe" } else { Write-Error-Item "version.env não encontrado" }
if (Test-Path "$ScriptDir\comum\version.props") { Write-Ok "version.props existe" } else { Write-Error-Item "version.props não encontrado" }
if (Test-Path "$ProjectRoot\Login\global.json") { Write-Ok "global.json existe" } else { Write-Warning-Item "global.json não encontrado (SDK não fixado)" }

Write-Host ""

# =============================================================================
# 2. Verificar consistência de versão
# =============================================================================
Write-Host "[2/5] Verificando consistência de versão..." -ForegroundColor White

if (Test-Path $VersionFile) {
    $VersionContent = Get-Content $VersionFile
    $VERSION = ($VersionContent | Select-String '^VERSION=' | ForEach-Object { ($_ -replace '^VERSION=', '').Trim('"') })
    Write-Ok "Versão centralizada: $VERSION"

    # Verificar version.props
    $PropsFile = "$ScriptDir\comum\version.props"
    if (Test-Path $PropsFile) {
        $PropsContent = Get-Content $PropsFile -Raw
        if ($PropsContent -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
            $PropsVersion = $Matches[1]
            if ($PropsVersion -eq $VERSION) {
                Write-Ok "version.props consistente"
            } else {
                Write-Error-Item "version.props inconsistente: $PropsVersion (esperado: $VERSION)"
            }
        }
    }
}

Write-Host ""

# =============================================================================
# 3. Verificar estrutura de diretórios
# =============================================================================
Write-Host "[3/5] Verificando estrutura de diretórios..." -ForegroundColor White

if (Test-Path "$ProjectRoot\INSTALADOR\windows\wix") { Write-Ok "Diretório WiX existe" } else { Write-Warning-Item "Diretório WiX não encontrado" }
if (Test-Path "$ProjectRoot\INSTALADOR\windows\innosetup") { Write-Ok "Diretório Inno Setup existe" } else { Write-Warning-Item "Diretório Inno Setup não encontrado" }
if (Test-Path "$ProjectRoot\INSTALADOR\linux\appimage") { Write-Ok "Diretório AppImage existe" } else { Write-Warning-Item "Diretório AppImage não encontrado" }
if (Test-Path "$ProjectRoot\INSTALADOR\linux\deb") { Write-Ok "Diretório DEB existe" } else { Write-Warning-Item "Diretório DEB não encontrado" }
if (Test-Path "$ProjectRoot\Login\Protons.UI") { Write-Ok "Projeto Protons.UI existe" } else { Write-Error-Item "Projeto Protons.UI não encontrado" }

Write-Host ""

# =============================================================================
# 4. Verificar arquivos de build essenciais
# =============================================================================
Write-Host "[4/5] Verificando arquivos de build..." -ForegroundColor White

# WiX
if (Test-Path "$ProjectRoot\INSTALADOR\windows\wix\Product.wxs") { Write-Ok "Product.wxs existe" } else { Write-Error-Item "Product.wxs não encontrado" }
if (Test-Path "$ProjectRoot\INSTALADOR\windows\wix\Components.wxs") { Write-Ok "Components.wxs existe" } else { Write-Error-Item "Components.wxs não encontrado" }
if (Test-Path "$ProjectRoot\INSTALADOR\windows\wix\Features.wxs") { Write-Ok "Features.wxs existe" } else { Write-Error-Item "Features.wxs não encontrado" }

# Inno Setup
if (Test-Path "$ProjectRoot\INSTALADOR\windows\innosetup\protons-setup.iss") { Write-Ok "protons-setup.iss existe" } else { Write-Error-Item "protons-setup.iss não encontrado" }

# Projeto .NET
if (Test-Path "$ProjectRoot\Login\Protons.UI\Protons.UI.csproj") { Write-Ok "Protons.UI.csproj existe" } else { Write-Error-Item "Protons.UI.csproj não encontrado" }

Write-Host ""

# =============================================================================
# 5. Verificar por versões hardcoded (anti-pattern)
# =============================================================================
Write-Host "[5/5] Verificando por versões hardcoded..." -ForegroundColor White

$FilesToCheck = @(
    "$ProjectRoot\INSTALADOR\windows\wix\Protons.wixproj",
    "$ProjectRoot\INSTALADOR\windows\wix\Variables.wxi"
)

foreach ($file in $FilesToCheck) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw -ErrorAction SilentlyContinue
        if ($content -match "ARQUIVO GERADO AUTOMATICAMENTE") {
            Write-Ok "$(Split-Path -Leaf $file) é gerado automaticamente"
        } else {
            # Verificar versões hardcoded (padrão X.Y.Z)
            $lines = Get-Content $file | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch '^\s*<!--' }
            $hasHardcoded = $lines | Where-Object { $_ -match '\d+\.\d+\.\d+' }
            if ($hasHardcoded) {
                Write-Warning-Item "$(Split-Path -Leaf $file) pode conter versão hardcoded"
            } else {
                Write-Ok "$(Split-Path -Leaf $file) não tem versão hardcoded visível"
            }
        }
    }
}

Write-Host ""

# =============================================================================
# Resumo
# =============================================================================
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "RESUMO DO LINT" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "Erros:   $script:Errors" -ForegroundColor $(if ($script:Errors -gt 0) { "Red" } else { "Green" })
Write-Host "Avisos:  $script:Warnings" -ForegroundColor $(if ($script:Warnings -gt 0) { "Yellow" } else { "Green" })
Write-Host ""

if ($script:Errors -gt 0) {
    Write-Host "❌ Build NÃO está pronto - corrija os erros acima" -ForegroundColor Red
    exit 1
} elseif ($script:Warnings -gt 0) {
    Write-Host "⚠️  Build pode prosseguir, mas considere os avisos" -ForegroundColor Yellow
    exit 0
} else {
    Write-Host "✅ Build está pronto para execução!" -ForegroundColor Green
    exit 0
}
