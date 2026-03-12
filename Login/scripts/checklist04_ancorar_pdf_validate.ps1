param(
    [ValidateSet("baseline", "full")]
    [string]$Profile = "baseline",

    [switch]$Ci,

    [switch]$WriteReport,

    [ValidateSet("never", "release")]
    [string]$SyncChecklist = "never"
)

$ErrorActionPreference = "Stop"

$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$bashScript = Join-Path $rootDir "Login/scripts/checklist04_ancorar_pdf_validate.sh"

if (-not (Test-Path -LiteralPath $bashScript)) {
    throw "Script nao encontrado: $bashScript"
}

$arguments = @(
    $bashScript,
    "--profile", $Profile,
    "--sync-checklist", $SyncChecklist
)

if ($Ci.IsPresent) {
    $arguments += "--ci"
}

if ($WriteReport.IsPresent) {
    $arguments += "--write-report"
}

& bash @arguments
exit $LASTEXITCODE
