# Automated MSI validation for Protons installer.

[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$Version,
    [string]$ArtifactsDir,
    [string]$LogDir,
    [string]$RunId,
    [switch]$InteractiveInstall,
    [switch]$SkipUninstall,
    [switch]$RequireManualAppOpen,
    [switch]$AllowManual
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Test-Common.ps1')

$suiteName = 'MSI-E2E'
$ctx = $null
$store = $null

try {
    if (-not (Test-IsAdmin)) {
        Write-Host 'ERROR: run this script in an elevated PowerShell session.' -ForegroundColor Red
        exit 1
    }

    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId
    Ensure-File -Path $ctx.MsiPath -Label 'MSI artifact'
    $contract = Get-InstallerContract
    $paths = $contract.paths

    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $installLog = Join-Path $ctx.LogDir ("msi-install-{0}.log" -f $ctx.Timestamp)
    $uninstallLog = Join-Path $ctx.LogDir ("msi-uninstall-{0}.log" -f $ctx.Timestamp)
    $uninstallDuration = 0.0

    Write-Host ("=== {0} ===" -f $suiteName) -ForegroundColor Cyan
    Write-Host ("Version: {0}" -f $ctx.Version) -ForegroundColor Gray
    Write-Host ("MSI: {0}" -f $ctx.MsiPath) -ForegroundColor Gray

    $installArgs = if ($InteractiveInstall) {
        "/i `"$($ctx.MsiPath)`" /L*v `"$installLog`" /norestart"
    } else {
        "/i `"$($ctx.MsiPath)`" /qn /L*v `"$installLog`" /norestart"
    }

    $installDuration = Get-CommandDuration {
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $installArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) {
            throw "MSI install failed with exit code $($proc.ExitCode)"
        }
    }
    Add-Result -Store $store -Id 'MSI-INSTALL-CMD' -Status 'PASS' -Details "Install command succeeded in ${installDuration}s" -Evidence $installLog

    $installDir = [Environment]::ExpandEnvironmentVariables([string]$paths.install_dir)
    $exePath = Join-Path $installDir ([string]$paths.main_executable)
    Add-Result -Store $store -Id 'MSI-01-EXE' -Status ($(if (Test-Path $exePath) { 'PASS' } else { 'FAIL' })) -Details "Executable check: $exePath"

    $desktopShortcut = [Environment]::ExpandEnvironmentVariables([string]$paths.desktop_shortcut)
    Add-Result -Store $store -Id 'MSI-02-DESKTOP' -Status ($(if (Test-Path $desktopShortcut) { 'PASS' } else { 'FAIL' })) -Details "Desktop shortcut check: $desktopShortcut"

    $startMenuShortcut = [Environment]::ExpandEnvironmentVariables([string]$paths.start_menu_shortcut)
    Add-Result -Store $store -Id 'MSI-03-STARTMENU' -Status ($(if (Test-Path $startMenuShortcut) { 'PASS' } else { 'FAIL' })) -Details "Start menu shortcut check: $startMenuShortcut"

    $regPath = [string]$paths.registry_hklm
    $legacyRegPath = [string]$paths.registry_hkcu_legacy
    $regStatus = 'FAIL'
    $regDetails = 'Registry key not found in HKLM'
    if (Test-Path $regPath) {
        try {
            $regValues = Get-ItemProperty -Path $regPath -ErrorAction Stop
            if ($regValues.InstallPath -and $regValues.Version) {
                $regStatus = 'PASS'
                $regDetails = "HKLM InstallPath=$($regValues.InstallPath); Version=$($regValues.Version)"
            } else {
                $regDetails = 'HKLM registry key exists but required values are missing'
            }
        } catch {
            $regDetails = "HKLM registry read failed: $($_.Exception.Message)"
        }
    } elseif (Test-Path $legacyRegPath) {
        $regDetails = 'HKLM missing; legacy HKCU key exists (diagnostic fallback only)'
    }
    Add-Result -Store $store -Id 'MSI-04-REGISTRY' -Status $regStatus -Details $regDetails

    $appDataPath = [Environment]::ExpandEnvironmentVariables([string]$paths.appdata_dir)
    $localAppDataPath = [Environment]::ExpandEnvironmentVariables([string]$paths.localappdata_dir)
    Add-Result -Store $store -Id 'MSI-05-APPDATA' -Status ($(if (Test-Path $appDataPath) { 'PASS' } else { 'FAIL' })) -Details "AppData path check: $appDataPath"
    Add-Result -Store $store -Id 'MSI-06-LOCALAPPDATA' -Status ($(if (-not (Test-Path $localAppDataPath)) { 'PASS' } else { 'FAIL' })) -Details "LocalAppData must not exist: $localAppDataPath"

    if ($RequireManualAppOpen) {
        Add-Result -Store $store -Id 'MSI-07-APP-OPEN' -Status 'MANUAL' -Details 'Manual app open verification requested'
    } else {
        if (Test-Path $exePath) {
            $proc = Start-Process -FilePath $exePath -PassThru
            Start-Sleep -Seconds 4
            if (-not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                Add-Result -Store $store -Id 'MSI-07-APP-OPEN' -Status 'PASS' -Details 'App process started successfully'
            } elseif ($proc.ExitCode -eq 0) {
                Add-Result -Store $store -Id 'MSI-07-APP-OPEN' -Status 'PASS' -Details 'App started and exited with code 0'
            } else {
                Add-Result -Store $store -Id 'MSI-07-APP-OPEN' -Status 'FAIL' -Details "App exited with code $($proc.ExitCode)"
            }
        } else {
            Add-Result -Store $store -Id 'MSI-07-APP-OPEN' -Status 'FAIL' -Details 'Executable is missing, app launch skipped'
        }
    }

    $sentinelPath = Join-Path $appDataPath ("sentinel-{0}.txt" -f $ctx.Timestamp)
    if (Test-Path $appDataPath) {
        "sentinel:$($ctx.Timestamp)" | Set-Content -Path $sentinelPath -Encoding ASCII
    }

    if (-not $SkipUninstall) {
        $uninstallArgs = "/x `"$($ctx.MsiPath)`" /qn /L*v `"$uninstallLog`" /norestart"
        $uninstallDuration = Get-CommandDuration {
            $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $uninstallArgs -Wait -PassThru
            if ($proc.ExitCode -ne 0) {
                throw "MSI uninstall failed with exit code $($proc.ExitCode)"
            }
        }
        Add-Result -Store $store -Id 'MSI-UNINSTALL-CMD' -Status 'PASS' -Details "Uninstall command succeeded in ${uninstallDuration}s" -Evidence $uninstallLog

        $programFilesWait = Wait-Condition -Description 'Program Files cleanup' -TimeoutSeconds 30 -PollIntervalMilliseconds 1000 -Condition {
            -not (Test-Path $installDir)
        }
        $programFilesStatus = if ($programFilesWait.Satisfied) { 'PASS' } else { 'FAIL' }
        Add-Result -Store $store -Id 'MSI-UNINST-01-PROGRAMFILES' -Status $programFilesStatus -Details "Program Files cleanup: $installDir | waited=$($programFilesWait.ElapsedSeconds)s | exists=$((Test-Path $installDir))"
        Add-Result -Store $store -Id 'MSI-UNINST-02-DESKTOP' -Status ($(if (-not (Test-Path $desktopShortcut)) { 'PASS' } else { 'FAIL' })) -Details "Desktop shortcut removal: $desktopShortcut"

        $registryWait = Wait-Condition -Description 'Registry cleanup (HKLM + legacy HKCU)' -TimeoutSeconds 30 -PollIntervalMilliseconds 1000 -Condition {
            (-not (Test-Path $regPath)) -and (-not (Test-Path $legacyRegPath))
        }
        $registryStatus = if ($registryWait.Satisfied) { 'PASS' } else { 'FAIL' }
        $registryExistsHklm = Test-Path $regPath
        $registryExistsHkcu = Test-Path $legacyRegPath
        $registryDetails = "Registry cleanup: HKLM=$regPath exists=$registryExistsHklm; HKCU(legacy)=$legacyRegPath exists=$registryExistsHkcu; waited=$($registryWait.ElapsedSeconds)s"
        if (-not $registryWait.Satisfied -and $registryWait.LastError) {
            $registryDetails = "$registryDetails; lastError=$($registryWait.LastError)"
        }
        Add-Result -Store $store -Id 'MSI-UNINST-03-REGISTRY' -Status $registryStatus -Details $registryDetails

        Add-Result -Store $store -Id 'MSI-UNINST-04-APPDATA-PRESERVED' -Status ($(if (Test-Path $appDataPath) { 'PASS' } else { 'FAIL' })) -Details "AppData preserved: $appDataPath"
        Add-Result -Store $store -Id 'MSI-UNINST-05-SENTINEL-PRESERVED' -Status ($(if (Test-Path $sentinelPath) { 'PASS' } else { 'FAIL' })) -Details "Sentinel preserved: $sentinelPath"
    }

    $resultBase = Join-Path $ctx.LogDir ("msi-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        artifact = [ordered]@{
            path = $ctx.MsiPath
            size_bytes = (Get-Item $ctx.MsiPath).Length
        }
        duration_seconds = [ordered]@{
            install = $installDuration
            uninstall = $(if ($SkipUninstall) { 0 } else { $uninstallDuration })
        }
    }
    $metricsPath = Join-Path $ctx.LogDir ("msi-metrics-{0}.json" -f $ctx.Timestamp)
    Write-JsonNoBom -Value $metrics -Path $metricsPath -Depth 6

    Write-Host "Result JSON: $($files.Json)"
    Write-Host "Result MD:   $($files.Markdown)"
    Write-Host "Metrics:     $metricsPath"

    $failCount = $files.Summary.FAIL
    $manualCount = $files.Summary.MANUAL

    if ($failCount -gt 0) {
        exit 1
    }

    if ($manualCount -gt 0 -and -not $AllowManual) {
        exit 2
    }

    exit 0
} catch {
    $fatalMessage = $_.Exception.Message
    Write-Host "FATAL: $fatalMessage" -ForegroundColor Red

    try {
        if ($null -eq $ctx) {
            $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId
        }
        if ($null -eq $store) {
            $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp
        }

        Add-Result -Store $store -Id 'MSI-FATAL' -Status 'FAIL' -Details $fatalMessage
        $resultBase = Join-Path $ctx.LogDir ("msi-results-{0}" -f $ctx.Timestamp)
        $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

        $metricsPath = Join-Path $ctx.LogDir ("msi-metrics-{0}.json" -f $ctx.Timestamp)
        if (-not (Test-Path $metricsPath)) {
            $metrics = [ordered]@{
                suite = $suiteName
                version = $ctx.Version
                run_id = $ctx.RunId
                timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
                fatal_error = $fatalMessage
                artifact = [ordered]@{
                    path = $ctx.MsiPath
                    size_bytes = $(if (Test-Path $ctx.MsiPath) { (Get-Item $ctx.MsiPath).Length } else { 0 })
                }
                duration_seconds = [ordered]@{
                    install = 0
                    uninstall = 0
                }
            }
            Write-JsonNoBom -Value $metrics -Path $metricsPath -Depth 6
        }

        Write-Host "Result JSON: $($files.Json)"
        Write-Host "Result MD:   $($files.Markdown)"
        Write-Host "Metrics:     $metricsPath"
    } catch {
        Write-Host "FATAL: unable to emit fallback MSI result: $($_.Exception.Message)" -ForegroundColor Red
    }

    exit 1
}
