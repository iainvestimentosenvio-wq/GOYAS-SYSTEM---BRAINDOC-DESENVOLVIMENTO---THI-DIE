#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOGIN_DIR="$ROOT_DIR/Login"
BASELINE_DIR="$ROOT_DIR/painel principal/documentos/doc_painel_principal/baseline_visual/dashboard"
RESULTS_DIR="$ROOT_DIR/Login/testes/TestResults/snapshot_dashboard"

mkdir -p "$BASELINE_DIR" "$RESULTS_DIR"

timestamp="$(date +%Y%m%d_%H%M%S)"
imagem_saida="$BASELINE_DIR/dashboard_baseline_${timestamp}.png"
metadata_saida="$BASELINE_DIR/dashboard_baseline_${timestamp}.json"
run_log="$RESULTS_DIR/captura_dashboard_${timestamp}.log"

if [[ -n "${XDG_DATA_HOME:-}" ]]; then
  ops_log="$XDG_DATA_HOME/Protons/log_ops.jsonl"
else
  ops_log="$HOME/.local/share/Protons/log_ops.jsonl"
fi

mkdir -p "$(dirname "$ops_log")"
touch "$ops_log"
linha_inicial="$(wc -l < "$ops_log" || echo 0)"

ferramenta_captura=""
tem_grim=0
tem_scrot=0
tem_gnome=0
if command -v grim >/dev/null 2>&1; then tem_grim=1; fi
if command -v scrot >/dev/null 2>&1; then tem_scrot=1; fi
if command -v gnome-screenshot >/dev/null 2>&1; then tem_gnome=1; fi
if [[ "$tem_grim" -ne 1 && "$tem_gnome" -ne 1 && "$tem_scrot" -ne 1 ]]; then
  echo "Nenhuma ferramenta de captura encontrada (grim/gnome-screenshot/scrot)." >&2
  exit 1
fi

export PROTONS_PAINEL_DIRETO=1
export PROTONS_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export PROTONS_PAINEL_USER_ID="${PROTONS_PAINEL_USER_ID:-1}"
export PROTONS_PAINEL_EMAIL="${PROTONS_PAINEL_EMAIL:-painel.dev@protons.local}"
export PROTONS_PAINEL_NOME="${PROTONS_PAINEL_NOME:-Painel Dev}"
export PROTONS_PAINEL_ROLE="${PROTONS_PAINEL_ROLE:-admin}"

cd "$LOGIN_DIR"
dotnet run --project Protons.UI/Protons.UI.csproj -c Debug >"$run_log" 2>&1 &
app_pid=$!

cleanup() {
  if kill -0 "$app_pid" >/dev/null 2>&1; then
    kill "$app_pid" >/dev/null 2>&1 || true
    wait "$app_pid" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

painel_resolvido=0
for _ in $(seq 1 90); do
  if tail -n +"$((linha_inicial + 1))" "$ops_log" | rg -q "PainelView"; then
    painel_resolvido=1
    break
  fi
  sleep 1
done

if [[ "$painel_resolvido" -ne 1 ]]; then
  echo "Nao foi possivel confirmar abertura do dashboard pelo log operacional." >&2
  exit 1
fi

sleep 2

captura_ok=0
if [[ "$tem_grim" -eq 1 ]]; then
  if timeout 8s grim "$imagem_saida" >/dev/null 2>&1; then
    ferramenta_captura="grim"
    captura_ok=1
  fi
fi

if [[ "$captura_ok" -ne 1 && "$tem_scrot" -eq 1 ]]; then
  if timeout 8s scrot "$imagem_saida" >/dev/null 2>&1; then
    ferramenta_captura="scrot"
    captura_ok=1
  fi
fi

if [[ "$captura_ok" -ne 1 && "$tem_gnome" -eq 1 ]]; then
  if timeout 8s gnome-screenshot -f "$imagem_saida" >/dev/null 2>&1; then
    ferramenta_captura="gnome-screenshot"
    captura_ok=1
  fi
fi

if [[ "$captura_ok" -ne 1 ]]; then
  echo "Falha ao capturar snapshot com grim, gnome-screenshot e scrot." >&2
  exit 1
fi

resolucao="desconhecida"
if command -v identify >/dev/null 2>&1; then
  resolucao="$(identify -format "%wx%h" "$imagem_saida" 2>/dev/null || echo "desconhecida")"
else
  resolucao="$(file "$imagem_saida" | sed -n 's/.*PNG image data, \([0-9]\+ x [0-9]\+\).*/\1/p' | head -n1 | tr -d ' ')"
  if [[ -z "$resolucao" ]]; then
    resolucao="desconhecida"
  fi
fi

data_iso="$(date --iso-8601=seconds)"
cat >"$metadata_saida" <<EOF
{
  "capturado_em": "$data_iso",
  "arquivo_imagem": "$(basename "$imagem_saida")",
  "resolucao": "$resolucao",
  "ferramenta_captura": "$ferramenta_captura",
  "comando_execucao": "PROTONS_PAINEL_DIRETO=1 PROTONS_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development dotnet run --project Protons.UI/Protons.UI.csproj -c Debug",
  "log_operacional": "$ops_log",
  "run_log": "$run_log"
}
EOF

echo "Snapshot criado:"
echo " - Imagem: $imagem_saida"
echo " - Metadata: $metadata_saida"
