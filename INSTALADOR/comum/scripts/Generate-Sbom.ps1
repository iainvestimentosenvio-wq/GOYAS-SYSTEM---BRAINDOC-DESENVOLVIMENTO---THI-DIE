# Generate SBOM (CycloneDX) for Protons
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $PSScriptRoot
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..\..\..")

$versionFile = Join-Path $ProjectRoot "INSTALADOR\comum\version.env"
$VERSION = "1.0.0"
if (Test-Path $versionFile) {
  $line = Get-Content $versionFile | Select-String '^VERSION=' | Select-Object -First 1
  if ($line) {
    $VERSION = $line.ToString().Split('=')[1]
  }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Host "❌ ERRO: dotnet não encontrado no PATH" -ForegroundColor Red
  exit 1
}

$ToolsDir = Join-Path $ProjectRoot ".tools"
if (-not (Test-Path $ToolsDir)) {
  New-Item -ItemType Directory -Path $ToolsDir | Out-Null
}

$ToolPath = Join-Path $ToolsDir "dotnet-CycloneDX"
if (-not (Test-Path $ToolPath)) {
  dotnet tool install --tool-path $ToolsDir CycloneDX
}

$OutputDir = Join-Path $ProjectRoot "INSTALADOR\saida\sbom"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

& $ToolPath (Join-Path $ProjectRoot "Login\Protons.sln") -o $OutputDir -f "protons-$VERSION-sbom" -j --exclude-dev -rs

$rawFile = Join-Path $OutputDir "protons-$VERSION-sbom"
$jsonFile = Join-Path $OutputDir "protons-$VERSION-sbom.json"
if ((Test-Path $rawFile) -and (-not (Test-Path $jsonFile))) {
  Move-Item -Path $rawFile -Destination $jsonFile
}

if (Test-Path $jsonFile) {
  try {
    $sbom = Get-Content $jsonFile -Raw | ConvertFrom-Json
    if (-not $sbom.metadata) { $sbom | Add-Member -MemberType NoteProperty -Name metadata -Value (@{}) }
    if (-not $sbom.metadata.component) { $sbom.metadata | Add-Member -MemberType NoteProperty -Name component -Value (@{}) }
    $sbom.metadata.component.version = $VERSION
    $sbom | ConvertTo-Json -Depth 100 | Set-Content -Path $jsonFile -Encoding UTF8
  } catch {
    Write-Host "⚠️  Falha ao atualizar metadata.component.version no SBOM" -ForegroundColor Yellow
  }
}

Write-Host "✅ SBOM gerado: $jsonFile" -ForegroundColor Green
