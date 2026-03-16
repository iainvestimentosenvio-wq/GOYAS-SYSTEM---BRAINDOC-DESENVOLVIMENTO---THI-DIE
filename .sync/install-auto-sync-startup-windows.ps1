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

$startupDir = [Environment]::GetFolderPath('Startup')
if (-not $startupDir) {
    throw 'Nao foi possivel localizar a pasta de Inicializar do usuario.'
}

New-Item -ItemType Directory -Force -Path $state.LogDir | Out-Null

$shell = Get-ShellCommand
$startScript = Join-Path $PSScriptRoot 'start-auto-sync-windows.ps1'
$cmdFile = Join-Path $startupDir "GOYAS-auto-sync-$ExpectedBranch.cmd"

$escapedShell = '"' + $shell + '"'
$escapedScript = '"' + $startScript + '"'
$escapedRepo = '"' + $state.Repo + '"'

$content = @(
    '@echo off'
    "$escapedShell -NoProfile -ExecutionPolicy Bypass -File $escapedScript -ExpectedBranch $ExpectedBranch -Repo $escapedRepo"
) -join [Environment]::NewLine

[System.IO.File]::WriteAllText($cmdFile, $content)
Write-Output "Inicializacao automatica configurada em: $cmdFile"
