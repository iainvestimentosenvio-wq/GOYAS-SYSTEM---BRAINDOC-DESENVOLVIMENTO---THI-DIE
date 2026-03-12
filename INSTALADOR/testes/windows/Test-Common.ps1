# Shared helpers for Windows installer test scripts.

Set-StrictMode -Version Latest

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Write-JsonNoBom {
    param(
        [Parameter(Mandatory=$true)]
        [object]$Value,
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [int]$Depth = 8
    )

    $json = $Value | ConvertTo-Json -Depth $Depth
    Write-Utf8NoBom -Path $Path -Content ($json + [Environment]::NewLine)
}

function Read-JsonFlexible {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path
    )

    $raw = Get-Content -Path $Path -Raw
    if ($raw.Length -gt 0 -and [int][char]$raw[0] -eq 0xFEFF) {
        $raw = $raw.Substring(1)
    }

    return $raw | ConvertFrom-Json
}

function Get-InstallerContract {
    param(
        [string]$ContractPath
    )

    if (-not $ContractPath) {
        $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $ContractPath = Join-Path $root 'testes\windows\installer-contract.json'
    }

    if (-not (Test-Path $ContractPath)) {
        throw "Installer contract file not found: $ContractPath"
    }

    return Read-JsonFlexible -Path $ContractPath
}

function Get-VersionFromEnvFile {
    param(
        [Parameter(Mandatory=$true)]
        [string]$VersionFile
    )

    if (-not (Test-Path $VersionFile)) {
        return "1.0.0"
    }

    $versionLine = Get-Content $VersionFile | Where-Object { $_ -match '^VERSION=' } | Select-Object -First 1
    if (-not $versionLine) {
        return "1.0.0"
    }

    return (($versionLine -replace '^VERSION=', '').Trim('"').Trim())
}

function Get-ProjectContext {
    param(
        [string]$ProjectRoot,
        [string]$Version,
        [string]$ArtifactsDir,
        [string]$LogDir,
        [string]$RunId
    )

    $scriptDir = Split-Path -Parent $PSScriptRoot
    $defaultRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)

    if (-not $ProjectRoot) {
        $ProjectRoot = $defaultRoot
    }

    # Accept both repository root and INSTALADOR root.
    if (Test-Path (Join-Path $ProjectRoot 'comum\version.env')) {
        $ProjectRoot = Split-Path -Parent $ProjectRoot
    }

    $versionFile = Join-Path $ProjectRoot "INSTALADOR\comum\version.env"
    if (-not $Version) {
        $Version = Get-VersionFromEnvFile -VersionFile $versionFile
    }

    if (-not $ArtifactsDir) {
        $ArtifactsDir = Join-Path $ProjectRoot "INSTALADOR\saida\windows"
    }

    if (-not $LogDir) {
        $LogDir = Join-Path $ProjectRoot "INSTALADOR\saida\test-logs"
    }

    if (-not (Test-Path $LogDir)) {
        New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    }

    if (-not $RunId) {
        $RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    }

    return [ordered]@{
        ProjectRoot = $ProjectRoot
        Version = $Version
        ArtifactsDir = $ArtifactsDir
        LogDir = $LogDir
        RunId = $RunId
        Timestamp = $RunId
        MsiPath = Join-Path $ArtifactsDir ("Protons-{0}-x64.msi" -f $Version)
        ExePath = Join-Path $ArtifactsDir ("ProtonsSetup-{0}.exe" -f $Version)
    }
}

function Test-IsAdmin {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function New-ResultStore {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Suite,
        [Parameter(Mandatory=$true)]
        [string]$Version,
        [Parameter(Mandatory=$true)]
        [string]$Timestamp
    )

    return [ordered]@{
        suite = $Suite
        version = $Version
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        run_id = $Timestamp
        results = @()
    }
}

