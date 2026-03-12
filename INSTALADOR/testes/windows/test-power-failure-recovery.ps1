# Validate recovery after abrupt installer interruption ("power failure" simulation).

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

function New-FallbackContext {
    param(
        [string]$InputProjectRoot,
        [string]$InputVersion,
        [string]$InputArtifactsDir,
        [string]$InputLogDir,
        [string]$InputRunId
    )

    $baseRoot = $InputProjectRoot
    if (-not $baseRoot) {
        $scriptDir = Split-Path -Parent $PSScriptRoot
        $baseRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
    }
    if (Test-Path (Join-Path $baseRoot 'comum\version.env')) {
        $baseRoot = Split-Path -Parent $baseRoot
    }

    $ver = $InputVersion
    if (-not $ver) { $ver = '1.0.0' }

    $logPath = $InputLogDir
    if (-not $logPath) {
        $logPath = Join-Path $baseRoot 'INSTALADOR\saida\test-logs'
    }
    if (-not (Test-Path $logPath)) {
        New-Item -ItemType Directory -Path $logPath -Force | Out-Null
    }

    $run = $InputRunId
    if (-not $run) {
        $run = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    }

    $artifacts = $InputArtifactsDir
    if (-not $artifacts) {
        $artifacts = Join-Path $baseRoot 'INSTALADOR\saida\windows'
    }

    return [ordered]@{
        ProjectRoot = $baseRoot
        Version = $ver
        ArtifactsDir = $artifacts
        LogDir = $logPath
        RunId = $run
        Timestamp = $run
        MsiPath = Join-Path $artifacts ("Protons-{0}-x64.msi" -f $ver)
        ExePath = Join-Path $artifacts ("ProtonsSetup-{0}.exe" -f $ver)
    }
}

$suiteName = 'POWER-RECOVERY'
$ctx = $null
$store = $null
$files = $null
$fatalMessage = ''
$recoverInstallDuration = 0.0
$killed = $false
$finalExitCode = 1

