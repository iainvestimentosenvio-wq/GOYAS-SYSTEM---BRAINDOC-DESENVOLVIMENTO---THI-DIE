# Validate install flows with Windows Defender active.

[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$Version,
    [string]$ArtifactsDir,
    [string]$LogDir,
    [string]$RunId,
    [switch]$AllowManual,
    [switch]$RequireDefenderActive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Test-Common.ps1')

function Invoke-MsiQuiet {
    param([string]$MsiPath, [string]$LogPath, [ValidateSet('install','uninstall')] [string]$Mode)
    $args = if ($Mode -eq 'install') {
        "/i `"$MsiPath`" /qn /L*v `"$LogPath`" /norestart"
    } else {
        "/x `"$MsiPath`" /qn /L*v `"$LogPath`" /norestart"
    }
    $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $args -Wait -PassThru
    return $proc.ExitCode
}

function Get-DefenderState {
    $mp = Get-MpComputerStatus
    $service = Get-Service -Name 'WinDefend' -ErrorAction SilentlyContinue
    $serviceStatus = if ($service) { [string]$service.Status } else { 'Unknown' }
    $serviceStartType = if ($service) { [string]$service.StartType } else { 'Unknown' }
    $avEnabled = [bool]$mp.AntivirusEnabled
    $rtEnabled = [bool]$mp.RealTimeProtectionEnabled
    return [ordered]@{
        antivirus_enabled = $avEnabled
        realtime_enabled = $rtEnabled
        active = ($avEnabled -and $rtEnabled)
        service_status = $serviceStatus
        service_start_type = $serviceStartType
    }
}

function Enable-DefenderRealtimeBestEffort {
    $actions = @()
    try {
        Set-Service -Name 'WinDefend' -StartupType Automatic -ErrorAction Stop
        $actions += 'Set-Service WinDefend=Automatic:OK'
    } catch {
        $actions += "Set-Service WinDefend=Automatic:FAIL ($($_.Exception.Message))"
    }

    try {
        Start-Service -Name 'WinDefend' -ErrorAction Stop
        $actions += 'Start-Service WinDefend:OK'
    } catch {
        $actions += "Start-Service WinDefend:FAIL ($($_.Exception.Message))"
    }

    try {
        Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction Stop
        $actions += 'Set-MpPreference DisableRealtimeMonitoring=false:OK'
    } catch {
        $actions += "Set-MpPreference DisableRealtimeMonitoring=false:FAIL ($($_.Exception.Message))"
    }

    Start-Sleep -Seconds 8
    return ($actions -join ' | ')
}

try {
    if (-not (Test-IsAdmin)) {
        Write-Host 'ERROR: run this script in an elevated PowerShell session.' -ForegroundColor Red
        exit 1
    }

    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId
    Ensure-File -Path $ctx.MsiPath -Label 'MSI artifact'
    Ensure-File -Path $ctx.ExePath -Label 'Inno artifact'

    $suiteName = 'DEFENDER-COMPAT'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $defenderCmd = Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue
    if (-not $defenderCmd) {
        if ($RequireDefenderActive) {
            Add-Result -Store $store -Id 'DEFENDER-01-STATUS' -Status 'FAIL' -Details 'Get-MpComputerStatus not available and -RequireDefenderActive requested'
        } else {
            Add-Result -Store $store -Id 'DEFENDER-01-STATUS' -Status 'SKIP' -Details 'Get-MpComputerStatus not available on this host'
        }
    } else {
        $stateBefore = Get-DefenderState
        $defenderStateDetails = "before: AntivirusEnabled=$($stateBefore.antivirus_enabled), RealTimeProtectionEnabled=$($stateBefore.realtime_enabled), ServiceStatus=$($stateBefore.service_status), StartType=$($stateBefore.service_start_type)"

        if ($RequireDefenderActive -and -not $stateBefore.active) {
            $remediation = Enable-DefenderRealtimeBestEffort
            $stateAfter = Get-DefenderState
            $defenderStateDetails = "$defenderStateDetails | remediation: $remediation | after: AntivirusEnabled=$($stateAfter.antivirus_enabled), RealTimeProtectionEnabled=$($stateAfter.realtime_enabled), ServiceStatus=$($stateAfter.service_status), StartType=$($stateAfter.service_start_type)"
            if (-not $stateAfter.active) {
                Add-Result -Store $store -Id 'DEFENDER-01-STATUS' -Status 'FAIL' -Details "Windows Defender is not active after remediation. $defenderStateDetails"
            } else {
                Add-Result -Store $store -Id 'DEFENDER-01-STATUS' -Status 'PASS' -Details "Windows Defender became active after remediation. $defenderStateDetails"
            }
        } else {
            Add-Result -Store $store -Id 'DEFENDER-01-STATUS' -Status 'PASS' -Details "Windows Defender status OK. $defenderStateDetails"
        }
    }

    $msiInstallLog = Join-Path $ctx.LogDir ("defender-msi-install-{0}.log" -f $ctx.Timestamp)
    $msiUninstallLog = Join-Path $ctx.LogDir ("defender-msi-uninstall-{0}.log" -f $ctx.Timestamp)
    $msiInstallExit = Invoke-MsiQuiet -MsiPath $ctx.MsiPath -LogPath $msiInstallLog -Mode install
    Add-Result -Store $store -Id 'DEFENDER-02-MSI-INSTALL' -Status ($(if ($msiInstallExit -eq 0) { 'PASS' } else { 'FAIL' })) -Details "MSI install exit code: $msiInstallExit" -Evidence $msiInstallLog

    $msiUninstallExit = Invoke-MsiQuiet -MsiPath $ctx.MsiPath -LogPath $msiUninstallLog -Mode uninstall
    Add-Result -Store $store -Id 'DEFENDER-03-MSI-UNINSTALL' -Status ($(if ($msiUninstallExit -eq 0) { 'PASS' } else { 'FAIL' })) -Details "MSI uninstall exit code: $msiUninstallExit" -Evidence $msiUninstallLog

    $innoInstallLog = Join-Path $ctx.LogDir ("defender-inno-install-{0}.log" -f $ctx.Timestamp)
    $innoUninstallLog = Join-Path $ctx.LogDir ("defender-inno-uninstall-{0}.log" -f $ctx.Timestamp)
    $innoInstallArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=`"$innoInstallLog`""
    $innoInstallProc = Start-Process -FilePath $ctx.ExePath -ArgumentList $innoInstallArgs -Wait -PassThru
    Add-Result -Store $store -Id 'DEFENDER-04-INNO-INSTALL' -Status ($(if ($innoInstallProc.ExitCode -eq 0) { 'PASS' } else { 'FAIL' })) -Details "Inno install exit code: $($innoInstallProc.ExitCode)" -Evidence $innoInstallLog

    $uninstaller = Join-Path ${env:ProgramFiles} 'Protons\unins000.exe'
    if (-not (Test-Path $uninstaller)) {
        Add-Result -Store $store -Id 'DEFENDER-05-INNO-UNINSTALL' -Status 'FAIL' -Details "Inno uninstaller not found: $uninstaller"
    } else {
        $innoUninstallArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$innoUninstallLog`""
        $innoUninstallProc = Start-Process -FilePath $uninstaller -ArgumentList $innoUninstallArgs -Wait -PassThru
        Add-Result -Store $store -Id 'DEFENDER-05-INNO-UNINSTALL' -Status ($(if ($innoUninstallProc.ExitCode -eq 0) { 'PASS' } else { 'FAIL' })) -Details "Inno uninstall exit code: $($innoUninstallProc.ExitCode)" -Evidence $innoUninstallLog
    }

    $resultBase = Join-Path $ctx.LogDir ("defender-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        require_defender_active = [bool]$RequireDefenderActive
    }
    $metricsPath = Join-Path $ctx.LogDir ("defender-metrics-{0}.json" -f $ctx.Timestamp)
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
