# Validate cancel/fallback cleanup contract through controlled cancel simulation.

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

    $suiteName = 'CANCEL-INSTALL'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $logPath = Join-Path $ctx.LogDir ("cancel-install-{0}.log" -f $ctx.Timestamp)
    $installDir = [Environment]::ExpandEnvironmentVariables([string]$paths.install_dir)
    $desktopShortcut = [Environment]::ExpandEnvironmentVariables([string]$paths.desktop_shortcut)
    $regPathHklm = [string]$paths.registry_hklm
    $regPathHkcu = [string]$paths.registry_hkcu_legacy

    $args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /PROTONS_CANCEL_TEST=1 /LOG=`"$logPath`""
    $proc = Start-Process -FilePath $ctx.ExePath -ArgumentList $args -Wait -PassThru
    Add-Result -Store $store -Id 'CANCEL-INSTALL-01-CANCEL-EXIT' -Status ($(if ($proc.ExitCode -ne 0) { 'PASS' } else { 'FAIL' })) -Details "Cancel simulation exit code: $($proc.ExitCode)" -Evidence $logPath

    Add-Result -Store $store -Id 'CANCEL-INSTALL-02-PROGRAMFILES-CLEAN' -Status ($(if (-not (Test-Path $installDir)) { 'PASS' } else { 'FAIL' })) -Details "Install dir absent after cancel: $installDir"
    Add-Result -Store $store -Id 'CANCEL-INSTALL-03-DESKTOP-CLEAN' -Status ($(if (-not (Test-Path $desktopShortcut)) { 'PASS' } else { 'FAIL' })) -Details "Desktop shortcut absent after cancel: $desktopShortcut"
    Add-Result -Store $store -Id 'CANCEL-INSTALL-04-REGISTRY-HKLM-CLEAN' -Status ($(if (-not (Test-Path $regPathHklm)) { 'PASS' } else { 'FAIL' })) -Details "HKLM registry absent after cancel: $regPathHklm"
    Add-Result -Store $store -Id 'CANCEL-INSTALL-05-REGISTRY-HKCU-CLEAN' -Status ($(if (-not (Test-Path $regPathHkcu)) { 'PASS' } else { 'FAIL' })) -Details "Legacy HKCU registry absent after cancel: $regPathHkcu"

    $resultBase = Join-Path $ctx.LogDir ("cancel-install-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        cancel_exit_code = $proc.ExitCode
    }
    $metricsPath = Join-Path $ctx.LogDir ("cancel-install-metrics-{0}.json" -f $ctx.Timestamp)
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
