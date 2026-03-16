param(
    [string]$ExpectedBranch = "estabilizacao-fase1"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$StateDirName = "GOYAS-SYSTEMS-auto-sync"
$StateRoot = if ($env:LOCALAPPDATA) {
    Join-Path $env:LOCALAPPDATA $StateDirName
} else {
    Join-Path $env:TEMP $StateDirName
}
$StateDir = Join-Path $StateRoot $ExpectedBranch
$PidFile = Join-Path $StateDir "auto-sync.pid"

$stopped = $false

if (Test-Path $PidFile) {
    $pidText = (Get-Content $PidFile | Select-Object -First 1).Trim()
    if ($pidText) {
        $process = Get-Process -Id $pidText -ErrorAction SilentlyContinue
        if ($process) {
            Stop-Process -Id $pidText -Force
            Write-Output "Auto-sync parado. PID: $pidText"
            $stopped = $true
        }
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

if (-not $stopped) {
    $matches = Get-CimInstance Win32_Process | Where-Object {
        $_.CommandLine -like "*powershell*" -and
        $_.CommandLine -like "*-File*auto-sync-windows.ps1*" -and
        $_.CommandLine -like "*$ExpectedBranch*" -and
        $_.CommandLine -notlike "*-RunOnce*"
    }

    foreach ($match in $matches) {
        Stop-Process -Id $match.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Output "Auto-sync parado. PID: $($match.ProcessId)"
        $stopped = $true
    }
}

if (-not $stopped) {
    Write-Output "Nenhum processo de auto-sync em execucao para '$ExpectedBranch'."
}
