# Validate MSI rollback behavior using an injected failpoint.

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
    Ensure-File -Path $ctx.MsiPath -Label 'MSI artifact'
    $contract = Get-InstallerContract
    $paths = $contract.paths

    $suiteName = 'MSI-ROLLBACK'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $installLog = Join-Path $ctx.LogDir ("msi-rollback-install-{0}.log" -f $ctx.Timestamp)
    $cleanupLog = Join-Path $ctx.LogDir ("msi-rollback-cleanup-{0}.log" -f $ctx.Timestamp)

    # Best-effort cleanup from previous runs.
    $cleanupArgs = "/x `"$($ctx.MsiPath)`" /qn /L*v `"$cleanupLog`" /norestart"
    try {
        $null = Start-Process -FilePath 'msiexec.exe' -ArgumentList $cleanupArgs -Wait -PassThru
    } catch {
    }

    $installArgs = "/i `"$($ctx.MsiPath)`" PROTONS_ROLLBACK_TEST=1 /qn /L*v `"$installLog`" /norestart"
    $installExitCode = 0
    $installDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $installArgs -Wait -PassThru
        $script:installExitCode = $proc.ExitCode
    }

    if ($installExitCode -ne 0) {
        Add-Result -Store $store -Id 'MSI-ROLLBACK-INSTALL-FAILPOINT' -Status 'PASS' -Details "MSI failed as expected (ExitCode=$installExitCode) in ${installDuration}s" -Evidence $installLog
    } else {
        Add-Result -Store $store -Id 'MSI-ROLLBACK-INSTALL-FAILPOINT' -Status 'FAIL' -Details "MSI succeeded unexpectedly with PROTONS_ROLLBACK_TEST=1" -Evidence $installLog
    }

    $installDir = [Environment]::ExpandEnvironmentVariables([string]$paths.install_dir)
    $desktopShortcut = [Environment]::ExpandEnvironmentVariables([string]$paths.desktop_shortcut)
    $startMenuDir = [Environment]::ExpandEnvironmentVariables([string]$paths.start_menu_dir)
    $regPathHklm = [string]$paths.registry_hklm
    $regPathHkcu = [string]$paths.registry_hkcu_legacy

    $dirWait = Wait-Condition -Description 'Rollback Program Files cleanup' -TimeoutSeconds 30 -PollIntervalMilliseconds 1000 -Condition {
        -not (Test-Path $installDir)
    }
    Add-Result -Store $store -Id 'MSI-ROLLBACK-01-PROGRAMFILES' -Status ($(if ($dirWait.Satisfied) { 'PASS' } else { 'FAIL' })) -Details "Install dir removed=$($dirWait.Satisfied); path=$installDir; waited=$($dirWait.ElapsedSeconds)s"

    Add-Result -Store $store -Id 'MSI-ROLLBACK-02-DESKTOP' -Status ($(if (-not (Test-Path $desktopShortcut)) { 'PASS' } else { 'FAIL' })) -Details "Desktop shortcut absent: $desktopShortcut"
    Add-Result -Store $store -Id 'MSI-ROLLBACK-03-STARTMENU' -Status ($(if (-not (Test-Path $startMenuDir)) { 'PASS' } else { 'FAIL' })) -Details "Start menu folder absent: $startMenuDir"
    Add-Result -Store $store -Id 'MSI-ROLLBACK-04-REGISTRY-HKLM' -Status ($(if (-not (Test-Path $regPathHklm)) { 'PASS' } else { 'FAIL' })) -Details "HKLM cleanup: $regPathHklm"
    Add-Result -Store $store -Id 'MSI-ROLLBACK-05-REGISTRY-HKCU' -Status ($(if (-not (Test-Path $regPathHkcu)) { 'PASS' } else { 'FAIL' })) -Details "Legacy HKCU cleanup: $regPathHkcu"

    # If failpoint was not wired and app got installed, clean immediately.
    if (Test-Path $installDir) {
        try {
            $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $cleanupArgs -Wait -PassThru
            Add-Result -Store $store -Id 'MSI-ROLLBACK-CLEANUP-FALLBACK' -Status ($(if ($proc.ExitCode -eq 0) { 'PASS' } else { 'FAIL' })) -Details "Fallback uninstall exit code: $($proc.ExitCode)" -Evidence $cleanupLog
        } catch {
            Add-Result -Store $store -Id 'MSI-ROLLBACK-CLEANUP-FALLBACK' -Status 'FAIL' -Details "Fallback uninstall error: $($_.Exception.Message)" -Evidence $cleanupLog
        }
    }

    $resultBase = Join-Path $ctx.LogDir ("msi-rollback-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        duration_seconds = [ordered]@{
            install = $installDuration
        }
        failpoint_exit_code = $installExitCode
    }
    $metricsPath = Join-Path $ctx.LogDir ("msi-rollback-metrics-{0}.json" -f $ctx.Timestamp)
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
