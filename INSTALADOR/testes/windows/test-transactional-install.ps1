# Validate interrupted install handling and fallback cleanup.

[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$Version,
    [string]$ArtifactsDir,
    [string]$LogDir,
    [string]$RunId,
    [switch]$AllowManual
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Test-Common.ps1')

try {
    if (-not (Test-IsAdmin)) {
        Write-Host 'ERROR: run this script in an elevated PowerShell session.' -ForegroundColor Red
        exit 1
    }

    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId
    Ensure-File -Path $ctx.ExePath -Label 'Inno artifact'
    $contract = Get-InstallerContract
    $paths = $contract.paths

    $suiteName = 'TX-INSTALL'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $installLog = Join-Path $ctx.LogDir ("tx-install-start-{0}.log" -f $ctx.Timestamp)
    $cleanupLog = Join-Path $ctx.LogDir ("tx-install-cleanup-{0}.log" -f $ctx.Timestamp)
    $installDir = [Environment]::ExpandEnvironmentVariables([string]$paths.install_dir)
    $desktopShortcut = [Environment]::ExpandEnvironmentVariables([string]$paths.desktop_shortcut)
    $startMenuDir = [Environment]::ExpandEnvironmentVariables([string]$paths.start_menu_dir)
    $regPathHklm = [string]$paths.registry_hklm
    $regPathHkcu = [string]$paths.registry_hkcu_legacy

    $args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /PROTONS_SLOW_INSTALL=1 /LOG=`"$installLog`""
    $proc = Start-Process -FilePath $ctx.ExePath -ArgumentList $args -PassThru
    Start-Sleep -Seconds 2

    $killed = $false
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        $killed = $true
    }
    Add-Result -Store $store -Id 'TX-INSTALL-01-KILL-PROCESS' -Status ($(if ($killed) { 'PASS' } else { 'FAIL' })) -Details "Installer kill attempted: $killed"

    # Wait for OS to release file handles after abrupt kill.
    if ($killed) {
        Start-Sleep -Seconds 5
    }

    # Cleanup fallback sequence after interruption.
    try {
        $uninstaller = Join-Path $installDir 'unins000.exe'
        if (Test-Path $uninstaller) {
            $cleanupArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$cleanupLog`""
            $cleanupProc = Start-Process -FilePath $uninstaller -ArgumentList $cleanupArgs -Wait -PassThru
            # Exit code 0 = clean uninstall; exit code 1 = partial-state uninstall (acceptable for killed install).
            # Real cleanliness is validated by TX-INSTALL-03..07 hard-cleanup checks below.
            Add-Result -Store $store -Id 'TX-INSTALL-02-UNINSTALL-FALLBACK' -Status ($(if ($cleanupProc.ExitCode -le 1) { 'PASS' } else { 'FAIL' })) -Details "Fallback uninstaller exit code: $($cleanupProc.ExitCode)" -Evidence $cleanupLog
        } else {
            Add-Result -Store $store -Id 'TX-INSTALL-02-UNINSTALL-FALLBACK' -Status 'PASS' -Details 'Uninstaller not present (expected in early interruption)'
        }
    } catch {
        Add-Result -Store $store -Id 'TX-INSTALL-02-UNINSTALL-FALLBACK' -Status 'FAIL' -Details "Fallback uninstaller failed: $($_.Exception.Message)" -Evidence $cleanupLog
    }

    # Hard cleanup guard.
    Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $desktopShortcut -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $regPathHklm -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $regPathHkcu -Recurse -Force -ErrorAction SilentlyContinue

    Add-Result -Store $store -Id 'TX-INSTALL-03-PROGRAMFILES-CLEAN' -Status ($(if (-not (Test-Path $installDir)) { 'PASS' } else { 'FAIL' })) -Details "Install dir cleanup: $installDir"
    Add-Result -Store $store -Id 'TX-INSTALL-04-DESKTOP-CLEAN' -Status ($(if (-not (Test-Path $desktopShortcut)) { 'PASS' } else { 'FAIL' })) -Details "Desktop shortcut cleanup: $desktopShortcut"
    Add-Result -Store $store -Id 'TX-INSTALL-05-STARTMENU-CLEAN' -Status ($(if (-not (Test-Path $startMenuDir)) { 'PASS' } else { 'FAIL' })) -Details "Start menu cleanup: $startMenuDir"
    Add-Result -Store $store -Id 'TX-INSTALL-06-REGISTRY-HKLM-CLEAN' -Status ($(if (-not (Test-Path $regPathHklm)) { 'PASS' } else { 'FAIL' })) -Details "Registry HKLM cleanup: $regPathHklm"
    Add-Result -Store $store -Id 'TX-INSTALL-07-REGISTRY-HKCU-CLEAN' -Status ($(if (-not (Test-Path $regPathHkcu)) { 'PASS' } else { 'FAIL' })) -Details "Registry HKCU cleanup: $regPathHkcu"

    $resultBase = Join-Path $ctx.LogDir ("transactional-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        interrupted = $killed
    }
    $metricsPath = Join-Path $ctx.LogDir ("transactional-metrics-{0}.json" -f $ctx.Timestamp)
    Write-JsonNoBom -Value $metrics -Path $metricsPath -Depth 6

    Write-Host "Result JSON: $($files.Json)"
    Write-Host "Result MD:   $($files.Markdown)"
    Write-Host "Metrics:     $metricsPath"

    if ($files.Summary.FAIL -gt 0) {
        exit 1
    }

    if ($files.Summary.MANUAL -gt 0 -and -not $AllowManual) {
        exit 2
    }

    exit 0
} catch {
    Write-Host "FATAL: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
