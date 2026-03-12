#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Uso:
  bash Login/scripts/checklist01_ancorar_pdf_validate.sh \
    --profile baseline|full \
    [--ci] \
    [--write-report] \
    [--sync-checklist never|release]

Exemplos:
  bash Login/scripts/checklist01_ancorar_pdf_validate.sh --profile baseline --ci --write-report --sync-checklist never
  bash Login/scripts/checklist01_ancorar_pdf_validate.sh --profile full --ci --write-report --sync-checklist release
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

RESULTS_DIR="$ROOT_DIR/Login/testes/TestResults/checklist_ancora_pdf"
RESULTS_FULL_DIR="$RESULTS_DIR/full"
TRX_DIR="$RESULTS_DIR/trx"
OBJ_ROOT="$ROOT_DIR/Login/testes/.obj_ci/checklist01_ancorar_pdf"
ARTIFACTS_ROOT="${TMPDIR:-/tmp}/protons_checklist01_artifacts"

mkdir -p "$RESULTS_DIR" "$RESULTS_FULL_DIR" "$TRX_DIR" "$OBJ_ROOT" "$ARTIFACTS_ROOT"
rm -f "$RESULTS_DIR"/G*.log "$RESULTS_DIR"/summary.json "$RESULTS_DIR"/summary.md "$RESULTS_DIR"/checklist_block.md
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

run_gate() {
  local gate_id="$1"
  local gate_description="$2"
  local gate_command="$3"
  local gate_scope="${4:-baseline}"

  local log_path
  if [[ "$gate_scope" == "full" ]]; then
    log_path="$RESULTS_FULL_DIR/${gate_id}.log"
  else
    log_path="$RESULTS_DIR/${gate_id}.log"
  fi

  local started_at ended_at duration_seconds exit_code gate_status
  started_at="$(date +%s)"

  echo "[checklist01] Executando ${gate_id}..."
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
      blocking_failures=1
      {
        echo
        echo "# gate_validation_error=no_tests_executed"
      } >>"$log_path"
    else
      gate_status="PASS"
    fi
  else
    gate_status="FAIL"
    blocking_failures=1
  fi

  GATE_IDS+=("$gate_id")
  GATE_DESCRIPTIONS+=("$gate_description")
  GATE_COMMANDS+=("$gate_command")
  GATE_STATUSES+=("$gate_status")
  GATE_DURATIONS+=("$duration_seconds")
  GATE_LOGS+=("$log_path")

  echo "[checklist01] ${gate_id}: ${gate_status} (${duration_seconds}s)"
}

build_core_test_command() {
  local _gate_id="$1"
  local filter="$2"
  local trx_file="$3"

  printf 'dotnet test "%s" -c Debug --filter "%s" --logger "trx;LogFileName=%s" --results-directory "%s" --artifacts-path "%s"' \
    "$CORE_TEST_PROJECT" \
    "$filter" \
    "$trx_file" \
    "$TRX_DIR" \
    "$ARTIFACTS_PATH"
}

build_infra_test_command() {
  local _gate_id="$1"
  local filter="$2"
  local trx_file="$3"

  printf 'dotnet test "%s" -c Debug --filter "%s" --logger "trx;LogFileName=%s" --results-directory "%s" --artifacts-path "%s"' \
    "$INFRA_TEST_PROJECT" \
    "$filter" \
    "$trx_file" \
    "$TRX_DIR" \
    "$ARTIFACTS_PATH"
}

gate_cmd_g1="dotnet restore \"$SOLUTION_PATH\" -p:ArtifactsPath=\"$ARTIFACTS_PATH\" && dotnet build \"$SOLUTION_PATH\" -c Debug --no-restore -warnaserror --artifacts-path \"$ARTIFACTS_PATH\""
gate_cmd_g2="$(build_core_test_command "G2_Core_Contracts_AncorarPdf" "FullyQualifiedName~AncorarPdfChecklist01ContractTests" "G2_Core_Contracts_AncorarPdf.trx")"
gate_cmd_g3="$(build_infra_test_command "G3_Infra_Migrations" "FullyQualifiedName~SqliteDbMigrationTests|FullyQualifiedName~PostgresUserRepositoryTests|FullyQualifiedName~AncorarPdfChecklist01PersistenceTests" "G3_Infra_Migrations.trx")"
gate_cmd_g4="$(build_infra_test_command "G4_Integracao_Painel_Base" "FullyQualifiedName~PainelAncorarPdfChecklist01ViewModelTests" "G4_Integracao_Painel_Base.trx")"
gate_cmd_g5="PROTONS_DOTNET_ARTIFACTS_PATH=\"$ARTIFACTS_PATH\" PROTONS_MSBUILD_PROJECT_EXTENSIONS_PATH=\"$OBJ_PATH/g5_smoke\" bash \"$ROOT_DIR/Login/scripts/smoke_painel.sh\""

run_gate "G1_Build" "Restore + build da solucao com warnings como erro." "$gate_cmd_g1"
run_gate "G2_Core_Contracts_AncorarPdf" "Contratos de dominio do ancorar_pdf (ids, selecao, retry, idempotencia)." "$gate_cmd_g2"
run_gate "G3_Infra_Migrations" "Migracoes e persistencia base SQLite/Postgres." "$gate_cmd_g3"
run_gate "G4_Integracao_Painel_Base" "Integracao de ViewModel no drop ancorar_pdf." "$gate_cmd_g4"
run_gate "G5_Smoke_Operacional" "Smoke operacional oficial do painel." "$gate_cmd_g5"

