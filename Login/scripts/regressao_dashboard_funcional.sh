#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOGIN_DIR="$ROOT_DIR/Login"
RESULTS_DIR="$ROOT_DIR/Login/testes/TestResults/regressao_dashboard"

declare -a EVENT_FILTERS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --event-filter)
      EVENT_FILTERS+=("${2:-}")
      shift 2
      ;;
    -h|--help)
      cat <<'USAGE'
Uso:
  bash Login/scripts/regressao_dashboard_funcional.sh [--event-filter "evento_id"]

Tambem aceita variavel de ambiente:
  PROTONS_REGRESSAO_EVENT_FILTERS="evento_a,evento_b"
USAGE
      exit 0
      ;;
    *)
      echo "Parametro invalido: $1" >&2
      exit 2
      ;;
  esac
done

mkdir -p "$RESULTS_DIR"

timestamp="$(date +%Y%m%d_%H%M%S)"
run_log="$RESULTS_DIR/regressao_dashboard_${timestamp}.log"
resumo_log="$RESULTS_DIR/regressao_dashboard_${timestamp}_resumo.log"
ops_log_copia="$RESULTS_DIR/regressao_dashboard_${timestamp}_ops.jsonl"

export XDG_DATA_HOME="${XDG_DATA_HOME:-$RESULTS_DIR/xdg_data}"
export PROTONS_DATA_KEY_BASE64="${PROTONS_DATA_KEY_BASE64:-AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=}"

if [[ -n "${XDG_DATA_HOME:-}" ]]; then
  ops_log="$XDG_DATA_HOME/Protons/log_ops.jsonl"
else
  ops_log="$HOME/.local/share/Protons/log_ops.jsonl"
fi

mkdir -p "$(dirname "$ops_log")"
touch "$ops_log"
: > "$ops_log"

export PROTONS_PAINEL_DIRETO=1
export PROTONS_PAINEL_DIRETO_HABILITADO="${PROTONS_PAINEL_DIRETO_HABILITADO:-1}"
export PROTONS_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export PROTONS_PAINEL_USER_ID="${PROTONS_PAINEL_USER_ID:-1}"
export PROTONS_PAINEL_EMAIL="${PROTONS_PAINEL_EMAIL:-painel.dev@protons.local}"
export PROTONS_PAINEL_NOME="${PROTONS_PAINEL_NOME:-Painel Dev}"
export PROTONS_PAINEL_ROLE="${PROTONS_PAINEL_ROLE:-admin}"

ARTIFACTS_PATH="${PROTONS_DOTNET_ARTIFACTS_PATH:-}"

dotnet_run_args=(
  dotnet run --project Protons.UI/Protons.UI.csproj -c Debug --no-build
)

if [[ -n "$ARTIFACTS_PATH" ]]; then
  ui_dll_artifacts="$ARTIFACTS_PATH/bin/Protons.UI/debug/Protons.UI.dll"
  if [[ -f "$ui_dll_artifacts" ]]; then
    dotnet_run_args=(
      dotnet "$ui_dll_artifacts"
    )
  else
    dotnet_run_args+=(--artifacts-path "$ARTIFACTS_PATH")
  fi
fi

cd "$LOGIN_DIR"
if [[ -z "${DISPLAY:-}" ]] && command -v xvfb-run >/dev/null 2>&1; then
  xvfb-run -a "${dotnet_run_args[@]}" >"$run_log" 2>&1 &
else
  "${dotnet_run_args[@]}" >"$run_log" 2>&1 &
fi
app_pid=$!

