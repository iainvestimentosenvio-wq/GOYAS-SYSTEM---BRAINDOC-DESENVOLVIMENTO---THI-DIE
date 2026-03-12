# Static UX contract checks for installer UX/P10 cycle.

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

    $suiteName = 'UX-CONTRACT'
    $store = New-ResultStore -Suite $suiteName -Version $ctx.Version -Timestamp $ctx.Timestamp

    $innoPath = Join-Path $ctx.ProjectRoot 'INSTALADOR\windows\innosetup\protons-setup.iss'
    $wixProductPath = Join-Path $ctx.ProjectRoot 'INSTALADOR\windows\wix\Product.wxs'
    Ensure-File -Path $innoPath -Label 'Inno script'
    Ensure-File -Path $wixProductPath -Label 'WiX Product'

    $inno = Get-Content $innoPath -Raw
    $wix = Get-Content $wixProductPath -Raw

    Add-Result -Store $store -Id 'UX-CONTRACT-01-INNO-MODERN' -Status ($(if ($inno -match '(?im)^WizardStyle\s*=\s*modern') { 'PASS' } else { 'FAIL' })) -Details 'Inno should use WizardStyle=modern' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-02-PROGRESS-LABEL' -Status ($(if ($inno -match 'ProgressStageLabel') { 'PASS' } else { 'FAIL' })) -Details 'Custom progress stage label should exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-03-ETA-LABEL' -Status ($(if ($inno -match 'EtaLabel' -and $inno -match 'CurInstallProgressChanged') { 'PASS' } else { 'FAIL' })) -Details 'ETA label and CurInstallProgressChanged hook should exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-04-CANCEL-CONFIRM' -Status ($(if ($inno -match 'CancelConfirmMessage' -and $inno -match 'CancelButtonClick') { 'PASS' } else { 'FAIL' })) -Details 'Cancel confirmation UX should exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-05-ROLLBACK-SWITCH' -Status ($(if ($inno -match 'PROTONS_ROLLBACK_TEST=1') { 'PASS' } else { 'FAIL' })) -Details 'Inno rollback failpoint switch should exist' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-06-SLOW-SWITCH' -Status ($(if ($inno -match 'PROTONS_SLOW_INSTALL=1') { 'PASS' } else { 'FAIL' })) -Details 'Inno slow-install switch should exist for interruption tests' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-07-WIX-BRANDING' -Status ($(if ($wix -match 'ARPPRODUCTICON') { 'PASS' } else { 'FAIL' })) -Details 'MSI baseline branding should keep ARPPRODUCTICON' -Evidence $wixProductPath
    Add-Result -Store $store -Id 'UX-CONTRACT-08-WIX-ROLLBACK-PROP' -Status ($(if ($wix -match 'PROTONS_ROLLBACK_TEST') { 'PASS' } else { 'FAIL' })) -Details 'MSI rollback failpoint property should exist' -Evidence $wixProductPath
    $hasEnglishLanguage = $inno -match '(?im)^\s*Name:\s*"en"\s*;'
    $hasEnglishCustomMessages = $inno -match '(?im)^\s*en\.'
    Add-Result -Store $store -Id 'UX-CONTRACT-09-PTBR-ONLY' -Status ($(if (-not $hasEnglishLanguage -and -not $hasEnglishCustomMessages) { 'PASS' } else { 'FAIL' })) -Details 'Inno should keep PT-BR only language messages in this cycle' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-10-BACK-IMAGE' -Status ($(if ($inno -match '(?im)^WizardBackImageFile\s*=\s*\.\.\\ativos\\installer\\wizard_back_light\.png') { 'PASS' } else { 'FAIL' })) -Details 'Inno should define light wizard background image' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-11-SMALL-IMAGE' -Status ($(if ($inno -match '(?im)^WizardSmallImageFile\s*=\s*\.\.\\ativos\\installer\\wizard_small_logo_light\.png') { 'PASS' } else { 'FAIL' })) -Details 'Inno should define light wizard small image/logo' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-12-NO-LANG-DIALOG' -Status ($(if ($inno -match '(?im)^ShowLanguageDialog\s*=\s*no') { 'PASS' } else { 'FAIL' })) -Details 'Language selector dialog should stay disabled for PT-BR-only flow' -Evidence $innoPath
    $hasDynamicStyle = $inno -match '(?im)^WizardStyle\s*=\s*.*\bmodern\b.*\bdynamic\b.*\bhidebevels\b.*\bexcludelightcontrols\b'
    Add-Result -Store $store -Id 'UX-CONTRACT-13-DYNAMIC-STYLE' -Status ($(if ($hasDynamicStyle) { 'PASS' } else { 'FAIL' })) -Details 'Inno should use dynamic Windows 11 wizard style profile' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-14-BACK-IMAGE-DARK' -Status ($(if ($inno -match '(?im)^WizardBackImageFileDynamicDark\s*=\s*\.\.\\ativos\\installer\\wizard_back_dark\.png') { 'PASS' } else { 'FAIL' })) -Details 'Inno should define dark wizard background image' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-15-SMALL-IMAGE-DARK' -Status ($(if ($inno -match '(?im)^WizardSmallImageFileDynamicDark\s*=\s*\.\.\\ativos\\installer\\wizard_small_logo_dark\.png') { 'PASS' } else { 'FAIL' })) -Details 'Inno should define dark wizard small image/logo' -Evidence $innoPath
    Add-Result -Store $store -Id 'UX-CONTRACT-16-BACK-COLOR-DARK' -Status ($(if ($inno -match '(?im)^WizardBackColorDynamicDark\s*=\s*\$[0-9A-Fa-f]{6}$') { 'PASS' } else { 'FAIL' })) -Details 'Inno should define dark background color for dynamic mode' -Evidence $innoPath
    $hasPtbrEntry = $inno -match '(?im)^\s*Name:\s*"ptbr"\s*;'
    $hasLanguageDialogDisabled = $inno -match '(?im)^ShowLanguageDialog\s*=\s*no'
    Add-Result -Store $store -Id 'UX-CONTRACT-17-PTBR-ONLY-STILL-ON' -Status ($(if ($hasPtbrEntry -and $hasLanguageDialogDisabled -and -not $hasEnglishLanguage -and -not $hasEnglishCustomMessages) { 'PASS' } else { 'FAIL' })) -Details 'PT-BR-only policy must remain active after dynamic UX upgrades' -Evidence $innoPath

    $resultBase = Join-Path $ctx.LogDir ("ux-contract-results-{0}" -f $ctx.Timestamp)
    $files = Write-ResultFiles -Store $store -OutputBasePath $resultBase

    $metrics = [ordered]@{
        suite = $suiteName
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    $metricsPath = Join-Path $ctx.LogDir ("ux-contract-metrics-{0}.json" -f $ctx.Timestamp)
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