function Add-Result {
    param(
        [Parameter(Mandatory=$true)]
        [System.Collections.IDictionary]$Store,
        [Parameter(Mandatory=$true)]
        [string]$Id,
        [Parameter(Mandatory=$true)]
        [ValidateSet('PASS','FAIL','MANUAL','SKIP')]
        [string]$Status,
        [Parameter(Mandatory=$true)]
        [string]$Details,
        [string]$Evidence = ''
    )

    $entry = [ordered]@{
        id = $Id
        status = $Status
        details = $Details
        evidence = $Evidence
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }

    $Store.results += $entry

    $color = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'MANUAL' { 'Yellow' }
        default { 'DarkYellow' }
    }

    Write-Host ("[{0}] {1} - {2}" -f $Status, $Id, $Details) -ForegroundColor $color
}

function Get-ResultCounts {
    param(
        [Parameter(Mandatory=$true)]
        [System.Collections.IDictionary]$Store
    )

    return [ordered]@{
        PASS = @($Store.results | Where-Object { $_.status -eq 'PASS' }).Count
        FAIL = @($Store.results | Where-Object { $_.status -eq 'FAIL' }).Count
        MANUAL = @($Store.results | Where-Object { $_.status -eq 'MANUAL' }).Count
        SKIP = @($Store.results | Where-Object { $_.status -eq 'SKIP' }).Count
        TOTAL = $Store.results.Count
    }
}

function Write-ResultFiles {
    param(
        [Parameter(Mandatory=$true)]
        [System.Collections.IDictionary]$Store,
        [Parameter(Mandatory=$true)]
        [string]$OutputBasePath
    )

    $counts = Get-ResultCounts -Store $Store
    $Store.summary = $counts

    $jsonPath = "${OutputBasePath}.json"
    $mdPath = "${OutputBasePath}.md"

    Write-JsonNoBom -Value $Store -Path $jsonPath -Depth 8

    $md = @()
    $md += "# Resultado de Testes - $($Store.suite)"
    $md += ""
    $md += "- Version: $($Store.version)"
    $md += "- RunId: $($Store.run_id)"
    $md += "- TimestampUTC: $($Store.timestamp_utc)"
    $md += ""
    $md += "## Resumo"
    $md += "- PASS: $($counts.PASS)"
    $md += "- FAIL: $($counts.FAIL)"
    $md += "- MANUAL: $($counts.MANUAL)"
    $md += "- SKIP: $($counts.SKIP)"
    $md += "- TOTAL: $($counts.TOTAL)"
    $md += ""
    $md += "## Itens"
    $md += "| ID | Status | Details | Evidence |"
    $md += "| --- | --- | --- | --- |"

    foreach ($result in $Store.results) {
        $details = ($result.details -replace '\|', '\\|')
        $evidence = ($result.evidence -replace '\|', '\\|')
        $md += "| $($result.id) | $($result.status) | $details | $evidence |"
    }

    Write-Utf8NoBom -Path $mdPath -Content (($md -join "`n") + [Environment]::NewLine)

    return [ordered]@{
        Json = $jsonPath
        Markdown = $mdPath
        Summary = $counts
    }
}

function Get-CommandDuration {
    param(
        [Parameter(Mandatory=$true)]
        [scriptblock]$ScriptBlock
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $ScriptBlock
    $sw.Stop()
    return [Math]::Round($sw.Elapsed.TotalSeconds, 2)
}

function Wait-Condition {
    param(
        [Parameter(Mandatory=$true)]
        [scriptblock]$Condition,
        [int]$TimeoutSeconds = 30,
        [int]$PollIntervalMilliseconds = 1000,
        [string]$Description = 'condition'
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $lastError = ''

    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        try {
            if (& $Condition) {
                $sw.Stop()
                return [ordered]@{
                    Satisfied = $true
                    ElapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
                    LastError = $lastError
                    Description = $Description
                }
            }
        } catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Milliseconds $PollIntervalMilliseconds
    }

    $sw.Stop()
    return [ordered]@{
        Satisfied = $false
        ElapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        LastError = $lastError
        Description = $Description
    }
}

function Ensure-File {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [string]$Label
    )

    if (-not (Test-Path $Path)) {
        throw "Required file not found ($Label): $Path"
    }
}
