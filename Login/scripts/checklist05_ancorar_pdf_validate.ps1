#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Wrapper PowerShell para checklist05_ancorar_pdf_validate.sh.
  Delega todos os argumentos ao script bash equivalente.

.EXAMPLE
  pwsh Login/scripts/checklist05_ancorar_pdf_validate.ps1 --profile baseline --ci --write-report --sync-checklist never
  pwsh Login/scripts/checklist05_ancorar_pdf_validate.ps1 --profile full --ci --write-report --sync-checklist release
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$shScript  = Join-Path $scriptDir "checklist05_ancorar_pdf_validate.sh"

if (-not (Test-Path $shScript)) {
  Write-Error "Script bash nao encontrado: $shScript"
  exit 2
}

bash $shScript @args
exit $LASTEXITCODE
