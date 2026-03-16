param(
    [string]$ExpectedBranch = "estabilizacao-fase1"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$StateDirName = "GOYAS-SYSTEMS-auto-sync"
$StateRoot = if ($env:LOCALAPPDATA) {
    Join-Path $env:LOCALAPPDATA $StateDirName
} else {
    Join-Path $env:TEMP $StateDirName
}
$StateDir = Join-Path $StateRoot $ExpectedBranch
$PidFile = Join-Path $StateDir "auto-sync.pid"
$LogFile = Join-Path $StateDir "sync.log"
$AutoSyncScript = Join-Path $ScriptDir "auto-sync-windows.ps1"

New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
Set-Location $RepoRoot

$currentBranch = ((git branch --show-current) | Out-String).Trim()
if ($currentBranch -ne $ExpectedBranch) {
    Write-Error "Branch atual '$currentBranch' nao eh '$ExpectedBranch'. Troque primeiro para a branch correta."
}

if (Test-Path $PidFile) {
    $existingPid = (Get-Content $PidFile | Select-Object -First 1).Trim()
    if ($existingPid) {
        $existingProcess = Get-Process -Id $existingPid -ErrorAction SilentlyContinue
        if ($existingProcess) {
            Write-Output "Auto-sync ja esta rodando. PID: $existingPid"
            Write-Output "Log: $LogFile"
            exit 0
        }
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

$pushCheckOutput = & cmd /c "git push --dry-run origin HEAD:refs/heads/$ExpectedBranch 2>&1"
if ($LASTEXITCODE -ne 0) {
    Write-Output "Falha ao validar permissao de push para '$ExpectedBranch'."
    $pushCheckOutput
    exit 1
}

$process = Start-Process `
    -FilePath "powershell.exe" `
    -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $AutoSyncScript,
        "-ExpectedBranch", $ExpectedBranch
    ) `
    -WorkingDirectory $RepoRoot `
    -WindowStyle Hidden `
    -PassThru

Set-Content -Path $PidFile -Value $process.Id -Encoding ascii
Start-Sleep -Seconds 1

if (-not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
    Write-Error "O processo de auto-sync nao permaneceu em execucao. Verifique o log em $LogFile"
}

Write-Output "Auto-sync iniciado com sucesso."
Write-Output "PID: $($process.Id)"
Write-Output "Log: $LogFile"
