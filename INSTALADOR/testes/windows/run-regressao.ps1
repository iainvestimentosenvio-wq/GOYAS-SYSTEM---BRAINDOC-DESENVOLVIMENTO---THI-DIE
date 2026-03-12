# Unified Windows regression runner for Protons installers.

[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$Version,
    [string]$ArtifactsDir,
    [string]$LogDir,
    [string]$RunId,
    [switch]$Interactive,
    [switch]$AllowManual,
    [switch]$SkipMsi,
    [switch]$SkipInno,
    [switch]$SkipUpgrade,
    [switch]$SkipHashAndSignature,
    [switch]$StrictSignature,
    [ValidateSet('technical','production')]
    [string]$SignatureProfile = 'technical',
    [switch]$IgnorePerformanceGate,
    [switch]$EnableResilienceSuite,
    [switch]$EnableUxSuite,
    [switch]$RequireDefenderActive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Test-Common.ps1')

function Invoke-TestScript {
    param(
        [Parameter(Mandatory=$true)][string]$ScriptPath,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][hashtable]$CommonArgs,
        [switch]$Interactive,
        [switch]$AllowManual,
        [switch]$PassInteractive = $true,
        [switch]$PassManualAppOpen = $true,
        [string[]]$AdditionalArgs = @()
    )

    $argList = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $ScriptPath,
        '-ProjectRoot', $CommonArgs.ProjectRoot,
        '-Version', $CommonArgs.Version,
        '-ArtifactsDir', $CommonArgs.ArtifactsDir,
        '-LogDir', $CommonArgs.LogDir,
        '-RunId', $CommonArgs.RunId
    )

    if ($Interactive -and $PassInteractive) { $argList += '-InteractiveInstall' }
    if ($AllowManual) {
        $argList += '-AllowManual'
        if ($PassManualAppOpen) { $argList += '-RequireManualAppOpen' }
    }
    if ($AdditionalArgs -and $AdditionalArgs.Count -gt 0) {
        $argList += $AdditionalArgs
    }

    Write-Host "Running $Name..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Wait -PassThru

    return [ordered]@{
        name = $Name
        exit_code = $proc.ExitCode
        status = $(if ($proc.ExitCode -eq 0) { 'PASS' } elseif ($proc.ExitCode -eq 2) { 'MANUAL' } else { 'FAIL' })
    }
}

function Set-StepFail {
    param(
        [Parameter(Mandatory=$true)][array]$Steps,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][string]$Reason
    )

    for ($i = 0; $i -lt $Steps.Count; $i++) {
        if ($Steps[$i].name -eq $Name) {
            $Steps[$i].status = 'FAIL'
            if ($Steps[$i].exit_code -eq 0) {
                $Steps[$i].exit_code = 91
            }
            $Steps[$i].validation = $Reason
            break
        }
    }
}

function Validate-StepResultFile {
    param(
        [Parameter(Mandatory=$true)][array]$Steps,
        [Parameter(Mandatory=$true)][string]$StepName,
        [Parameter(Mandatory=$true)][string]$ResultPath
    )

    if (-not (Test-Path $ResultPath)) {
        Set-StepFail -Steps $Steps -Name $StepName -Reason "missing result file: $ResultPath"
        return
    }

    try {
        $result = Read-JsonFlexible -Path $ResultPath
        if (-not $result.summary -or [int]$result.summary.TOTAL -le 0) {
            Set-StepFail -Steps $Steps -Name $StepName -Reason "invalid summary TOTAL<=0 in $ResultPath"
        }
    } catch {
        Set-StepFail -Steps $Steps -Name $StepName -Reason "invalid json result in $($ResultPath): $($_.Exception.Message)"
    }
}

