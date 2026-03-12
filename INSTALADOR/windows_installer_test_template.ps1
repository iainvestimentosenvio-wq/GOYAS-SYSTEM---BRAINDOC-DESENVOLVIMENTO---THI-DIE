param(
  [string]$InstallerPath = "C:\temp\installer.exe",
  [string]$LogPath = "C:\temp\test_log.txt",
  [string]$SilentArgs = "/S"
)

New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
"[$(Get-Date -Format s)] Inicio do teste" | Out-File -FilePath $LogPath -Encoding utf8

if (-not (Test-Path $InstallerPath)) {
  "Installer nao encontrado: $InstallerPath" | Add-Content -Path $LogPath
  exit 1
}

"Executando instalador: $InstallerPath $SilentArgs" | Add-Content -Path $LogPath
$proc = Start-Process -FilePath $InstallerPath -ArgumentList $SilentArgs -Wait -PassThru
"ExitCode instalador: $($proc.ExitCode)" | Add-Content -Path $LogPath

Start-Sleep -Seconds 5

"Checagens basicas (ajuste para seu app):" | Add-Content -Path $LogPath
"- App abre" | Add-Content -Path $LogPath
"- Servico roda" | Add-Content -Path $LogPath
"- Arquivos gerados" | Add-Content -Path $LogPath

"[$(Get-Date -Format s)] Fim do teste" | Add-Content -Path $LogPath
