# Validate MSI repair flow without losing user data.

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

    $suiteName = 'MSI-REPAIR'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $installLog = Join-Path $ctx.LogDir ("msi-repair-install-{0}.log" -f $ctx.Timestamp)
    $repairLog = Join-Path $ctx.LogDir ("msi-repair-run-{0}.log" -f $ctx.Timestamp)
    $uninstallLog = Join-Path $ctx.LogDir ("msi-repair-uninstall-{0}.log" -f $ctx.Timestamp)

    $installArgs = "/i `"$($ctx.MsiPath)`" /qn /L*v `"$installLog`" /norestart"
    $installDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $installArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "MSI install failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'MSI-REPAIR-01-INSTALL' -Status 'PASS' -Details "Install succeeded in ${installDuration}s" -Evidence $installLog

    $targetFile = Join-Path ${env:ProgramFiles} 'Protons\appsettings.json'
    if (-not (Test-Path $targetFile)) {
        Add-Result -Store $store -Id 'MSI-REPAIR-02-TARGET-FILE' -Status 'FAIL' -Details "Repair target file missing: $targetFile"
    } else {
        Add-Result -Store $store -Id 'MSI-REPAIR-02-TARGET-FILE' -Status 'PASS' -Details "Repair target file found: $targetFile"
        Remove-Item -Path $targetFile -Force -ErrorAction SilentlyContinue
        Add-Result -Store $store -Id 'MSI-REPAIR-03-TARGET-DELETED' -Status ($(if (-not (Test-Path $targetFile)) { 'PASS' } else { 'FAIL' })) -Details "Target file deleted before repair: $targetFile"
    }

    $repairArgs = "/fa `"$($ctx.MsiPath)`" /qn /L*v `"$repairLog`" /norestart"
    $repairDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $repairArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "MSI repair failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'MSI-REPAIR-04-REPAIR-CMD' -Status 'PASS' -Details "Repair command succeeded in ${repairDuration}s" -Evidence $repairLog
    Add-Result -Store $store -Id 'MSI-REPAIR-05-TARGET-RESTORED' -Status ($(if (Test-Path $targetFile) { 'PASS' } else { 'FAIL' })) -Details "Target file restored after repair: $targetFile"

    $uninstallArgs = "/x `"$($ctx.MsiPath)`" /qn /L*v `"$uninstallLog`" /norestart"
    $uninstallDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $uninstallArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "MSI uninstall failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'MSI-REPAIR-06-UNINSTALL' -Status 'PASS' -Details "Uninstall succeeded in ${uninstallDuration}s" -Evidence $uninstallLog

    $resultBase = Join-Path $ctx.LogDir ("msi-repair-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        duration_seconds = [ordered]@{
            install = $installDuration
            repair = $repairDuration
            uninstall = $uninstallDuration
        }
    }
    $metricsPath = Join-Path $ctx.LogDir ("msi-repair-metrics-{0}.json" -f $ctx.Timestamp)
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
