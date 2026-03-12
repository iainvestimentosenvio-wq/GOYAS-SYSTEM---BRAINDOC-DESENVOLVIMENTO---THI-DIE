# =============================================================================
# Collect-Test-Evidence.ps1 - Coleta evidencias de testes (Windows)
# =============================================================================
# Gera logs e evidencias padronizadas para auditoria e revisao
# =============================================================================

$ErrorActionPreference = "Continue"

$ScriptDir = Split-Path -Parent $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$VersionFile = Join-Path $ProjectRoot "INSTALADOR\comum\version.env"

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$OutDir = Join-Path $ProjectRoot "INSTALADOR\saida\test-logs\$timestamp"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$LogFile = Join-Path $OutDir "collect.log"

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), $Message
    $line | Tee-Object -FilePath $LogFile -Append
}

# Versao
if (Test-Path $VersionFile) {
    $VersionContent = Get-Content $VersionFile
    $VERSION = ($VersionContent | Select-String '^VERSION=' | ForEach-Object { ($_ -replace '^VERSION=', '').Trim('"') })
} else {
    $VERSION = "1.0.0"
}

$MsiPath = Join-Path $ProjectRoot "INSTALADOR\saida\windows\Protons-$VERSION-x64.msi"
$ExePath = Join-Path $ProjectRoot "INSTALADOR\saida\windows\ProtonsSetup-$VERSION.exe"

Write-Log "=== Coleta de Evidencias - Protons (Windows) ==="
Write-Log "Versao: $VERSION"
Write-Log "Saida: $OutDir"

# Ambiente
Write-Log "Coletando ambiente..."
try { Get-ComputerInfo | Out-File (Join-Path $OutDir "env.txt") } catch {}
try { dotnet --info | Out-File (Join-Path $OutDir "dotnet-info.txt") } catch {}

# Artefatos
"MSI: $MsiPath" | Out-File (Join-Path $OutDir "artifacts.txt")
"EXE: $ExePath" | Add-Content (Join-Path $OutDir "artifacts.txt")
if (Test-Path $MsiPath) { Get-Item $MsiPath | Format-List | Out-File (Join-Path $OutDir "msi-info.txt") }
if (Test-Path $ExePath) { Get-Item $ExePath | Format-List | Out-File (Join-Path $OutDir "exe-info.txt") }

# Hashes
if (Test-Path $MsiPath) {
    $hash = (Get-FileHash -Path $MsiPath -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path -Leaf $MsiPath)" | Out-File (Join-Path $OutDir "msi.sha256") -Encoding ASCII
}
if (Test-Path $ExePath) {
    $hash = (Get-FileHash -Path $ExePath -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path -Leaf $ExePath)" | Out-File (Join-Path $OutDir "exe.sha256") -Encoding ASCII
}

# Atalhos
function Get-ShortcutInfo {
    param([string]$ShortcutPath)
    if (Test-Path $ShortcutPath) {
        $wsh = New-Object -ComObject WScript.Shell
        $lnk = $wsh.CreateShortcut($ShortcutPath)
        "Path: $ShortcutPath" | Out-File -Append (Join-Path $OutDir "shortcuts.txt")
        "Target: $($lnk.TargetPath)" | Out-File -Append (Join-Path $OutDir "shortcuts.txt")
        "Icon: $($lnk.IconLocation)" | Out-File -Append (Join-Path $OutDir "shortcuts.txt")
        "" | Out-File -Append (Join-Path $OutDir "shortcuts.txt")
    }
}

$CommonDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
$CommonPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
Get-ShortcutInfo (Join-Path $CommonDesktop "Protons Login.lnk")
Get-ShortcutInfo (Join-Path (Join-Path $CommonPrograms "Protons") "Protons Login.lnk")

# Registro (opcional)
$regPath = "HKCU:\Software\Protons"
if (Test-Path $regPath) {
    Get-ItemProperty -Path $regPath | Format-List | Out-File (Join-Path $OutDir "registry.txt")
} else {
    "Registro não encontrado: $regPath" | Out-File (Join-Path $OutDir "registry.txt")
}

Write-Log "Coleta concluida. Evidencias em: $OutDir"
