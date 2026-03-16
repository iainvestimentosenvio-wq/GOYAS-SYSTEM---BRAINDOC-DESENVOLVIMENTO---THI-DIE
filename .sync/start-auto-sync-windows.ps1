param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedBranch,
    [string]$Repo = (Split-Path -Parent $PSScriptRoot),
    [string]$LogRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib-auto-sync-windows.ps1')

$state = New-AutoSyncState -Repo $Repo -ExpectedBranch $ExpectedBranch -LogRoot $LogRoot
Assert-ExpectedBranch -State $state

if (Test-Path $state.PidFile) {
    $existingPid = Get-Content $state.PidFile -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existingPid) {
        $process = Get-Process -Id $existingPid -ErrorAction SilentlyContinue
        if ($process) {
            Write-Output "Auto-sync ja esta rodando. PID: $existingPid"
            exit 0
        }
    }
}

New-Item -ItemType Directory -Force -Path $state.LogDir | Out-Null

$shell = Get-ShellCommand
$scriptPath = Join-Path $PSScriptRoot 'auto-sync-windows.ps1'
$argumentString = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -ExpectedBranch `"$ExpectedBranch`" -Repo `"$($state.Repo)`""
if ($LogRoot) {
    $argumentString += " -LogRoot `"$LogRoot`""
}

$startParams = @{
    FilePath = $shell
    ArgumentList = $argumentString
    PassThru = $true
}

if ($IsWindows) {
    $startParams.WindowStyle = 'Hidden'
}

$process = Start-Process @startParams
Start-Sleep -Seconds 2

if (-not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
    throw 'Falha ao iniciar o processo de auto-sync.'
}

[System.IO.File]::WriteAllText($state.PidFile, [string]$process.Id)
Write-Output "Auto-sync iniciado com sucesso. PID: $($process.Id)"
Write-Output "Log: $($state.LogFile)"
