# One-command Windows test runner for Protons installer
$ErrorActionPreference = "Continue"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = $ScriptDir
$ProjectRoot = Join-Path $Root "INSTALADOR"

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$OutDir = Join-Path $Root "win-test-results\$timestamp"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$LogFile = Join-Path $OutDir "run.log"

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), $Message
    $line | Tee-Object -FilePath $LogFile -Append
}

Write-Log "=== Protons Windows Tests ==="
Write-Log "Root: $Root"
Write-Log "ProjectRoot: $ProjectRoot"
Write-Log "Output: $OutDir"

if (-not (Test-Path $ProjectRoot)) {
    Write-Log "ERRO: Pasta INSTALADOR nao encontrada. Copie a pasta correta para este local."
    exit 1
}

# Load version
$VersionFile = Join-Path $ProjectRoot "comum\version.env"
$VERSION = "1.0.0"
if (Test-Path $VersionFile) {
    $VersionContent = Get-Content $VersionFile
    $VERSION = ($VersionContent | Select-String '^VERSION=' | ForEach-Object { ($_ -replace '^VERSION=', '').Trim('"') })
}
Write-Log "Version: $VERSION"

# Resolve MSI/EXE paths
function Resolve-Artifacts {
    param([string]$RootPath, [string]$Version)
    $msi = Join-Path $RootPath "saida\windows\Protons-$Version-x64.msi"
    if (-not (Test-Path $msi)) {
        $msi = Get-ChildItem -Path (Join-Path $RootPath "saida\windows") -Filter "*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { $_.FullName }
    }
    $exe = Join-Path $RootPath "saida\windows\ProtonsSetup-$Version.exe"
    if (-not (Test-Path $exe)) {
        $exe = Get-ChildItem -Path (Join-Path $RootPath "saida\windows") -Filter "*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { $_.FullName }
    }
    return @{ MSI = $msi; EXE = $exe }
}

$Artifacts = Resolve-Artifacts -RootPath $ProjectRoot -Version $VERSION
$MsiPath = $Artifacts.MSI
$ExePath = $Artifacts.EXE

Write-Log "MSI: $MsiPath"
Write-Log "EXE: $ExePath"

# Auto-build if MSI/EXE missing and build scripts exist
if (-not $MsiPath -or -not $ExePath) {
    $BuildMsi = Join-Path $ProjectRoot "windows\scripts\build-msi.ps1"
    $BuildInno = Join-Path $ProjectRoot "windows\scripts\build-inno.ps1"

    $RepoRoot = Split-Path -Parent $ProjectRoot
    $UiProject = Join-Path $RepoRoot "Login\\Protons.UI\\Protons.UI.csproj"

    if (-not (Test-Path $UiProject)) {
        Write-Log "AVISO: Repo incompleto (Login\\Protons.UI nao encontrado). Auto-build nao e possivel."
    } elseif (Test-Path $BuildMsi) {
        Write-Log "MSI nao encontrado. Tentando gerar via build-msi.ps1..."
        try {
            & powershell -ExecutionPolicy Bypass -File $BuildMsi *>&1 | Tee-Object -FilePath (Join-Path $OutDir "build-msi.out.txt")
            Write-Log "build-msi.ps1 finalizado"
        } catch {
            Write-Log "ERRO ao rodar build-msi.ps1: $_"
        }
    } else {
        Write-Log "SKIP build-msi.ps1 (nao encontrado)"
    }

    if ((Test-Path $UiProject) -and (Test-Path $BuildInno)) {
        Write-Log "EXE nao encontrado. Tentando gerar via build-inno.ps1..."
        try {
            & powershell -ExecutionPolicy Bypass -File $BuildInno *>&1 | Tee-Object -FilePath (Join-Path $OutDir "build-inno.out.txt")
            Write-Log "build-inno.ps1 finalizado"
        } catch {
            Write-Log "ERRO ao rodar build-inno.ps1: $_"
        }
    } else {
        Write-Log "SKIP build-inno.ps1 (nao encontrado)"
    }

    # Re-resolve after build
    $Artifacts = Resolve-Artifacts -RootPath $ProjectRoot -Version $VERSION
    $MsiPath = $Artifacts.MSI
    $ExePath = $Artifacts.EXE
    Write-Log "MSI (apos build): $MsiPath"
    Write-Log "EXE (apos build): $ExePath"
}

# Run MSI test if possible
$MsiTest = Join-Path $ProjectRoot "testes\windows\test-msi.ps1"
if ((Test-Path $MsiTest) -and ($MsiPath)) {
    Write-Log "Rodando test-msi.ps1..."
    try {
        & powershell -ExecutionPolicy Bypass -File $MsiTest -ProjectRoot $ProjectRoot *>&1 | Tee-Object -FilePath (Join-Path $OutDir "test-msi.out.txt")
        Write-Log "test-msi.ps1 finalizado"
    } catch {
        Write-Log "ERRO ao rodar test-msi.ps1: $_"
    }
} else {
    Write-Log "SKIP test-msi.ps1 (arquivo ou MSI nao encontrado)"
}

# Run Inno test if possible
$InnoTest = Join-Path $ProjectRoot "testes\windows\test-inno.ps1"
if ((Test-Path $InnoTest) -and ($ExePath)) {
    Write-Log "Rodando test-inno.ps1..."
    try {
        & powershell -ExecutionPolicy Bypass -File $InnoTest -ProjectRoot $ProjectRoot *>&1 | Tee-Object -FilePath (Join-Path $OutDir "test-inno.out.txt")
        Write-Log "test-inno.ps1 finalizado"
    } catch {
        Write-Log "ERRO ao rodar test-inno.ps1: $_"
    }
} else {
    Write-Log "SKIP test-inno.ps1 (arquivo ou EXE nao encontrado)"
}

# Run evidence collection
$Collect = Join-Path $ProjectRoot "comum\scripts\Collect-Test-Evidence.ps1"
if (Test-Path $Collect) {
    Write-Log "Coletando evidencias..."
    try {
        & powershell -ExecutionPolicy Bypass -File $Collect *>&1 | Tee-Object -FilePath (Join-Path $OutDir "collect.out.txt")
        Write-Log "Evidencias coletadas"
    } catch {
        Write-Log "ERRO na coleta de evidencias: $_"
    }
} else {
    Write-Log "SKIP Collect-Test-Evidence.ps1 (nao encontrado)"
}

Write-Log "Concluido. Resultados em: $OutDir"
Write-Host "`n✅ Finalizado. Resultados em: $OutDir`n"
