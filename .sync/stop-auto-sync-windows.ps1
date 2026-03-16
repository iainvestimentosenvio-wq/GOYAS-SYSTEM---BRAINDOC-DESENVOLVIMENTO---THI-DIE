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

if (-not (Test-Path $state.PidFile)) {
    Write-Output 'Auto-sync nao esta rodando.'
    exit 0
}

$storedPid = Get-Content $state.PidFile | Select-Object -First 1
if (-not $storedPid) {
    Remove-Item $state.PidFile -Force -ErrorAction SilentlyContinue
    Write-Output 'PID invalido removido. Auto-sync nao esta rodando.'
    exit 0
}

$process = Get-Process -Id $storedPid -ErrorAction SilentlyContinue
if (-not $process) {
    Remove-Item $state.PidFile -Force -ErrorAction SilentlyContinue
    Write-Output "Processo $storedPid nao estava mais ativo."
    exit 0
}

Stop-Process -Id $storedPid -Force
Remove-Item $state.PidFile -Force -ErrorAction SilentlyContinue
Write-Output "Auto-sync parado. PID: $storedPid"
