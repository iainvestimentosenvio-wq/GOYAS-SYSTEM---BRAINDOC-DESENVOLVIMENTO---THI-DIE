#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Uso:
  bash Login/scripts/checklist13_timezone_matrix_validate.sh \
    --profile baseline|full \
    [--ci] \
    [--write-report] \
    [--sync-checklist never|release]

Exemplos:
  bash Login/scripts/checklist13_timezone_matrix_validate.sh --profile baseline --ci
  bash Login/scripts/checklist13_timezone_matrix_validate.sh --profile full --ci --write-report
USAGE
}

PROFILE="baseline"
CI_MODE=0
WRITE_REPORT=0
SYNC_CHECKLIST="never"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --profile)
      PROFILE="${2:-}"
      shift 2
      ;;
    --ci)
      CI_MODE=1
      shift
      ;;
    --write-report)
      WRITE_REPORT=1
      shift
      ;;
    --sync-checklist)
      SYNC_CHECKLIST="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Parametro invalido: $1" >&2
      usage
      exit 2
      ;;
  esac
done

if [[ "$PROFILE" != "baseline" && "$PROFILE" != "full" ]]; then
  echo "Valor invalido para --profile: $PROFILE (use baseline ou full)." >&2
  exit 2
fi

if [[ "$SYNC_CHECKLIST" != "never" && "$SYNC_CHECKLIST" != "release" ]]; then
  echo "Valor invalido para --sync-checklist: $SYNC_CHECKLIST (use never ou release)." >&2
  exit 2
fi

if [[ "$SYNC_CHECKLIST" == "release" ]]; then
  WRITE_REPORT=1
fi

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOLUTION_PATH="$ROOT_DIR/Login/Protons.sln"
CORE_TEST_PROJECT="$ROOT_DIR/Login/testes/Protons.Core.Tests/Protons.Core.Tests.csproj"
INFRA_TEST_PROJECT="$ROOT_DIR/Login/testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj"
CHECKLIST_PATH="$ROOT_DIR/painel principal/codigos/painel_principal/funcionalidades/ferramentas/ancorar_pdf/documentacao/CHECKLIST_ANCORA_PDF.md"

RESULTS_DIR="$ROOT_DIR/Login/testes/TestResults/checklist13_timezone_matrix"
RESULTS_FULL_DIR="$RESULTS_DIR/full"
TRX_DIR="$RESULTS_DIR/trx"
OBJ_ROOT="$ROOT_DIR/Login/testes/.obj_ci/checklist13_timezone_matrix"
ARTIFACTS_ROOT="${TMPDIR:-/tmp}/protons_checklist13_artifacts"

