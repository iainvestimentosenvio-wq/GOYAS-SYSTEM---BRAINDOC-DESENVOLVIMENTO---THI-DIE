#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE="${1:-debug}"
WATCH_PROJECT="Protons.UI/Protons.UI.csproj"
DEBUG_OUTPUT="$ROOT_DIR/Protons.UI/bin/Debug/net8.0/Protons.UI"
DEBUG_OUTPUT_DLL="$ROOT_DIR/Protons.UI/bin/Debug/net8.0/Protons.UI.dll"
DEBUG_OUTPUT_DEPS="$ROOT_DIR/Protons.UI/bin/Debug/net8.0/Protons.UI.deps.json"
DEBUG_OUTPUT_RUNTIMECONFIG="$ROOT_DIR/Protons.UI/bin/Debug/net8.0/Protons.UI.runtimeconfig.json"
RESTORE_GRAPH_FILE="$ROOT_DIR/Protons.UI/obj/project.assets.json"
PANEL_SOURCE_DIR="$ROOT_DIR/../painel principal/codigos"

cd "$ROOT_DIR"

export PROTONS_PAINEL_DIRETO=1
export PROTONS_PAINEL_DIRETO_HABILITADO="${PROTONS_PAINEL_DIRETO_HABILITADO:-1}"
export PROTONS_ENVIRONMENT="${PROTONS_ENVIRONMENT:-Development}"
export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}"
export PROTONS_PAINEL_USER_ID="${PROTONS_PAINEL_USER_ID:-1}"
export PROTONS_PAINEL_EMAIL="${PROTONS_PAINEL_EMAIL:-painel.dev@protons.local}"
export PROTONS_PAINEL_NOME="${PROTONS_PAINEL_NOME:-Painel Dev}"
export PROTONS_PAINEL_ROLE="${PROTONS_PAINEL_ROLE:-admin}"
export DOTNET_USE_POLLING_FILE_WATCHER="${DOTNET_USE_POLLING_FILE_WATCHER:-1}"
export DOTNET_WATCH_RESTART_ON_RUDE_EDIT="${DOTNET_WATCH_RESTART_ON_RUDE_EDIT:-1}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# Evita perfil temporario (/tmp) e separa o bypass em perfil DEV isolado.
# Assim nao mistura base/chave do ambiente normal com testes do painel direto.
if [[ -z "${XDG_DATA_HOME:-}" || "${XDG_DATA_HOME}" == /tmp/* || "${XDG_DATA_HOME}" == /var/tmp/* ]]; then
  export XDG_DATA_HOME="${PROTONS_XDG_DATA_HOME:-$HOME/.local/share/protons-dev}"
fi

now_ms() {
  date +%s%3N
}

print_timing() {
  local label="$1"
  local value="$2"
  printf '[startup-profile] %s=%sms\n' "$label" "$value"
}

extract_startup_probe_ms() {
  local log_file="$1"
  sed -n 's/.*StartupProbe: \([0-9][0-9]*\) ms.*/\1/p' "$log_file" | tail -n 1
}

extract_watch_process_ms() {
  local log_file="$1"
  local index="$2"
  sed -n 's/.* ran for \([0-9][0-9]*\)ms/\1/p' "$log_file" | sed -n "${index}p"
}

extract_watch_file_count() {
  local log_file="$1"
  sed -n 's/.*Watching \([0-9][0-9]*\) file(s) for changes/\1/p' "$log_file" | tail -n 1
}

resolve_debug_output() {
  if [[ -x "$DEBUG_OUTPUT" && -f "$DEBUG_OUTPUT_DEPS" && -f "$DEBUG_OUTPUT_RUNTIMECONFIG" ]]; then
    printf '%s\n' "$DEBUG_OUTPUT"
    return
  fi

  if [[ -f "$DEBUG_OUTPUT_DLL" && -f "$DEBUG_OUTPUT_DEPS" && -f "$DEBUG_OUTPUT_RUNTIMECONFIG" ]]; then
    printf '%s\n' "$DEBUG_OUTPUT_DLL"
    return
  fi

  printf '%s\n' "$DEBUG_OUTPUT"
}

validate_debug_runtime_files() {
  [[ -f "$DEBUG_OUTPUT_DEPS" && -f "$DEBUG_OUTPUT_RUNTIMECONFIG" ]] || return 1

  python3 - "$DEBUG_OUTPUT_DEPS" "$(dirname "$DEBUG_OUTPUT")" <<'PY'
import json
import os
import sys

deps_path, output_dir = sys.argv[1:]

with open(deps_path, "r", encoding="utf-8") as fh:
    data = json.load(fh)

targets = data.get("targets") or {}
target = next(iter(targets.values()), {})
missing = set()

for _, library in target.items():
    runtime = library.get("runtime") or {}
    for relative_path in runtime:
        if relative_path.endswith("/_._"):
            continue

        filename = os.path.basename(relative_path)
        candidate = os.path.join(output_dir, filename)
        if not os.path.exists(candidate):
            missing.add(filename)

if missing:
    for item in sorted(missing):
        print(item)
    sys.exit(1)
PY
}

needs_restore_debug() {
  if [[ ! -f "$RESTORE_GRAPH_FILE" ]]; then
    return 0
  fi

  local markers=(
    "$ROOT_DIR/global.json"
    "$ROOT_DIR/NuGet.config"
    "$ROOT_DIR/Protons.UI/Protons.UI.csproj"
    "$ROOT_DIR/Protons.Core/Protons.Core.csproj"
    "$ROOT_DIR/Protons.Infrastructure/Protons.Infrastructure.csproj"
  )

  local marker
  for marker in "${markers[@]}"; do
    [[ -e "$marker" ]] || continue
    if [[ "$marker" -nt "$RESTORE_GRAPH_FILE" ]]; then
      return 0
    fi
  done

  return 1
}

needs_debug_build() {
  local output_path
  output_path="$(resolve_debug_output)"

  if [[ ! -x "$DEBUG_OUTPUT" && ! -f "$DEBUG_OUTPUT_DLL" ]]; then
    return 0
  fi

  if ! validate_debug_runtime_files >/dev/null 2>&1; then
    return 0
  fi

  if find \
    "$ROOT_DIR/Protons.UI" \
    "$ROOT_DIR/Protons.Core" \
    "$ROOT_DIR/Protons.Infrastructure" \
    "$PANEL_SOURCE_DIR" \
    \( -path "*/bin/*" -o -path "*/obj/*" \) -prune -o \
    -type f -newer "$output_path" -print -quit | grep -q .; then
    return 0
  fi

  return 1
}

restore_debug_graph() {
  echo "Restaurando dependencias do painel direto (Debug)..."
  dotnet restore "$WATCH_PROJECT" -nologo
}

build_debug_output_fast() {
  echo "Compilando painel direto (Debug, sem Hot Reload)..."
  dotnet build "$WATCH_PROJECT" -c Debug --no-restore --disable-build-servers -p:ProduceReferenceAssembly=false -clp:ErrorsOnly
}

build_debug_output_full() {
  echo "Refazendo compilacao completa do painel direto (Debug)..."
  dotnet build "$WATCH_PROJECT" -c Debug --disable-build-servers -p:ProduceReferenceAssembly=false -clp:ErrorsOnly
}

ensure_debug_output_ready() {
  local restore_needed=0
  local build_needed=0

  if needs_restore_debug; then
    restore_needed=1
    build_needed=1
  fi

  if needs_debug_build; then
    build_needed=1
  fi

  if (( restore_needed )); then
    restore_debug_graph
  fi

  if (( build_needed )); then
    build_debug_output_fast
  fi

  if validate_debug_runtime_files >/dev/null 2>&1; then
    return 0
  fi

  echo "Saida Debug incompleta detectada; executando restore + build completo..."
  restore_debug_graph
  build_debug_output_full

  if validate_debug_runtime_files >/dev/null 2>&1; then
    return 0
  fi

  echo "Falha: saida Debug permaneceu incompleta apos restore/build." >&2
  validate_debug_runtime_files || true
  return 1
}

run_debug_output() {
  local output_path
  output_path="$(resolve_debug_output)"

  if [[ -x "$DEBUG_OUTPUT" ]]; then
    "$DEBUG_OUTPUT"
    return
  fi

  if [[ -f "$DEBUG_OUTPUT_DLL" ]]; then
    dotnet "$DEBUG_OUTPUT_DLL"
    return
  fi

  dotnet run --project "$WATCH_PROJECT" -c Debug --no-build --disable-build-servers
}

run_no_hot_reload_fast() {
  ensure_debug_output_ready

  echo "Iniciando painel direto (Debug, sem Hot Reload, sem watch)..."
  run_debug_output
}

run_watch_probe() {
  local log_file="$1"
  shift

  stop_watch_process() {
    local pid="$1"
    local stop_deadline_ms=$(( $(now_ms) + 5000 ))

    kill "$pid" >/dev/null 2>&1 || true

    while kill -0 "$pid" >/dev/null 2>&1; do
      if (( $(now_ms) >= stop_deadline_ms )); then
        kill -9 "$pid" >/dev/null 2>&1 || true
        break
      fi

      sleep 0.1
    done

    wait "$pid" >/dev/null 2>&1 || true
  }

  env "$@" \
    DOTNET_USE_POLLING_FILE_WATCHER=1 \
    dotnet watch \
      --verbose \
      --non-interactive \
      --disable-build-servers \
      --project "$WATCH_PROJECT" \
      -c Debug \
      --no-hot-reload \
      run >"$log_file" 2>&1 &

  local watch_pid=$!
  local deadline_ms=$(( $(now_ms) + 30000 ))

  while true; do
    if grep -q 'StartupProbe:' "$log_file" 2>/dev/null; then
      stop_watch_process "$watch_pid"
      return 0
    fi

    if ! kill -0 "$watch_pid" >/dev/null 2>&1; then
      wait "$watch_pid" >/dev/null 2>&1 || true
      return 1
    fi

    if (( $(now_ms) >= deadline_ms )); then
      stop_watch_process "$watch_pid"
      return 124
    fi

    sleep 0.1
  done
}

profile_startup() {
  local cleanup_ms=0
  local shutdown_ms=0
  local build_ms=0
  local run_no_build_ms=0
  local direct_binary_ms=0
  local watch_probe_total_ms=0
  local step_started_ms=0
  local watch_probe_status=0

  local build_log
  local run_log
  local watch_log
  build_log="$(mktemp)"
  run_log="$(mktemp)"
  watch_log="$(mktemp)"

  local common_env=(
    "PROTONS_STARTUP_PROBE=1"
    "PROTONS_PAINEL_DIRETO=1"
    "PROTONS_PAINEL_DIRETO_HABILITADO=${PROTONS_PAINEL_DIRETO_HABILITADO}"
    "PROTONS_ENVIRONMENT=${PROTONS_ENVIRONMENT}"
    "DOTNET_ENVIRONMENT=${DOTNET_ENVIRONMENT}"
    "PROTONS_PAINEL_USER_ID=${PROTONS_PAINEL_USER_ID}"
    "PROTONS_PAINEL_EMAIL=${PROTONS_PAINEL_EMAIL}"
    "PROTONS_PAINEL_NOME=${PROTONS_PAINEL_NOME}"
    "PROTONS_PAINEL_ROLE=${PROTONS_PAINEL_ROLE}"
    "XDG_DATA_HOME=${XDG_DATA_HOME}"
    "DOTNET_CLI_TELEMETRY_OPTOUT=1"
  )

  echo "Perfilando startup do painel direto (Debug)..."

  step_started_ms="$(now_ms)"
  pkill -f "dotnet watch.*${WATCH_PROJECT}" >/dev/null 2>&1 || true
  pkill -f "dotnet-watch.dll.*${WATCH_PROJECT}" >/dev/null 2>&1 || true
  cleanup_ms=$(( $(now_ms) - step_started_ms ))
  print_timing "prelaunch_watch_cleanup" "$cleanup_ms"

  step_started_ms="$(now_ms)"
  dotnet build-server shutdown >/dev/null 2>&1 || true
  shutdown_ms=$(( $(now_ms) - step_started_ms ))
  print_timing "build_server_shutdown" "$shutdown_ms"

  step_started_ms="$(now_ms)"
  dotnet build "$WATCH_PROJECT" -c Debug --no-restore --disable-build-servers >"$build_log" 2>&1
  build_ms=$(( $(now_ms) - step_started_ms ))
  print_timing "dotnet_build_debug_no_restore" "$build_ms"

  step_started_ms="$(now_ms)"
  env "${common_env[@]}" dotnet run --project "$WATCH_PROJECT" -c Debug --no-build >"$run_log" 2>&1
  run_no_build_ms=$(( $(now_ms) - step_started_ms ))
  print_timing "dotnet_run_debug_no_build_total" "$run_no_build_ms"

  step_started_ms="$(now_ms)"
  env "${common_env[@]}" bash -lc 'run_debug_output() {
    local output="$1"
    local output_dll="$2"
    local watch_project="$3"
    if [[ -x "$output" ]]; then
      "$output"
      return
    fi
    if [[ -f "$output_dll" ]]; then
      dotnet "$output_dll"
      return
    fi
    dotnet run --project "$watch_project" -c Debug --no-build --disable-build-servers
  }; run_debug_output "$0" "$1" "$2"' \
    "$DEBUG_OUTPUT" "$DEBUG_OUTPUT_DLL" "$WATCH_PROJECT" >"$build_log.direct" 2>&1
  direct_binary_ms=$(( $(now_ms) - step_started_ms ))
  print_timing "debug_output_direct_total" "$direct_binary_ms"

  step_started_ms="$(now_ms)"
  if run_watch_probe "$watch_log" "${common_env[@]}"; then
    watch_probe_status=0
  else
    watch_probe_status=$?
  fi
  watch_probe_total_ms=$(( $(now_ms) - step_started_ms ))
  print_timing "dotnet_watch_no_hot_reload_probe_total" "$watch_probe_total_ms"

  local startup_probe_ms
  startup_probe_ms="$(extract_startup_probe_ms "$run_log")"
  if [[ -n "$startup_probe_ms" ]]; then
    print_timing "startup_probe_inside_app" "$startup_probe_ms"
  fi

  local watch_startup_probe_ms
  watch_startup_probe_ms="$(extract_startup_probe_ms "$watch_log")"
  if [[ -n "$watch_startup_probe_ms" ]]; then
    print_timing "startup_probe_inside_watch_child" "$watch_startup_probe_ms"
  fi

  local watch_generate_ms
  watch_generate_ms="$(extract_watch_process_ms "$watch_log" 1)"
  if [[ -n "$watch_generate_ms" ]]; then
    print_timing "dotnet_watch_generate_watch_list" "$watch_generate_ms"
  fi

  local watch_child_ms
  watch_child_ms="$(extract_watch_process_ms "$watch_log" 2)"
  if [[ -n "$watch_child_ms" ]]; then
    print_timing "dotnet_watch_child_process" "$watch_child_ms"
  fi

  local watch_file_count
  watch_file_count="$(extract_watch_file_count "$watch_log")"
  if [[ -n "$watch_file_count" ]]; then
    printf '[startup-profile] watched_files=%s\n' "$watch_file_count"
  fi

  printf '[startup-profile] watch_probe_status=%s\n' "$watch_probe_status"
  printf '[startup-profile] build_vs_run_gap=%sms\n' "$(( build_ms - run_no_build_ms ))"
  printf '[startup-profile] logs_saved=%s,%s,%s,%s\n' "$build_log" "$run_log" "$build_log.direct" "$watch_log"
}

if [[ "$MODE" == "profile-startup" ]]; then
  profile_startup
  exit 0
fi

# Evita lock no Protons.UI.pdb:
# 1) encerra watches antigos do mesmo projeto
# 2) desliga build servers antes de iniciar novo watch
pkill -f "dotnet watch.*${WATCH_PROJECT}" >/dev/null 2>&1 || true
pkill -f "dotnet-watch.dll.*${WATCH_PROJECT}" >/dev/null 2>&1 || true
dotnet build-server shutdown >/dev/null 2>&1 || true

WATCH_BASE_ARGS=(--non-interactive --disable-build-servers --project "$WATCH_PROJECT" -c Debug)

if [[ "$MODE" == "no-hot-reload" ]]; then
  run_no_hot_reload_fast
  exit 0
fi

if [[ "$MODE" != "debug" ]]; then
  echo "Uso: $0 [debug|no-hot-reload|profile-startup]"
  exit 1
fi

echo "Iniciando painel direto (Debug, Hot Reload)..."
dotnet watch "${WATCH_BASE_ARGS[@]}" run
