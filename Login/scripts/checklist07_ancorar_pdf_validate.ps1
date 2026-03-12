# Wrapper PowerShell — delega para checklist07_ancorar_pdf_validate.sh via bash.
# Uso: .\Login\scripts\checklist07_ancorar_pdf_validate.ps1 --profile baseline --ci --write-report

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$shScript  = Join-Path $scriptDir "checklist07_ancorar_pdf_validate.sh"

bash $shScript @args
exit $LASTEXITCODE