try {
    if (-not (Test-IsAdmin)) {
        Write-Host 'ERROR: run this script in an elevated PowerShell session.' -ForegroundColor Red
        exit 1
    }

    if (-not $RunId) {
        $RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    }

    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId

    if ($StrictSignature -and $SignatureProfile -eq 'technical') {
        $SignatureProfile = 'production'
    }

    $runId = $ctx.RunId
    $reportBase = Join-Path $ctx.LogDir ("regressao-windows-{0}" -f $runId)
    $summaryMdPath = "${reportBase}.md"
    $summaryJsonPath = "${reportBase}.json"

    $commonArgs = [ordered]@{
        ProjectRoot = $ctx.ProjectRoot
        Version = $ctx.Version
        ArtifactsDir = $ctx.ArtifactsDir
        LogDir = $ctx.LogDir
        RunId = $runId
    }

    $steps = @()

    if (-not $SkipMsi) {
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-msi.ps1') -Name 'MSI-E2E' -CommonArgs $commonArgs -Interactive:$Interactive -AllowManual:$AllowManual -PassManualAppOpen
    }

    if (-not $SkipInno) {
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-inno.ps1') -Name 'INNO-E2E' -CommonArgs $commonArgs -Interactive:$Interactive -AllowManual:$AllowManual -PassManualAppOpen
    }

    if (-not $SkipUpgrade) {
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-upgrade-reinstall.ps1') -Name 'UPGRADE-REINSTALL-E2E' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
    }

    if (-not $SkipHashAndSignature) {
        $hashScript = Join-Path $PSScriptRoot 'verify-artifacts.ps1'
        $hashArgs = @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $hashScript,
            '-ProjectRoot', $ctx.ProjectRoot,
            '-Version', $ctx.Version,
            '-ArtifactsDir', $ctx.ArtifactsDir,
            '-LogDir', $ctx.LogDir,
            '-RunId', $runId,
            '-SignatureProfile', $SignatureProfile
        )
        if ($StrictSignature) { $hashArgs += '-RequireSignature' }

        $proc = Start-Process -FilePath 'powershell.exe' -ArgumentList $hashArgs -Wait -PassThru
        $steps += [ordered]@{
            name = 'ARTIFACT-VERIFY'
            exit_code = $proc.ExitCode
            status = $(if ($proc.ExitCode -eq 0) { 'PASS' } else { 'FAIL' })
        }
    }

    if ($EnableResilienceSuite) {
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-msi-rollback.ps1') -Name 'MSI-ROLLBACK' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-inno-rollback.ps1') -Name 'INNO-ROLLBACK' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-transactional-install.ps1') -Name 'TX-INSTALL' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-power-failure-recovery.ps1') -Name 'POWER-RECOVERY' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-msi-repair.ps1') -Name 'MSI-REPAIR' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
    }

    if ($EnableUxSuite) {
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-ux-contract.ps1') -Name 'UX-CONTRACT' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-cancel-install.ps1') -Name 'CANCEL-INSTALL' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-time-estimate.ps1') -Name 'TIME-ESTIMATE' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false
    }

    if ($RequireDefenderActive) {
        $steps += Invoke-TestScript -ScriptPath (Join-Path $PSScriptRoot 'test-defender-compatibility.ps1') -Name 'DEFENDER-COMPAT' -CommonArgs $commonArgs -AllowManual:$AllowManual -PassInteractive:$false -PassManualAppOpen:$false -AdditionalArgs @('-RequireDefenderActive')
    }

    if (-not $SkipMsi) {
        $msiResultPath = Join-Path $ctx.LogDir ("msi-results-{0}.json" -f $runId)
        Validate-StepResultFile -Steps $steps -StepName 'MSI-E2E' -ResultPath $msiResultPath
    }

    if (-not $SkipInno) {
        $innoResultPath = Join-Path $ctx.LogDir ("inno-results-{0}.json" -f $runId)
        Validate-StepResultFile -Steps $steps -StepName 'INNO-E2E' -ResultPath $innoResultPath
    }

    if ($EnableResilienceSuite) {
        Validate-StepResultFile -Steps $steps -StepName 'MSI-ROLLBACK' -ResultPath (Join-Path $ctx.LogDir ("msi-rollback-results-{0}.json" -f $runId))
        Validate-StepResultFile -Steps $steps -StepName 'INNO-ROLLBACK' -ResultPath (Join-Path $ctx.LogDir ("inno-rollback-results-{0}.json" -f $runId))
        Validate-StepResultFile -Steps $steps -StepName 'TX-INSTALL' -ResultPath (Join-Path $ctx.LogDir ("transactional-results-{0}.json" -f $runId))
        Validate-StepResultFile -Steps $steps -StepName 'POWER-RECOVERY' -ResultPath (Join-Path $ctx.LogDir ("power-recovery-results-{0}.json" -f $runId))
        Validate-StepResultFile -Steps $steps -StepName 'MSI-REPAIR' -ResultPath (Join-Path $ctx.LogDir ("msi-repair-results-{0}.json" -f $runId))
    }

    if ($EnableUxSuite) {
        Validate-StepResultFile -Steps $steps -StepName 'UX-CONTRACT' -ResultPath (Join-Path $ctx.LogDir ("ux-contract-results-{0}.json" -f $runId))
        Validate-StepResultFile -Steps $steps -StepName 'CANCEL-INSTALL' -ResultPath (Join-Path $ctx.LogDir ("cancel-install-results-{0}.json" -f $runId))
        Validate-StepResultFile -Steps $steps -StepName 'TIME-ESTIMATE' -ResultPath (Join-Path $ctx.LogDir ("time-estimate-results-{0}.json" -f $runId))
    }

    if ($RequireDefenderActive) {
        Validate-StepResultFile -Steps $steps -StepName 'DEFENDER-COMPAT' -ResultPath (Join-Path $ctx.LogDir ("defender-results-{0}.json" -f $runId))
    }

    $statusCounts = [ordered]@{
        PASS = @($steps | Where-Object { $_.status -eq 'PASS' }).Count
        FAIL = @($steps | Where-Object { $_.status -eq 'FAIL' }).Count
        MANUAL = @($steps | Where-Object { $_.status -eq 'MANUAL' }).Count
        TOTAL = $steps.Count
    }

    $metricsFiles = Get-ChildItem -Path $ctx.LogDir -Filter "*-metrics-$runId.json" -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime
    $metrics = @()
    foreach ($file in $metricsFiles) {
        try {
            $metrics += (Read-JsonFlexible -Path $file.FullName)
        } catch {
        }
    }

    function Get-P95FromValues {
        param([double[]]$Values)

        if (-not $Values -or $Values.Count -eq 0) { return 0 }
        $sorted = @($Values | Sort-Object)
        $idx = [Math]::Ceiling(0.95 * $sorted.Count) - 1
        if ($idx -lt 0) { $idx = 0 }
        return [Math]::Round($sorted[$idx], 2)
    }

    $msiInstallValues = @($metrics | Where-Object { $_.suite -eq 'MSI-E2E' } | ForEach-Object { [double]$_.duration_seconds.install })
    $msiUninstallValues = @($metrics | Where-Object { $_.suite -eq 'MSI-E2E' } | ForEach-Object { [double]$_.duration_seconds.uninstall })
    $innoInstallValues = @($metrics | Where-Object { $_.suite -eq 'INNO-E2E' } | ForEach-Object { [double]$_.duration_seconds.install })
    $innoUninstallValues = @($metrics | Where-Object { $_.suite -eq 'INNO-E2E' } | ForEach-Object { [double]$_.duration_seconds.uninstall })

    $msiInstallP95 = Get-P95FromValues -Values $msiInstallValues
    $msiUninstallP95 = Get-P95FromValues -Values $msiUninstallValues
    $innoInstallP95 = Get-P95FromValues -Values $innoInstallValues
    $innoUninstallP95 = Get-P95FromValues -Values $innoUninstallValues

    $targets = [ordered]@{
        msi_install_p95_max = 90
        msi_uninstall_p95_max = 45
        inno_install_p95_max = 90
        inno_uninstall_p95_max = 45
    }

    $performanceChecks = @()
    if (-not $SkipMsi) {
        $performanceChecks += [ordered]@{
            metric = 'msi_install_p95_seconds'
            value = $msiInstallP95
            max = $targets.msi_install_p95_max
            available = ($msiInstallValues.Count -gt 0)
            pass = ($msiInstallValues.Count -gt 0 -and $msiInstallP95 -le $targets.msi_install_p95_max)
        }
        $performanceChecks += [ordered]@{
            metric = 'msi_uninstall_p95_seconds'
            value = $msiUninstallP95
            max = $targets.msi_uninstall_p95_max
            available = ($msiUninstallValues.Count -gt 0)
            pass = ($msiUninstallValues.Count -gt 0 -and $msiUninstallP95 -le $targets.msi_uninstall_p95_max)
        }
    }

    if (-not $SkipInno) {
        $performanceChecks += [ordered]@{
            metric = 'inno_install_p95_seconds'
            value = $innoInstallP95
            max = $targets.inno_install_p95_max
            available = ($innoInstallValues.Count -gt 0)
            pass = ($innoInstallValues.Count -gt 0 -and $innoInstallP95 -le $targets.inno_install_p95_max)
        }
        $performanceChecks += [ordered]@{
            metric = 'inno_uninstall_p95_seconds'
            value = $innoUninstallP95
            max = $targets.inno_uninstall_p95_max
            available = ($innoUninstallValues.Count -gt 0)
            pass = ($innoUninstallValues.Count -gt 0 -and $innoUninstallP95 -le $targets.inno_uninstall_p95_max)
        }
    }

    $performanceFailures = @($performanceChecks | Where-Object { -not $_.pass })
    $baseGatePass = ($statusCounts.FAIL -eq 0 -and ($statusCounts.MANUAL -eq 0 -or $AllowManual))
    $performanceGateBlocked = ($performanceFailures.Count -gt 0 -and -not $IgnorePerformanceGate)

    $artifactMsi = Join-Path $ctx.ArtifactsDir ("Protons-{0}-x64.msi" -f $ctx.Version)
    $artifactExe = Join-Path $ctx.ArtifactsDir ("ProtonsSetup-{0}.exe" -f $ctx.Version)

    $summary = [ordered]@{
        suite = 'WINDOWS-REGRESSION'
        version = $ctx.Version
        run_id = $runId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        status = $(if ($baseGatePass -and -not $performanceGateBlocked) { 'GO_TECNICO_WINDOWS' } else { 'NO-GO' })
        options = [ordered]@{
            enable_resilience_suite = [bool]$EnableResilienceSuite
            enable_ux_suite = [bool]$EnableUxSuite
            require_defender_active = [bool]$RequireDefenderActive
            signature_profile = $SignatureProfile
            strict_signature = [bool]$StrictSignature
        }
        steps = $steps
        summary = $statusCounts
        gate = [ordered]@{
            base_gate_pass = [bool]$baseGatePass
            performance_gate_blocked = [bool]$performanceGateBlocked
            ignore_performance_gate = [bool]$IgnorePerformanceGate
            performance_failures = $performanceFailures
        }
        performance = [ordered]@{
            msi_install_p95_seconds = $msiInstallP95
            msi_uninstall_p95_seconds = $msiUninstallP95
            inno_install_p95_seconds = $innoInstallP95
            inno_uninstall_p95_seconds = $innoUninstallP95
            targets = $targets
            checks = $performanceChecks
        }
        artifacts = [ordered]@{
            msi = [ordered]@{
                path = $artifactMsi
                size_bytes = $(if (Test-Path $artifactMsi) { (Get-Item $artifactMsi).Length } else { 0 })
            }
            exe = [ordered]@{
                path = $artifactExe
                size_bytes = $(if (Test-Path $artifactExe) { (Get-Item $artifactExe).Length } else { 0 })
            }
        }
    }

    Write-JsonNoBom -Value $summary -Path $summaryJsonPath -Depth 8

    $md = @()
    $md += '# Regressao Windows - Protons'
    $md += ''
    $md += "- Version: $($summary.version)"
    $md += "- RunId: $($summary.run_id)"
    $md += "- TimestampUTC: $($summary.timestamp_utc)"
    $md += "- Veredito: $($summary.status)"
    $md += "- GatePerformanceIgnorado: $($summary.gate.ignore_performance_gate)"
    $md += "- ResilienceSuite: $($summary.options.enable_resilience_suite)"
    $md += "- UxSuite: $($summary.options.enable_ux_suite)"
    $md += "- RequireDefenderActive: $($summary.options.require_defender_active)"
    $md += "- SignatureProfile: $($summary.options.signature_profile)"
    $md += "- StrictSignature: $($summary.options.strict_signature)"
    $md += ''
    $md += '## Resumo'
    $md += "- PASS: $($statusCounts.PASS)"
    $md += "- FAIL: $($statusCounts.FAIL)"
    $md += "- MANUAL: $($statusCounts.MANUAL)"
    $md += "- TOTAL: $($statusCounts.TOTAL)"
    $md += ''
    $md += '## Etapas'
    $md += '| Etapa | Status | ExitCode |'
    $md += '| --- | --- | --- |'
    foreach ($step in $steps) {
        $md += "| $($step.name) | $($step.status) | $($step.exit_code) |"
    }
    $md += ''
    $md += "## Performance p95 (apenas metricas da rodada $runId)"
    $md += "- MSI install p95: $msiInstallP95 s (meta <= 90)"
    $md += "- MSI uninstall p95: $msiUninstallP95 s (meta <= 45)"
    $md += "- Inno install p95: $innoInstallP95 s (meta <= 90)"
    $md += "- Inno uninstall p95: $innoUninstallP95 s (meta <= 45)"
    if ($performanceFailures.Count -gt 0) {
        $md += ''
        $md += '### Falhas de performance (bloqueiam GO por padrao)'
        foreach ($failure in $performanceFailures) {
            if (-not $failure.available) {
                $md += "- $($failure.metric): sem metrica coletada (meta <= $($failure.max))"
            } else {
                $md += "- $($failure.metric): $($failure.value) s (meta <= $($failure.max))"
            }
        }
    }
    $md += ''
    $md += '## Artefatos'
    $md += "- MSI: $artifactMsi"
    $md += "- EXE: $artifactExe"
    $md += ''
    $md += "JSON: $summaryJsonPath"

    Write-Utf8NoBom -Path $summaryMdPath -Content (($md -join "`n") + [Environment]::NewLine)

    Write-Host "Regression JSON: $summaryJsonPath"
    Write-Host "Regression MD:   $summaryMdPath"

    if ($statusCounts.FAIL -gt 0) {
        exit 1
    }

    if ($statusCounts.MANUAL -gt 0 -and -not $AllowManual) {
        exit 2
    }

    if ($performanceGateBlocked) {
        exit 3
    }

    exit 0
} catch {
    Write-Host "FATAL: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.InvocationInfo -and $_.InvocationInfo.ScriptLineNumber) {
        Write-Host "FATAL-LINE: $($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
        if ($_.InvocationInfo.Line) {
            Write-Host "FATAL-CODE: $($_.InvocationInfo.Line.Trim())" -ForegroundColor Red
        }
    }
    exit 1
}
