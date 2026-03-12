# checklist09_ancorar_pdf_validate.ps1 — wrapper thin para Windows.
# Delega para o script bash equivalente.
param(
    [string]$Profile = "baseline",
    [switch]$Ci,
    [switch]$WriteReport,
    [string]$SyncChecklist = "never"
)

$ErrorActionPreference = "Stop"

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$shScript   = Join-Path $scriptDir "checklist09_ancorar_pdf_validate.sh"

$arguments = @("--profile", $Profile)

if ($Ci)          { $arguments += "--ci" }
if ($WriteReport) { $arguments += "--write-report" }
if ($SyncChecklist -ne "never") {
    $arguments += @("--sync-checklist", $SyncChecklist)
}

bash $shScript @arguments
