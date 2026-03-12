# =============================================================================
# Init-Dirs.ps1 - Cria estrutura de diretorios de saida para builds
# =============================================================================
# QB-03: Padroniza criacao de diretorios de saida
#
# Uso:
#   . .\Init-Dirs.ps1  (dot-sourcing para importar variaveis)
#   ou
#   .\Init-Dirs.ps1 (apenas criar diretorios)
#
# =============================================================================

param(
    [string]$ProjectRoot
)

# Se ProjectRoot nao fornecido, tentar descobrir
if (-not $ProjectRoot) {
    $ScriptDir = Split-Path -Parent $PSScriptRoot
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
}

# Estrutura de diretorios de saida
$script:OutputBase = Join-Path $ProjectRoot "INSTALADOR\saida"
$script:OutputWindows = Join-Path $OutputBase "windows"
$script:OutputAppImage = Join-Path $OutputBase "appimage"
$script:OutputDeb = Join-Path $OutputBase "deb"
$script:OutputMetadata = Join-Path $OutputBase "metadata"

function Initialize-OutputDirs {
    Write-Host "Inicializando estrutura de diretórios de saída..." -ForegroundColor Gray

    # Criar diretorios principais
    $dirs = @(
        $script:OutputWindows,
        $script:OutputAppImage,
        $script:OutputDeb,
        $script:OutputMetadata
    )

    foreach ($dir in $dirs) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }

    Write-Host "✅ Diretórios criados:" -ForegroundColor Green
    Write-Host "   - $script:OutputWindows" -ForegroundColor Gray
    Write-Host "   - $script:OutputAppImage" -ForegroundColor Gray
    Write-Host "   - $script:OutputDeb" -ForegroundColor Gray
    Write-Host "   - $script:OutputMetadata" -ForegroundColor Gray
}

# Se executado diretamente, criar os diretorios
if ($MyInvocation.InvocationName -ne '.') {
    Initialize-OutputDirs
}

# Exportar variaveis para uso nos scripts de build
$global:OutputBase = $script:OutputBase
$global:OutputWindows = $script:OutputWindows
$global:OutputAppImage = $script:OutputAppImage
$global:OutputDeb = $script:OutputDeb
$global:OutputMetadata = $script:OutputMetadata