cleanup() {
  if kill -0 "$app_pid" >/dev/null 2>&1; then
    kill "$app_pid" >/dev/null 2>&1 || true
    wait "$app_pid" >/dev/null 2>&1 || true
  fi
  rm -f "${ops_chunk_tmp:-}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

capturou_painel=0
capturou_first_render=0
capturou_first_render_fallback=0
capturou_busca_clientes=0
last_lido_bytes=0
ops_chunk_tmp="$RESULTS_DIR/.ops_chunk_${timestamp}.tmp"
: >"$ops_chunk_tmp"

if [[ -n "${PROTONS_REGRESSAO_EVENT_FILTERS:-}" ]]; then
  IFS=',' read -r -a extra_filters <<<"${PROTONS_REGRESSAO_EVENT_FILTERS}"
  for filtro in "${extra_filters[@]}"; do
    filtro_normalizado="$(echo "$filtro" | xargs)"
    if [[ -n "$filtro_normalizado" ]]; then
      EVENT_FILTERS+=("$filtro_normalizado")
    fi
  done
fi

declare -A EXTRA_FILTER_STATUS
for filtro in "${EVENT_FILTERS[@]}"; do
  EXTRA_FILTER_STATUS["$filtro"]=0
done

for _ in $(seq 1 75); do
  tamanho_atual=0
  if [[ -f "$ops_log" ]]; then
    tamanho_atual="$(wc -c <"$ops_log" | tr -d '[:space:]')"
  fi

  if (( tamanho_atual < last_lido_bytes )); then
    last_lido_bytes=0
  fi

  if (( tamanho_atual > last_lido_bytes )); then
    tail -c "+$((last_lido_bytes + 1))" "$ops_log" >"$ops_chunk_tmp"
    last_lido_bytes=$tamanho_atual

    if [[ "$capturou_painel" -eq 0 ]] && rg -q "PainelView" "$ops_chunk_tmp"; then
      capturou_painel=1
    fi

    if [[ "$capturou_first_render" -eq 0 ]] && rg -q "painel_metrica id=first_render_ms" "$ops_chunk_tmp"; then
      capturou_first_render=1
    fi

    # Fallback para execucao headless: alguns ambientes nao disparam AttachedToVisualTree
    # no mesmo timing, mas ainda registram resolucao/renderizacao do PainelView.
    if [[ "$capturou_first_render_fallback" -eq 0 ]] && rg -q "viewlocator_elapsed: type=PainelView|ViewLocator: successfully created PainelView" "$ops_chunk_tmp"; then
      capturou_first_render_fallback=1
    fi

    if [[ "$capturou_busca_clientes" -eq 0 ]] && rg -q "painel_evento id=clientes_busca" "$ops_chunk_tmp"; then
      capturou_busca_clientes=1
    fi

    for filtro in "${EVENT_FILTERS[@]}"; do
      if [[ "${EXTRA_FILTER_STATUS[$filtro]}" -eq 0 ]] && rg -q "$filtro" "$ops_chunk_tmp"; then
        EXTRA_FILTER_STATUS["$filtro"]=1
      fi
    done
  fi

  todos_extras_ok=1
  for filtro in "${EVENT_FILTERS[@]}"; do
    if [[ "${EXTRA_FILTER_STATUS[$filtro]}" -ne 1 ]]; then
      todos_extras_ok=0
      break
    fi
  done

  if [[ "$capturou_painel" -eq 1 && ("$capturou_first_render" -eq 1 || "$capturou_first_render_fallback" -eq 1) && "$capturou_busca_clientes" -eq 1 && "$todos_extras_ok" -eq 1 ]]; then
    break
  fi

  sleep 1
done

cp "$ops_log" "$ops_log_copia"

{
  echo "regressao_dashboard_funcional"
  echo "timestamp=$timestamp"
  echo "painel_resolvido=$capturou_painel"
  echo "first_render_metrica=$capturou_first_render"
  echo "first_render_fallback_viewlocator=$capturou_first_render_fallback"
  echo "evento_clientes_busca=$capturou_busca_clientes"
  if [[ "${#EVENT_FILTERS[@]}" -gt 0 ]]; then
    echo "--- filtros extras ---"
    for filtro in "${EVENT_FILTERS[@]}"; do
      echo "filtro[$filtro]=${EXTRA_FILTER_STATUS[$filtro]}"
    done
  fi
  echo "--- eventos ---"
  rg -n "PainelView|painel_metrica id=first_render_ms|viewlocator_elapsed: type=PainelView|ViewLocator: successfully created PainelView|painel_evento id=clientes_busca" "$ops_log" || true
  for filtro in "${EVENT_FILTERS[@]}"; do
    rg -n "$filtro" "$ops_log" || true
  done
} >"$resumo_log"

extras_fail=0
for filtro in "${EVENT_FILTERS[@]}"; do
  if [[ "${EXTRA_FILTER_STATUS[$filtro]}" -ne 1 ]]; then
    extras_fail=1
    break
  fi
done

if [[ "$capturou_painel" -ne 1 || ("$capturou_first_render" -ne 1 && "$capturou_first_render_fallback" -ne 1) || "$capturou_busca_clientes" -ne 1 || "$extras_fail" -ne 0 ]]; then
  echo "Falha na regressao funcional do dashboard. Veja:"
  echo " - $run_log"
  echo " - $resumo_log"
  echo " - $ops_log_copia"
  exit 1
fi

echo "Regressao funcional do dashboard aprovada."
echo " - Run log: $run_log"
echo " - Resumo: $resumo_log"
echo " - Ops log: $ops_log_copia"
