# Validate reinstall/upgrade data preservation for MSI baseline.

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

    $suiteName = 'UPGRADE-REINSTALL-E2E'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $installLog = Join-Path $ctx.LogDir ("upgrade-install-{0}.log" -f $ctx.Timestamp)
    $reinstallLog = Join-Path $ctx.LogDir ("upgrade-reinstall-{0}.log" -f $ctx.Timestamp)
    $uninstallLog = Join-Path $ctx.LogDir ("upgrade-uninstall-{0}.log" -f $ctx.Timestamp)

    $installArgs = "/i `"$($ctx.MsiPath)`" /qn /L*v `"$installLog`" /norestart"
    $reinstallArgs = "/fvamus `"$($ctx.MsiPath)`" /qn /L*v `"$reinstallLog`" /norestart"
    $uninstallArgs = "/x `"$($ctx.MsiPath)`" /qn /L*v `"$uninstallLog`" /norestart"

    $installDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $installArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "Initial install failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'UPGRADE-00-INSTALL' -Status 'PASS' -Details "Initial install succeeded in ${installDuration}s" -Evidence $installLog

    $appDataPath = Join-Path $env:APPDATA 'Protons'
    if (-not (Test-Path $appDataPath)) {
        New-Item -ItemType Directory -Path $appDataPath -Force | Out-Null
    }
    $sentinel = Join-Path $appDataPath ("upgrade-sentinel-{0}.txt" -f $ctx.Timestamp)
    "upgrade-sentinel:$($ctx.Timestamp)" | Set-Content -Path $sentinel -Encoding ASCII
    Add-Result -Store $store -Id 'UPGRADE-01-SENTINEL-CREATED' -Status 'PASS' -Details "Sentinel created: $sentinel"

    $reinstallDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $reinstallArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "Reinstall failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'UPGRADE-02-REINSTALL' -Status 'PASS' -Details "Maintenance reinstall succeeded in ${reinstallDuration}s" -Evidence $reinstallLog

    Add-Result -Store $store -Id 'UPGRADE-03-SENTINEL-PRESERVED' -Status ($(if (Test-Path $sentinel) { 'PASS' } else { 'FAIL' })) -Details "Sentinel preservation after reinstall: $sentinel"

    $uninstallDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $uninstallArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "Cleanup uninstall failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'UPGRADE-04-CLEANUP-UNINSTALL' -Status 'PASS' -Details "Cleanup uninstall succeeded in ${uninstallDuration}s" -Evidence $uninstallLog

    Add-Result -Store $store -Id 'UPGRADE-05-SENTINEL-AFTER-UNINSTALL' -Status ($(if (Test-Path $sentinel) { 'PASS' } else { 'FAIL' })) -Details 'Sentinel must remain after uninstall'

    $resultBase = Join-Path $ctx.LogDir ("upgrade-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        duration_seconds = [ordered]@{
            install = $installDuration
            reinstall = $reinstallDuration
            uninstall = $uninstallDuration
        }
    }
    $metricsPath = Join-Path $ctx.LogDir ("upgrade-metrics-{0}.json" -f $ctx.Timestamp)
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
