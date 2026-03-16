param(
    [string]$Repo = (Split-Path -Parent $PSScriptRoot),
    [string]$ExpectedBranch,
    [int]$DebounceMs = 5000,
    [int]$PullEverySeconds = 30,
    [string]$LogRoot,
    [switch]$RunOnce
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib-auto-sync-windows.ps1')

$state = New-AutoSyncState -Repo $Repo -ExpectedBranch $ExpectedBranch -LogRoot $LogRoot
Assert-ExpectedBranch -State $state

New-Item -ItemType Directory -Force -Path $state.LogDir | Out-Null
[System.IO.File]::WriteAllText($state.PidFile, [string]$PID)

Write-AutoSyncLog -State $state -Message "=== Auto-sync iniciado (Windows) | branch=$($state.Branch) ==="
Write-AutoSyncLog -State $state -Message "Monitorando: $($state.Repo)"

if ($RunOnce) {
    Invoke-PullIfBehind -State $state
    Invoke-PushChanges -State $state
    Write-AutoSyncLog -State $state -Message 'RunOnce concluido.'
    return
}

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $state.Repo
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true
$watcher.Filter = '*'

$script:PendingChange = $false
$script:LastChange = [DateTime]::MinValue

$action = {
    $path = $Event.SourceEventArgs.FullPath
    if (Test-ShouldIgnorePath -Path $path) { return }
    $script:PendingChange = $true
    $script:LastChange = Get-Date
}

$subscriptions = @(
    Register-ObjectEvent $watcher Changed -Action $action
    Register-ObjectEvent $watcher Created -Action $action
    Register-ObjectEvent $watcher Deleted -Action $action
    Register-ObjectEvent $watcher Renamed -Action $action
)

try {
    $pullTimer = [DateTime]::Now

    while ($true) {
        Start-Sleep -Milliseconds 1000

        if ($script:PendingChange) {
            $elapsed = ([DateTime]::Now - $script:LastChange).TotalMilliseconds
            if ($elapsed -ge $DebounceMs) {
                $script:PendingChange = $false
                Invoke-PushChanges -State $state
            }
        }

        $sinceLastPull = ([DateTime]::Now - $pullTimer).TotalSeconds
        if ($sinceLastPull -ge $PullEverySeconds) {
            $pullTimer = [DateTime]::Now
            Invoke-PullIfBehind -State $state
        }
    }
}
finally {
    foreach ($subscription in $subscriptions) {
        if ($null -ne $subscription) {
            Unregister-Event -SourceIdentifier $subscription.Name -ErrorAction SilentlyContinue
            Remove-Job -Id $subscription.Id -Force -ErrorAction SilentlyContinue
        }
    }

    $watcher.Dispose()
}