mkdir -p "$RESULTS_DIR" "$RESULTS_FULL_DIR" "$TRX_DIR" "$OBJ_ROOT" "$ARTIFACTS_ROOT"
rm -f "$RESULTS_DIR"/C13_*.log "$RESULTS_DIR"/summary.json "$RESULTS_DIR"/summary.md "$RESULTS_DIR"/checklist_block.md
rm -f "$RESULTS_FULL_DIR"/*.log "$TRX_DIR"/*.trx

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)_$$"
ARTIFACTS_PATH="$ARTIFACTS_ROOT/$RUN_ID"
OBJ_PATH="$OBJ_ROOT/$RUN_ID"
mkdir -p "$ARTIFACTS_PATH" "$OBJ_PATH"

utc_timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
git_sha="$(git -C "$ROOT_DIR" rev-parse --short HEAD 2>/dev/null || echo "sem_git")"

declare -a GATE_IDS=()
declare -a GATE_DESCRIPTIONS=()
declare -a GATE_COMMANDS=()
declare -a GATE_STATUSES=()
declare -a GATE_DURATIONS=()
declare -a GATE_LOGS=()
declare -a GATE_BLOCKING=()

blocking_failures=0

to_rel_path() {
  local abs="$1"
  if [[ "$abs" == "$ROOT_DIR/"* ]]; then
    printf '%s' "${abs#"$ROOT_DIR"/}"
  else
    printf '%s' "$abs"
  fi
}

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g; s/\r/\\r/g; s/\n/\\n/g'
}

gate_artifacts_path() {
  local gate_id="$1"
  local gate_artifacts="$ARTIFACTS_PATH/$gate_id"
  mkdir -p "$gate_artifacts"
  printf '%s' "$gate_artifacts"
}

run_gate() {
  local gate_id="$1"
  local gate_description="$2"
  local gate_command="$3"
  local gate_scope="${4:-baseline}"
  local gate_blocking="${5:-blocking}"

  local log_path
  if [[ "$gate_scope" == "full" ]]; then
    log_path="$RESULTS_FULL_DIR/${gate_id}.log"
  else
    log_path="$RESULTS_DIR/${gate_id}.log"
  fi

  local started_at ended_at duration_seconds exit_code gate_status
  started_at="$(date +%s)"

  echo "[checklist13] Executando ${gate_id}..."
  set +e
  {
    echo "# gate_id=${gate_id}"
    echo "# gate_description=${gate_description}"
    echo "# started_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "# command=${gate_command}"
    echo
    eval "$gate_command"
  } >"$log_path" 2>&1
  exit_code=$?
  set -e

  ended_at="$(date +%s)"
  duration_seconds="$((ended_at - started_at))"

  if [[ $exit_code -eq 0 ]]; then
    if grep -Eq "No test matches the given testcase filter|No test is available|Total:\s+0" "$log_path"; then
      gate_status="FAIL"
      if [[ "$gate_blocking" == "blocking" ]]; then
        blocking_failures=1
      fi
      {
        echo
        echo "# gate_validation_error=no_tests_executed"
      } >>"$log_path"
    else
      gate_status="PASS"
    fi
  else
    gate_status="FAIL"
    if [[ "$gate_blocking" == "blocking" ]]; then
      blocking_failures=1
    fi
  fi

  GATE_IDS+=("$gate_id")
  GATE_DESCRIPTIONS+=("$gate_description")
  GATE_COMMANDS+=("$gate_command")
  GATE_STATUSES+=("$gate_status")
  GATE_DURATIONS+=("$duration_seconds")
  GATE_LOGS+=("$log_path")
  GATE_BLOCKING+=("$gate_blocking")

  echo "[checklist13] ${gate_id}: ${gate_status} (${duration_seconds}s, ${gate_blocking})"
}

build_core_test_command() {
  local filter="$1"
  local trx_file="$2"
  local gate_id="${3:-core}"
  local gate_artifacts
  gate_artifacts="$(gate_artifacts_path "$gate_id")"

  printf 'dotnet test "%s" -c Debug --filter "%s" --logger "trx;LogFileName=%s" --results-directory "%s" --artifacts-path "%s" --disable-build-servers -m:1' \
    "$CORE_TEST_PROJECT" \
    "$filter" \
    "$trx_file" \
    "$TRX_DIR" \
    "$gate_artifacts"
}

build_infra_test_command() {
  local filter="$1"
  local trx_file="$2"
  local gate_id="${3:-infra}"
  local gate_artifacts
  gate_artifacts="$(gate_artifacts_path "$gate_id")"

  printf 'dotnet test "%s" -c Debug --filter "%s" --logger "trx;LogFileName=%s" --results-directory "%s" --artifacts-path "%s" --disable-build-servers -m:1' \
    "$INFRA_TEST_PROJECT" \
    "$filter" \
    "$trx_file" \
    "$TRX_DIR" \
    "$gate_artifacts"
}

# --- Gate G1: Build da solucao ---
g1_artifacts="$(gate_artifacts_path "C13_G1_Build")"
gate_cmd_g1="dotnet restore \"$SOLUTION_PATH\" --disable-build-servers -p:ArtifactsPath=\"$g1_artifacts\" && dotnet build \"$SOLUTION_PATH\" -c Debug --no-restore -warnaserror --artifacts-path \"$g1_artifacts\" -m:1 --disable-build-servers"

# --- Gate G2: Testes Core — matriz timezone/DST (UTC, Sao Paulo, Berlin) ---
gate_cmd_g2="$(build_core_test_command "Checklist=C13&Category=C13_G2_TzCore" "C13_G2_TzCore.trx" "C13_G2")"

# --- Gate G3: Testes Infra — binding DatePicker + Reset() por timezone ---
gate_cmd_g3="$(build_infra_test_command "Checklist=C13&Category=C13_G3_TzUi" "C13_G3_TzUi.trx" "C13_G3")"

# --- Gate F1 (full): Regressao Core C1–C13 ---
gate_cmd_f1="$(build_core_test_command \
  "Checklist=C1|Checklist=C2|Checklist=C3|Checklist=C4|Checklist=C5|Checklist=C6|Checklist=C7|Checklist=C8|Checklist=C9|Checklist=C10|Checklist=C11|Checklist=C12|Checklist=C13" \
  "C13_F1_CoreRegression.trx" "C13_F1")"

# --- Gate F2 (full): Regressao Infra C1–C13 ---
gate_cmd_f2="$(build_infra_test_command \
  "Checklist=C1|Checklist=C2|Checklist=C3|Checklist=C4|Checklist=C5|Checklist=C6|Checklist=C7|(Checklist=C8&Category=C8_G2_Guards)|Checklist=C9|Checklist=C10|Checklist=C11|Checklist=C12|Checklist=C13" \
  "C13_F2_InfraRegression.trx" "C13_F2")"

# --- Execucao dos gates blocking ---
run_gate "C13_G1_Build"   "Restore + build da solucao com warnings como erro."                                        "$gate_cmd_g1"
run_gate "C13_G2_TzCore"  "Matriz Core: UTC, America/Sao_Paulo, Europe/Berlin DST (spring forward + fall back)."      "$gate_cmd_g2"
run_gate "C13_G3_TzUi"    "Matriz UI: DatePicker binding e Reset() com UTC, BRT, CET/CEST e datas invalidas/ambiguas." "$gate_cmd_g3"

if [[ "$PROFILE" == "full" ]]; then
  run_gate "C13_F1_CoreRegression"  "Regressao completa Core (C1–C13)."  "$gate_cmd_f1" "full"
  run_gate "C13_F2_InfraRegression" "Regressao completa Infra (C1–C13)." "$gate_cmd_f2" "full"
fi

overall_status="PASS"
if [[ $blocking_failures -ne 0 ]]; then
  overall_status="FAIL"
fi

if [[ $WRITE_REPORT -eq 1 || $CI_MODE -eq 1 ]]; then
  summary_json="$RESULTS_DIR/summary.json"
  summary_md="$RESULTS_DIR/summary.md"
  summary_block="$RESULTS_DIR/checklist_block.md"

  {
    echo "{"
    echo "  \"checklist\": \"Checklist13_TimezoneMatrix\","
    echo "  \"generatedAtUtc\": \"$(json_escape "$utc_timestamp")\","
    echo "  \"gitCommit\": \"$(json_escape "$git_sha")\","
    echo "  \"profile\": \"$(json_escape "$PROFILE")\","
    echo "  \"ci\": $([[ $CI_MODE -eq 1 ]] && echo "true" || echo "false"),"
    echo "  \"syncChecklist\": \"$(json_escape "$SYNC_CHECKLIST")\","
    echo "  \"overallStatus\": \"$(json_escape "$overall_status")\","
    echo "  \"gates\": ["
    for idx in "${!GATE_IDS[@]}"; do
      gate_id="${GATE_IDS[$idx]}"
      gate_desc="${GATE_DESCRIPTIONS[$idx]}"
      gate_cmd="${GATE_COMMANDS[$idx]}"
      gate_status="${GATE_STATUSES[$idx]}"
      gate_duration="${GATE_DURATIONS[$idx]}"
      gate_log_rel="$(to_rel_path "${GATE_LOGS[$idx]}")"
      gate_blocking="${GATE_BLOCKING[$idx]}"
      suffix=","
      if [[ "$idx" -eq "$(( ${#GATE_IDS[@]} - 1 ))" ]]; then
        suffix=""
      fi
      echo "    {"
      echo "      \"id\": \"$(json_escape "$gate_id")\","
      echo "      \"description\": \"$(json_escape "$gate_desc")\","
      echo "      \"status\": \"$(json_escape "$gate_status")\","
      echo "      \"blocking\": $([[ "$gate_blocking" == "blocking" ]] && echo "true" || echo "false"),"
      echo "      \"durationSeconds\": $gate_duration,"
      echo "      \"command\": \"$(json_escape "$gate_cmd")\","
      echo "      \"evidence\": \"$(json_escape "$gate_log_rel")\""
      echo "    }$suffix"
    done
    echo "  ]"
    echo "}"
  } >"$summary_json"

  {
    echo "# Checklist 13 - Timezone Matrix: Regressao de Binding DatePicker e DST (validacao automatica)"
    echo
    echo "- Gerado em UTC: \`$utc_timestamp\`"
    echo "- Commit: \`$git_sha\`"
    echo "- Perfil: \`$PROFILE\`"
    echo "- CI: \`$([[ $CI_MODE -eq 1 ]] && echo "true" || echo "false")\`"
    echo "- Sync checklist: \`$SYNC_CHECKLIST\`"
    echo "- Status geral: \`$overall_status\`"
    echo "- Artefato JSON: \`$(to_rel_path "$summary_json")\`"
    echo
    echo "| Gate | Status | Bloqueante | Duracao(s) | Evidencia |"
    echo "|---|---|---|---:|---|"
    for idx in "${!GATE_IDS[@]}"; do
      gate_id="${GATE_IDS[$idx]}"
      gate_status="${GATE_STATUSES[$idx]}"
      gate_duration="${GATE_DURATIONS[$idx]}"
      gate_log_rel="$(to_rel_path "${GATE_LOGS[$idx]}")"
      gate_blocking="${GATE_BLOCKING[$idx]}"
      echo "| \`$gate_id\` | \`$gate_status\` | \`$gate_blocking\` | $gate_duration | \`$gate_log_rel\` |"
    done
  } >"$summary_md"

  {
    echo "- atualizado_em_utc: \`$utc_timestamp\`"
    echo "- commit_sha: \`$git_sha\`"
    echo "- perfil: \`$PROFILE\`"
    echo "- status_geral: \`$overall_status\`"
    echo "- artefato_summary_json: \`$(to_rel_path "$summary_json")\`"
    echo "- artefato_summary_md: \`$(to_rel_path "$summary_md")\`"
    for idx in "${!GATE_IDS[@]}"; do
      gate_id="${GATE_IDS[$idx]}"
      gate_status="${GATE_STATUSES[$idx]}"
      gate_log_rel="$(to_rel_path "${GATE_LOGS[$idx]}")"
      echo "- gate_${gate_id}: \`${gate_status}\` evidencia: \`${gate_log_rel}\`"
    done
  } >"$summary_block"

  echo "[checklist13] Relatorio escrito em: $summary_md"

  if [[ "$SYNC_CHECKLIST" == "release" ]]; then
    bash "$(dirname "${BASH_SOURCE[0]}")/checklist13_timezone_matrix_sync_md.sh" \
      --block-file "$summary_block" \
      --checklist "$CHECKLIST_PATH"
  fi
fi

echo "[checklist13] Status geral: $overall_status"
if [[ $blocking_failures -ne 0 ]]; then
  exit 1
fi
