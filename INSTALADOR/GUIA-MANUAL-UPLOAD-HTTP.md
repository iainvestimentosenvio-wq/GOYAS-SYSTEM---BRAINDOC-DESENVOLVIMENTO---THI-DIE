# GUIA MANUAL: Upload Bundle via HTTP Server

## URL do bundle
http://192.168.15.11:8888/bundle.zip

## PowerShell na VM (Admin)
$url = "http://192.168.15.11:8888/bundle.zip"
$dest = "C:\Windows\Temp\protons-autonoma-bundle.zip"
Invoke-WebRequest -Uri $url -OutFile $dest

if (Test-Path $dest) {
  Write-Host "Bundle baixado com sucesso" -ForegroundColor Green
} else {
  Write-Host "Falha no download" -ForegroundColor Red
}
