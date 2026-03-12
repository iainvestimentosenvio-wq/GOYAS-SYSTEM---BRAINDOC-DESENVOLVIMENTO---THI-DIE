#!/usr/bin/env bash
# troubleshoot-upload.sh
# Diagnostico e fallback para transferencia de bundle Windows.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
if PROJECT_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null)"; then
  :
else
  PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd -P)"
fi
if [ -d "$PROJECT_ROOT/INSTALADOR" ]; then
  INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
else
  INSTALADOR_ROOT="$PROJECT_ROOT"
fi

find_latest_bundle_zip() {
  find "$INSTALADOR_ROOT/saida" -maxdepth 3 -type f -name 'protons-autonoma-bundle.zip' -printf '%T@ %p\n' 2>/dev/null \
    | sort -nr \
    | head -n1 \
    | cut -d' ' -f2-
}

resolve_host_ip() {
  ip -4 route get 1.1.1.1 2>/dev/null | awk '/src/ {for (i=1; i<=NF; i++) if ($i=="src") {print $(i+1); exit}}'
}

print_usage() {
  cat <<USAGE
Uso:
  bash troubleshoot-upload.sh

Variaveis opcionais:
  PROTONS_SHARED_ROOT   Diretorio base para publicar bundle HTTP
  BUNDLE_ZIP            Caminho explicito para bundle zip

Fluxo:
  1) Ajusta QGA e encerra (sem HTTP)
  2) Sobe servidor HTTP local de fallback
  3) Ajusta QGA e prepara fallback HTTP sem iniciar servidor
USAGE
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  print_usage
  exit 0
fi

echo "=== Upload Troubleshooter (Windows VM) ==="
echo ""
echo "1) Ajustar upload por QGA e sair"
echo "2) Fallback HTTP server local (inicia servidor)"
echo "3) Ajustar QGA e preparar fallback HTTP (sem iniciar servidor)"
echo ""
read -r -p "Escolha [1/2/3]: " choice

configure_qga_defaults() {
  export QGA_WRITE_CHUNK_SIZE="${QGA_WRITE_CHUNK_SIZE:-65536}"
  export QGA_WRITE_PROGRESS_EVERY_MB="${QGA_WRITE_PROGRESS_EVERY_MB:-10}"
  export QGA_UPLOAD_MAX_ATTEMPTS="${QGA_UPLOAD_MAX_ATTEMPTS:-3}"
  export QGA_UPLOAD_RETRY_SLEEP_SEC="${QGA_UPLOAD_RETRY_SLEEP_SEC:-10}"

  echo ""
  echo "Parametros sugeridos para QGA:"
  echo "  QGA_WRITE_CHUNK_SIZE=$QGA_WRITE_CHUNK_SIZE"
  echo "  QGA_WRITE_PROGRESS_EVERY_MB=$QGA_WRITE_PROGRESS_EVERY_MB"
  echo "  QGA_UPLOAD_MAX_ATTEMPTS=$QGA_UPLOAD_MAX_ATTEMPTS"
  echo "  QGA_UPLOAD_RETRY_SLEEP_SEC=$QGA_UPLOAD_RETRY_SLEEP_SEC"
  echo ""
  echo "Exemplo de execucao:"
  echo "  QGA_WRITE_CHUNK_SIZE=$QGA_WRITE_CHUNK_SIZE QGA_WRITE_PROGRESS_EVERY_MB=$QGA_WRITE_PROGRESS_EVERY_MB \\"
  echo "  bash run-windows-base-gate.sh"
}

start_http_server=0

case "$choice" in
  1)
    configure_qga_defaults
    echo ""
    echo "Fluxo QGA configurado. Encerrando sem iniciar fallback HTTP."
    exit 0
    ;;
  2)
    start_http_server=1
    ;;
  3)
    configure_qga_defaults
    start_http_server=0
    ;;
  *)
    echo "Opcao invalida."
    exit 1
    ;;
esac

BUNDLE_ZIP="${BUNDLE_ZIP:-$(find_latest_bundle_zip || true)}"
if [ -z "${BUNDLE_ZIP:-}" ] || [ ! -f "$BUNDLE_ZIP" ]; then
  echo "ERRO: nao foi encontrado bundle zip automaticamente."
  echo "Defina BUNDLE_ZIP manualmente e execute novamente."
  exit 1
fi

HTTP_ROOT_DEFAULT="$INSTALADOR_ROOT/saida/http-bundle-server"
HTTP_DIR="${PROTONS_SHARED_ROOT:-$HTTP_ROOT_DEFAULT}"
mkdir -p "$HTTP_DIR"
cp -f "$BUNDLE_ZIP" "$HTTP_DIR/bundle.zip"

HOST_IP="$(resolve_host_ip || true)"
if [ -z "${HOST_IP:-}" ]; then
  HOST_IP="127.0.0.1"
fi

GUIDE_FILE="$INSTALADOR_ROOT/GUIA-MANUAL-UPLOAD-HTTP.md"
cat > "$GUIDE_FILE" <<GUIDE
# GUIA MANUAL: Upload Bundle via HTTP Server

## URL do bundle
http://$HOST_IP:8888/bundle.zip

## PowerShell na VM (Admin)
\$url = "http://$HOST_IP:8888/bundle.zip"
\$dest = "C:\\Windows\\Temp\\protons-autonoma-bundle.zip"
Invoke-WebRequest -Uri \$url -OutFile \$dest

if (Test-Path \$dest) {
  Write-Host "Bundle baixado com sucesso" -ForegroundColor Green
} else {
  Write-Host "Falha no download" -ForegroundColor Red
}
GUIDE

echo ""
echo "Bundle publicado: $HTTP_DIR/bundle.zip"
echo "Guia gerado: $GUIDE_FILE"
echo "URL: http://$HOST_IP:8888/bundle.zip"
echo ""

if [ "$start_http_server" -eq 1 ]; then
  echo "Iniciando servidor HTTP local na porta 8888..."
  echo "Pressione CTRL+C para encerrar."
  cd "$HTTP_DIR"
  exec python3 -m http.server 8888
fi

echo "Fallback HTTP preparado sem iniciar servidor."
echo "Para iniciar manualmente quando precisar:"
echo "  cd \"$HTTP_DIR\""
echo "  python3 -m http.server 8888"
echo ""
echo "Nenhum servidor foi iniciado."
exit 0
