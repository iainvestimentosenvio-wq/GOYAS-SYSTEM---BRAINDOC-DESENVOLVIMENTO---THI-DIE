# =============================================================================
# Bump-Version.ps1 - Atualiza a versao centralizada do projeto
# =============================================================================
# Uso:
#   .\Bump-Version.ps1 -Version 1.1.0
#   .\Bump-Version.ps1 -Bump patch   # 1.0.0 -> 1.0.1
#   .\Bump-Version.ps1 -Bump minor   # 1.0.0 -> 1.1.0
#   .\Bump-Version.ps1 -Bump major   # 1.0.0 -> 2.0.0
# =============================================================================

param(
    [string]$Version,
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $PSScriptRoot
$VersionFile = Join-Path $ScriptDir "comum\version.env"
$VersionProps = Join-Path $ScriptDir "comum\version.props"

if (-not (Test-Path $VersionFile)) {
    Write-Host "ERRO: version.env nao encontrado: $VersionFile" -ForegroundColor Red
    exit 1
}

# Ler versao atual
$Content = Get-Content $VersionFile
$CurrentVersion = ($Content | Select-String '^VERSION=' | ForEach-Object { ($_ -replace '^VERSION=', '').Trim('"') })

if (-not $CurrentVersion) {
    Write-Host "ERRO: VERSION nao definida em $VersionFile" -ForegroundColor Red
    exit 1
}

# Extrair major.minor.patch
$Parts = $CurrentVersion.Split('.')
$Major = [int]$Parts[0]
$Minor = [int]$Parts[1]
$Patch = [int]$Parts[2]

# Processar argumentos
if (-not $Version -and -not $Bump) {
    Write-Host "Uso: .\Bump-Version.ps1 -Version <nova-versao>" -ForegroundColor Yellow
    Write-Host "     .\Bump-Version.ps1 -Bump <patch|minor|major>" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Exemplos:" -ForegroundColor Gray
    Write-Host "  .\Bump-Version.ps1 -Version 1.1.0    # Define versao especifica"
    Write-Host "  .\Bump-Version.ps1 -Bump patch       # Incrementa patch: $CurrentVersion -> $Major.$Minor.$($Patch + 1)"
    Write-Host "  .\Bump-Version.ps1 -Bump minor       # Incrementa minor: $CurrentVersion -> $Major.$($Minor + 1).0"
    Write-Host "  .\Bump-Version.ps1 -Bump major       # Incrementa major: $CurrentVersion -> $($Major + 1).0.0"
    Write-Host ""
    Write-Host "Versao atual: $CurrentVersion" -ForegroundColor Cyan
    exit 0
}

$NewVersion = $Version

if ($Bump) {
    switch ($Bump) {
        "patch" { $NewVersion = "$Major.$Minor.$($Patch + 1)" }
        "minor" { $NewVersion = "$Major.$($Minor + 1).0" }
        "major" { $NewVersion = "$($Major + 1).0.0" }
    }
}

# Validar formato X.Y.Z
if ($NewVersion -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "ERRO: Formato de versao invalido: $NewVersion" -ForegroundColor Red
    Write-Host "      Use o formato X.Y.Z (ex: 1.2.3)" -ForegroundColor Yellow
    exit 1
}

# Extrair novos valores
$NewParts = $NewVersion.Split('.')
$NewMajor = $NewParts[0]
$NewMinor = $NewParts[1]
$NewPatch = $NewParts[2]

Write-Host "=== Bump de Versao ===" -ForegroundColor Cyan
Write-Host "Versao atual:  $CurrentVersion" -ForegroundColor Yellow
Write-Host "Nova versao:   $NewVersion" -ForegroundColor Green
Write-Host ""

# Atualizar version.env
Write-Host "Atualizando $VersionFile..." -ForegroundColor Gray
$NewContent = $Content -replace '^VERSION=.*', "VERSION=$NewVersion"
$NewContent = $NewContent -replace '^VERSION_MAJOR=.*', "VERSION_MAJOR=$NewMajor"
$NewContent = $NewContent -replace '^VERSION_MINOR=.*', "VERSION_MINOR=$NewMinor"
$NewContent = $NewContent -replace '^VERSION_PATCH=.*', "VERSION_PATCH=$NewPatch"
$NewContent | Set-Content $VersionFile -Encoding UTF8

# Atualizar version.props
if (Test-Path $VersionProps) {
    Write-Host "Atualizando $VersionProps..." -ForegroundColor Gray
    $PropsContent = Get-Content $VersionProps -Raw
    $PropsContent = $PropsContent -replace '<VersionPrefix>.*</VersionPrefix>', "<VersionPrefix>$NewVersion</VersionPrefix>"
    $PropsContent | Set-Content $VersionProps -Encoding UTF8
}

Write-Host ""
Write-Host "Versao atualizada para $NewVersion" -ForegroundColor Green
Write-Host ""
Write-Host "Arquivos modificados:" -ForegroundColor White
Write-Host "  - $VersionFile" -ForegroundColor Gray
if (Test-Path $VersionProps) {
    Write-Host "  - $VersionProps" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Proximo passo: execute os scripts de build para gerar artefatos com a nova versao" -ForegroundColor Yellow
Write-Host "  .\INSTALADOR\windows\scripts\build-msi.ps1" -ForegroundColor Gray
Write-Host "  .\INSTALADOR\windows\scripts\build-inno.ps1" -ForegroundColor Gray
