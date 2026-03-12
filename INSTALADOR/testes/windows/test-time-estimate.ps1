# Validate ETA/progress hooks exist for installer UX.

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
    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId
    $suiteName = 'TIME-ESTIMATE'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $innoPath = Join-Path $ctx.ProjectRoot 'INSTALADOR\windows\innosetup\protons-setup.iss'
    Ensure-File -Path $innoPath -Label 'Inno script'
    $inno = Get-Content $innoPath -Raw

    Add-Result -Store $store -Id 'TIME-ESTIMATE-01-ETA-LABEL' -Status ($(if ($inno -match 'EtaLabel') { 'PASS' } else { 'FAIL' })) -Details 'EtaLabel custom message must exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'TIME-ESTIMATE-02-CALCULATING-MSG' -Status ($(if ($inno -match 'EtaCalculating') { 'PASS' } else { 'FAIL' })) -Details 'EtaCalculating message must exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'TIME-ESTIMATE-03-PROGRESS-HOOK' -Status ($(if ($inno -match 'CurInstallProgressChanged') { 'PASS' } else { 'FAIL' })) -Details 'CurInstallProgressChanged hook must exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'TIME-ESTIMATE-04-STAGE-LABEL' -Status ($(if ($inno -match 'ProgressStageLabel') { 'PASS' } else { 'FAIL' })) -Details 'Progress stage label must exist for ETA context' -Evidence $innoPath

    $resultBase = Join-Path $ctx.LogDir ("time-estimate-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    $metricsPath = Join-Path $ctx.LogDir ("time-estimate-metrics-{0}.json" -f $ctx.Timestamp)
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