if [[ "$PROFILE" == "full" ]]; then
  run_gate "F1" "Drop abre configuracao sem erro." \
    "$(build_infra_test_command "F1" "FullyQualifiedName~F1_" "F1.trx")" \
    "full"
  run_gate "F2" "Salvar configuracao posiciona no segundo correto." \
    "$(build_infra_test_command "F2" "FullyQualifiedName~F2_" "F2.trx")" \
    "full"
  run_gate "F3" "Isolamento entre clientes." \
    "$(build_infra_test_command "F3" "FullyQualifiedName~F3_" "F3.trx")" \
    "full"
  run_gate "F4" "Reabertura da configuracao por clique na regua." \
    "$(build_infra_test_command "F4" "FullyQualifiedName~F4_" "F4.trx")" \
    "full"
  run_gate "F5" "Tarefa passada abre em modo leitura." \
    "$(build_infra_test_command "F5" "FullyQualifiedName~F5_" "F5.trx")" \
    "full"
  run_gate "F6" "Assinatura completa persistida (nome/userId/data-hora/cliente)." \
    "$(build_infra_test_command "F6" "FullyQualifiedName~F6_" "F6.trx")" \
    "full"
  run_gate "F7" "Idempotencia de reprocessamento." \
    "$(build_infra_test_command "F7" "FullyQualifiedName~F7_" "F7.trx")" \
    "full"
  run_gate "F8" "Bloqueio por validacao de cliente." \
    "$(build_infra_test_command "F8" "FullyQualifiedName~F8_" "F8.trx")" \
    "full"
  run_gate "F9" "Retry tecnico 5/20/60 e sem retry para erro de negocio." \
    "$(build_infra_test_command "F9" "FullyQualifiedName~F9_" "F9.trx")" \
    "full"
  run_gate "F10" "Misfire/backlog com decisao explicita." \
    "$(build_infra_test_command "F10" "FullyQualifiedName~F10_" "F10.trx")" \
    "full"
  run_gate "F11" "Rodape/historico com estados completos." \
    "$(build_infra_test_command "F11" "FullyQualifiedName~F11_" "F11.trx")" \
    "full"
  run_gate "F12" "Notificacoes refletem execucao real." \
    "$(build_infra_test_command "F12" "FullyQualifiedName~F12_" "F12.trx")" \
    "full"
  run_gate "F13" "Saida ancorada disponivel para consumo futuro." \
    "$(build_infra_test_command "F13" "FullyQualifiedName~F13_" "F13.trx")" \
    "full"
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
    echo "  \"checklist\": \"Checklist01_AncorarPdf\","
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
      suffix=","
      if [[ "$idx" -eq "$((${#GATE_IDS[@]} - 1))" ]]; then
        suffix=""
      fi
      echo "    {"
      echo "      \"id\": \"$(json_escape "$gate_id")\","
      echo "      \"description\": \"$(json_escape "$gate_desc")\","
      echo "      \"status\": \"$(json_escape "$gate_status")\","
      echo "      \"durationSeconds\": $gate_duration,"
      echo "      \"command\": \"$(json_escape "$gate_cmd")\","
      echo "      \"evidence\": \"$(json_escape "$gate_log_rel")\""
      echo "    }$suffix"
    done
    echo "  ]"
    echo "}"
  } >"$summary_json"

  {
    echo "# Checklist 01 - Ancorar PDF (validacao automatica)"
    echo
    echo "- Gerado em UTC: \`$utc_timestamp\`"
    echo "- Commit: \`$git_sha\`"
    echo "- Perfil: \`$PROFILE\`"
    echo "- CI: \`$([[ $CI_MODE -eq 1 ]] && echo "true" || echo "false")\`"
    echo "- Sync checklist: \`$SYNC_CHECKLIST\`"
    echo "- Status geral: \`$overall_status\`"
    echo "- Artefato JSON: \`$(to_rel_path "$summary_json")\`"
    echo
    echo "| Gate | Status | Duracao(s) | Evidencia |"
    echo "|---|---|---:|---|"
    for idx in "${!GATE_IDS[@]}"; do
      gate_id="${GATE_IDS[$idx]}"
      gate_status="${GATE_STATUSES[$idx]}"
      gate_duration="${GATE_DURATIONS[$idx]}"
      gate_log_rel="$(to_rel_path "${GATE_LOGS[$idx]}")"
      echo "| \`$gate_id\` | \`$gate_status\` | $gate_duration | \`$gate_log_rel\` |"
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
      echo "- gate_${gate_id}: \`${gate_status}\` (\`$gate_log_rel\`)"
    done
  } >"$summary_block"

  if [[ "$SYNC_CHECKLIST" == "release" ]]; then
    bash "$ROOT_DIR/Login/scripts/checklist01_ancorar_pdf_sync_md.sh" \
      --checklist "$CHECKLIST_PATH" \
      --block-file "$summary_block"
  fi
fi

echo "[checklist01] Perfil: $PROFILE"
echo "[checklist01] Status geral: $overall_status"
echo "[checklist01] Evidencias: $(to_rel_path "$RESULTS_DIR")"

if [[ $blocking_failures -ne 0 ]]; then
  exit 1
fi
