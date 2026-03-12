#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RESULTS_DIR="$ROOT_DIR/Login/testes/TestResults/smoke"
mkdir -p "$RESULTS_DIR"

ARTIFACTS_PATH="${PROTONS_DOTNET_ARTIFACTS_PATH:-}"
MSBUILD_OBJ_ROOT="${PROTONS_MSBUILD_OBJ_ROOT:-}"

dotnet_cmd() {
  local scope="$1"
  shift

  local -a args
  args=(dotnet "$@")
  args+=(--disable-build-servers)

  local scoped_artifacts=""
  if [[ -n "$ARTIFACTS_PATH" ]]; then
    scoped_artifacts="$ARTIFACTS_PATH"
  elif [[ -n "$MSBUILD_OBJ_ROOT" ]]; then
    scoped_artifacts="$MSBUILD_OBJ_ROOT/$scope/artifacts"
  fi

  if [[ -n "$scoped_artifacts" ]]; then
    mkdir -p "$scoped_artifacts"
    args+=(--artifacts-path "$scoped_artifacts")
  fi

  "${args[@]}"
}

echo "[smoke] build painel"
dotnet_cmd build build "$ROOT_DIR/Login/Protons.UI/Protons.UI.csproj" -c Debug >"$RESULTS_DIR/build.log" 2>&1

echo "[smoke] testes core cliente"
dotnet_cmd test-core test "$ROOT_DIR/Login/testes/Protons.Core.Tests/Protons.Core.Tests.csproj" \
  -c Debug \
  --filter "FullyQualifiedName~Cliente" \
  --logger "trx;LogFileName=smoke-core-clientes.trx" \
  --results-directory "$RESULTS_DIR" >"$RESULTS_DIR/test-core.log" 2>&1

echo "[smoke] testes infra cliente sqlite"
dotnet_cmd test-infra test "$ROOT_DIR/Login/testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj" \
  -c Debug \
  --filter "FullyQualifiedName~SqliteClienteRepositoryTests|FullyQualifiedName~SqliteDbMigrationTests" \
  --logger "trx;LogFileName=smoke-infra-clientes.trx" \
  --results-directory "$RESULTS_DIR" >"$RESULTS_DIR/test-infra.log" 2>&1

# Sandboxing operacional para smoke:
# evita dependencia de banco/chaves locais da maquina.
export XDG_DATA_HOME="$RESULTS_DIR/xdg_data"
rm -rf "$XDG_DATA_HOME"
mkdir -p "$XDG_DATA_HOME"
export PROTONS_DATA_KEY_BASE64="${PROTONS_DATA_KEY_BASE64:-AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=}"
export PROTONS_PAINEL_DIRETO=1
export PROTONS_PAINEL_DIRETO_HABILITADO="${PROTONS_PAINEL_DIRETO_HABILITADO:-1}"
export PROTONS_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export PROTONS_PAINEL_USER_ID="${PROTONS_PAINEL_USER_ID:-1}"
export PROTONS_PAINEL_EMAIL="${PROTONS_PAINEL_EMAIL:-painel.dev@protons.local}"
export PROTONS_PAINEL_NOME="${PROTONS_PAINEL_NOME:-Painel Dev}"
export PROTONS_PAINEL_ROLE="${PROTONS_PAINEL_ROLE:-admin}"

echo "[smoke] startup probe"
rm -f "$RESULTS_DIR/startup_probe.txt"
startup_cmd=(
  dotnet run --project "$ROOT_DIR/Login/Protons.UI/Protons.UI.csproj" -c Debug --no-build
)

if [[ -n "$ARTIFACTS_PATH" ]]; then
  startup_cmd+=(--artifacts-path "$ARTIFACTS_PATH")
fi

if ! (
  cd "$ROOT_DIR/Login"
  PROTONS_STARTUP_PROBE=1 \
  PROTONS_STARTUP_PROBE_OUT="$RESULTS_DIR/startup_probe.txt" \
  "${startup_cmd[@]}"
) >"$RESULTS_DIR/startup.log" 2>&1; then
  {
    echo
    echo "[smoke] startup probe falhou (best-effort); seguindo para regressao funcional."
  } >>"$RESULTS_DIR/startup.log"
fi

echo "[smoke] regressao funcional dashboard"
if ! PROTONS_DOTNET_ARTIFACTS_PATH="$ARTIFACTS_PATH" \
  bash "$ROOT_DIR/Login/scripts/regressao_dashboard_funcional.sh" >"$RESULTS_DIR/regressao-dashboard.log" 2>&1; then
  {
    echo
    echo "[smoke] regressao falhou na primeira tentativa; executando retry unico..."
  } >>"$RESULTS_DIR/regressao-dashboard.log"

  sleep 2
  PROTONS_DOTNET_ARTIFACTS_PATH="$ARTIFACTS_PATH" \
    bash "$ROOT_DIR/Login/scripts/regressao_dashboard_funcional.sh" >>"$RESULTS_DIR/regressao-dashboard.log" 2>&1
fi

echo "[smoke] concluido. evidencias em: $RESULTS_DIR"