try {
    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    if (-not (Test-IsAdmin)) {
        throw 'run this script in an elevated PowerShell session.'
    }

    Ensure-File -Path $ctx.ExePath -Label 'Inno artifact'

    $interruptLog = Join-Path $ctx.LogDir ("power-recovery-interrupt-{0}.log" -f $ctx.Timestamp)
    $recoverInstallLog = Join-Path $ctx.LogDir ("power-recovery-install-{0}.log" -f $ctx.Timestamp)
    $recoverUninstallLog = Join-Path $ctx.LogDir ("power-recovery-uninstall-{0}.log" -f $ctx.Timestamp)
    $installDir = Join-Path ${env:ProgramFiles} 'Protons'
    $exePath = Join-Path $installDir 'Protons.UI.exe'

    # Pre-test cleanup: ensure clean state regardless of previous test runs.
    $desktopShortcutClean = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) 'Protons Login.lnk'
    $startMenuDirClean = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) 'Protons'
    Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $desktopShortcutClean -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $startMenuDirClean -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'HKLM:\Software\Protons' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'HKCU:\Software\Protons' -Recurse -Force -ErrorAction SilentlyContinue

    $interruptArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /PROTONS_SLOW_INSTALL=1 /LOG=`"$interruptLog`""
    $proc = Start-Process -FilePath $ctx.ExePath -ArgumentList $interruptArgs -PassThru
    Start-Sleep -Seconds 2
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        $killed = $true
    }
    Add-Result -Store $store -Id 'POWER-RECOVERY-01-INTERRUPT' -Status ($(if ($killed) { 'PASS' } else { 'FAIL' })) -Details "Power-failure simulation kill executed: $killed" -Evidence $interruptLog

    # Wait for OS to release file handles after abrupt kill.
    if ($killed) {
        Start-Sleep -Seconds 5
    }

    # Recovery install must still succeed after interruption.
    $recoverArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=`"$recoverInstallLog`""
    $recoverExitCode = 0
    $recoverInstallDuration = Get-CommandDuration {
        $recoverProc = Start-Process -FilePath $ctx.ExePath -ArgumentList $recoverArgs -Wait -PassThru
        $script:recoverExitCode = $recoverProc.ExitCode
    }
    if ($recoverExitCode -ne 0) {
        Add-Result -Store $store -Id 'POWER-RECOVERY-02-RECOVER-INSTALL' -Status 'FAIL' -Details "Recovery install failed with exit code ${recoverExitCode} in ${recoverInstallDuration}s" -Evidence $recoverInstallLog
    } else {
        Add-Result -Store $store -Id 'POWER-RECOVERY-02-RECOVER-INSTALL' -Status 'PASS' -Details "Recovery install succeeded in ${recoverInstallDuration}s" -Evidence $recoverInstallLog
    }
    Add-Result -Store $store -Id 'POWER-RECOVERY-03-EXE-AVAILABLE' -Status ($(if (Test-Path $exePath) { 'PASS' } else { 'FAIL' })) -Details "Executable check after recovery: $exePath"

    # Cleanup after verification.
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (-not (Test-Path $uninstaller)) {
        Add-Result -Store $store -Id 'POWER-RECOVERY-04-UNINSTALL-CMD' -Status 'FAIL' -Details "Recovery uninstaller not found: $uninstaller"
    } else {
        $uninstallExitCode = 0
        $uninstallArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$recoverUninstallLog`""
        $uninstallDuration = Get-CommandDuration {
            $uninstallProc = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArgs -Wait -PassThru
            $script:uninstallExitCode = $uninstallProc.ExitCode
        }
        if ($uninstallExitCode -ne 0) {
            Add-Result -Store $store -Id 'POWER-RECOVERY-04-UNINSTALL-CMD' -Status 'FAIL' -Details "Recovery uninstall failed with exit code ${uninstallExitCode} in ${uninstallDuration}s" -Evidence $recoverUninstallLog
        } else {
            Add-Result -Store $store -Id 'POWER-RECOVERY-04-UNINSTALL-CMD' -Status 'PASS' -Details "Recovery uninstall succeeded in ${uninstallDuration}s" -Evidence $recoverUninstallLog
        }
    }

    $dirWait = Wait-Condition -Description 'Power recovery cleanup' -TimeoutSeconds 30 -PollIntervalMilliseconds 1000 -Condition {
        -not (Test-Path $installDir)
    }
    Add-Result -Store $store -Id 'POWER-RECOVERY-05-PROGRAMFILES-CLEAN' -Status ($(if ($dirWait.Satisfied) { 'PASS' } else { 'FAIL' })) -Details "Install dir removed=$($dirWait.Satisfied); waited=$($dirWait.ElapsedSeconds)s"
} catch {
    $fatalMessage = $_.Exception.Message
    if (-not $ctx) {
        $ctx = New-FallbackContext -InputProjectRoot $ProjectRoot -InputVersion $Version -InputArtifactsDir $ArtifactsDir -InputLogDir $LogDir -InputRunId $RunId
    }
    if (-not $store) {
        $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp
    }
    Add-Result -Store $store -Id 'POWER-RECOVERY-00-FATAL' -Status 'FAIL' -Details ("Unhandled exception: {0}" -f $fatalMessage)
} finally {
    if (-not $ctx) {
        $ctx = New-FallbackContext -InputProjectRoot $ProjectRoot -InputVersion $Version -InputArtifactsDir $ArtifactsDir -InputLogDir $LogDir -InputRunId $RunId
    }
    if (-not $store) {
        $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp
    }

    $resultBase = Join-Path $ctx.LogDir ("power-recovery-results-{0}" -f $ctx.Timestamp)
    try {
        $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase
    } catch {
        Write-Host "FATAL: failed to write result files: $($_.Exception.Message)" -ForegroundColor Red
        $files = $null
    }

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        duration_seconds = [ordered]@{
            recovery_install = [double]$recoverInstallDuration
        }
        interrupted = [bool]$killed
        fatal_exception = $fatalMessage
    }
    $metricsPath = Join-Path $ctx.LogDir ("power-recovery-metrics-{0}.json" -f $ctx.Timestamp)
    try {
        Write-JsonNoBom -Value $metrics -Path $metricsPath -Depth 6
    } catch {
        Write-Host "WARN: failed to write metrics: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    if ($files) {
        Write-Host "Result JSON: $($files.Json)"
        Write-Host "Result MD:   $($files.Markdown)"
    }
    Write-Host "Metrics:     $metricsPath"

    $summary = if ($files) { $files.Summary } else { Get-ResultCounts -Store $store }
    if ($fatalMessage) {
        $finalExitCode = 1
    } elseif ($summary.FAIL -gt 0) {
        $finalExitCode = 1
    } elseif ($summary.MANUAL -gt 0 -and -not $AllowManual) {
        $finalExitCode = 2
    } else {
        $finalExitCode = 0
    }
}

exit $finalExitCode
