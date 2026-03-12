# Verify artifact hashes and optional digital signatures.

[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$Version,
    [string]$ArtifactsDir,
    [string]$LogDir,
    [string]$RunId,
    [switch]$RequireSignature,
    [ValidateSet('technical','production')]
    [string]$SignatureProfile = 'technical'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Test-Common.ps1')

function Resolve-SignTool {
    $paths = @(
        'C:\Program Files (x86)\Windows Kits\10\bin',
        'C:\Program Files\Windows Kits\10\bin'
    )

    foreach ($base in $paths) {
        if (Test-Path $base) {
            $candidate = Get-ChildItem -Path $base -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
            if ($candidate) {
                return $candidate.FullName
            }
        }
    }

    $inPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }

    return $null
}

try {
    $ctx = Get-ProjectContext -ProjectRoot $ProjectRoot -Version $Version -ArtifactsDir $ArtifactsDir -LogDir $LogDir -RunId $RunId

    if ($RequireSignature -and $SignatureProfile -eq 'technical') {
        $SignatureProfile = 'production'
    }
    $enforceSignature = ($SignatureProfile -eq 'production')

    Ensure-File -Path $ctx.MsiPath -Label 'MSI artifact'
    Ensure-File -Path $ctx.ExePath -Label 'EXE artifact'

    $jsonPath = Join-Path $ctx.LogDir ("artifact-verify-{0}.json" -f $ctx.RunId)
    $mdPath = Join-Path $ctx.LogDir ("artifact-verify-{0}.md" -f $ctx.RunId)

    $artifacts = @($ctx.MsiPath, $ctx.ExePath)
    $results = @()
    $signTool = Resolve-SignTool
    $signToolAvailable = [bool]$signTool

    foreach ($artifact in $artifacts) {
        $file = Get-Item $artifact
        $actualHash = (Get-FileHash -Path $artifact -Algorithm SHA256).Hash.ToLower()

        $shaFile = "${artifact}.sha256"
        $expectedHash = ''
        $hashStatus = 'FAIL'
        $hashDetails = ''

        if (Test-Path $shaFile) {
            $raw = (Get-Content $shaFile -Raw).Trim()
            $expectedHash = ($raw -split '\s+')[0].ToLower()
            if ($expectedHash -notmatch '^[a-f0-9]{64}$') {
                $hashStatus = 'FAIL'
                $hashDetails = "Expected hash format invalid in $shaFile"
            } elseif ($expectedHash -eq $actualHash) {
                $hashStatus = 'PASS'
                $hashDetails = 'SHA256 matched expected hash'
            } else {
                $hashStatus = 'FAIL'
                $hashDetails = 'SHA256 mismatch against expected hash'
            }
        } else {
            $hashStatus = 'FAIL'
            $hashDetails = "Missing expected hash file: $shaFile"
        }

        $signatureStatus = 'NOT_CHECKED'
        $signatureDetails = 'signtool not available on host'
        if ($signToolAvailable) {
            $output = & $signTool verify /pa $artifact 2>&1
            if ($LASTEXITCODE -eq 0) {
                $signatureStatus = 'PASS'
                $signatureDetails = 'Signature verified with signtool'
            } else {
                $signatureStatus = 'FAIL'
                $signatureDetails = ($output | Out-String).Trim()
            }
        } elseif ($enforceSignature) {
            $signatureStatus = 'BLOCKED_SIGNSDK'
            $signatureDetails = 'BLOCKED_SIGNSDK: signtool not available on host'
        }

        $signaturePolicyResult = 'PASS'
        if ($enforceSignature) {
            if ($signatureStatus -eq 'PASS') {
                $signaturePolicyResult = 'PASS'
            } else {
                $signaturePolicyResult = 'FAIL'
            }
        } elseif ($signatureStatus -eq 'FAIL' -or $signatureStatus -eq 'BLOCKED_SIGNSDK') {
            $signaturePolicyResult = 'WARN'
        }

        $results += [ordered]@{
            file = $artifact
            size_bytes = $file.Length
            hash = [ordered]@{
                source_file = $shaFile
                expected_sha256 = $expectedHash
                actual_sha256 = $actualHash
                status = $hashStatus
                details = $hashDetails
            }
            signature = [ordered]@{
                status = $signatureStatus
                details = $signatureDetails
                tool_available = $signToolAvailable
                signtool_path = $(if ($signToolAvailable) { $signTool } else { '' })
                require_signature = [bool]$enforceSignature
                signature_profile = $SignatureProfile
                policy_result = $signaturePolicyResult
            }
        }
    }

    $summary = [ordered]@{
        hash_pass = @($results | Where-Object { $_.hash.status -eq 'PASS' }).Count
        hash_fail = @($results | Where-Object { $_.hash.status -eq 'FAIL' }).Count
        signature_pass = @($results | Where-Object { $_.signature.status -eq 'PASS' }).Count
        signature_fail = @($results | Where-Object { $_.signature.status -eq 'FAIL' }).Count
        signature_not_checked = @($results | Where-Object { $_.signature.status -eq 'NOT_CHECKED' }).Count
        signature_blocked_signsdk = @($results | Where-Object { $_.signature.status -eq 'BLOCKED_SIGNSDK' }).Count
        signature_policy_fail = @($results | Where-Object { $_.signature.policy_result -eq 'FAIL' }).Count
        signature_policy_warn = @($results | Where-Object { $_.signature.policy_result -eq 'WARN' }).Count
    }

    $fail = ($summary.hash_fail -gt 0 -or $summary.signature_policy_fail -gt 0)

    $payload = [ordered]@{
        suite = 'ARTIFACT-VERIFY'
        version = $ctx.Version
        run_id = $ctx.RunId
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        require_signature = [bool]$enforceSignature
        signature_profile = $SignatureProfile
        signtool_available = $signToolAvailable
        signtool_path = $(if ($signToolAvailable) { $signTool } else { '' })
        summary = $summary
        results = $results
    }

    Write-JsonNoBom -Value $payload -Path $jsonPath -Depth 8

    $md = @()
    $md += '# Artifact Verification'
    $md += ''
    $md += "- Version: $($ctx.Version)"
    $md += "- RunId: $($ctx.RunId)"
    $md += "- TimestampUTC: $($payload.timestamp_utc)"
    $md += "- RequireSignature: $($payload.require_signature)"
    $md += "- SignatureProfile: $($payload.signature_profile)"
    $md += "- SignToolAvailable: $($payload.signtool_available)"
    if ($payload.signtool_path) {
        $md += "- SignToolPath: $($payload.signtool_path)"
    }
    $md += ''
    $md += '| File | HashStatus | SignatureStatus | SignaturePolicy | Details |'
    $md += '| --- | --- | --- | --- | --- |'
    foreach ($result in $results) {
        $details = ("hash: {0}; signature: {1}" -f $result.hash.details, $result.signature.details) -replace '\|', '\\|'
        $md += "| $($result.file) | $($result.hash.status) | $($result.signature.status) | $($result.signature.policy_result) | $details |"
    }
    $md += ''
    $md += '## Summary'
    $md += "- Hash PASS: $($summary.hash_pass)"
    $md += "- Hash FAIL: $($summary.hash_fail)"
    $md += "- Signature PASS: $($summary.signature_pass)"
    $md += "- Signature FAIL: $($summary.signature_fail)"
    $md += "- Signature NOT_CHECKED: $($summary.signature_not_checked)"
    $md += "- Signature BLOCKED_SIGNSDK: $($summary.signature_blocked_signsdk)"
    $md += "- Signature policy FAIL: $($summary.signature_policy_fail)"
    $md += "- Signature policy WARN: $($summary.signature_policy_warn)"

    Write-Utf8NoBom -Path $mdPath -Content (($md -join "`n") + [Environment]::NewLine)

    Write-Host "Artifact verify JSON: $jsonPath"
    Write-Host "Artifact verify MD:   $mdPath"

    if ($fail) {
        exit 1
    }

    exit 0
} catch {
    Write-Host "FATAL: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
