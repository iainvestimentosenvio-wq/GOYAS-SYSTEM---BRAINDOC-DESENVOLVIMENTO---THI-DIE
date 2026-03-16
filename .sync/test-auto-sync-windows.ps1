Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$lib = Join-Path $root '.sync/lib-auto-sync-windows.ps1'

. $lib

$ignored = @(
    "$root/.git/index",
    "$root/.sync/sync.log",
    "$root/Login/bin/Debug/app.dll",
    "$root/Login/obj/Debug/app.o",
    "$root/Login/testes/TestResults/result.trx"
)

foreach ($path in $ignored) {
    if (-not (Test-ShouldIgnorePath -Path $path)) {
        throw "expected ignored path: $path"
    }
}

$tracked = @(
    "$root/README.md",
    "$root/Login/scripts/launch_login.sh",
    "$root/.sync/start-auto-sync-windows.ps1"
)

foreach ($path in $tracked) {
    if (Test-ShouldIgnorePath -Path $path) {
        throw "expected tracked path: $path"
    }
}

$logRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'goyas-auto-sync-tests'
$state = New-AutoSyncState -Repo $root -ExpectedBranch 'estabilizacao-fase1' -LogRoot $logRoot

if (-not $state.LogDir.StartsWith($logRoot)) {
    throw "expected log dir under custom log root, got: $($state.LogDir)"
}

if ($state.LogDir -like "$root*") {
    throw "expected log dir outside repo, got: $($state.LogDir)"
}

Write-Output 'auto-sync windows tests: ok'
