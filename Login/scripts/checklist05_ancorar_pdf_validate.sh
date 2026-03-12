#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Uso:
  bash Login/scripts/checklist05_ancorar_pdf_validate.sh \
    --profile baseline|full \
    [--ci] \
    [--write-report] \
    [--sync-checklist never|release]

Exemplos:
  bash Login/scripts/checklist05_ancorar_pdf_validate.sh --profile baseline --ci --write-report --sync-checklist never
  bash Login/scripts/checklist05_ancorar_pdf_validate.sh --profile full --ci --write-report --sync-checklist release
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

RESULTS_DIR="$ROOT_DIR/Login/testes/TestResults/checklist05_ancora_pdf"
RESULTS_FULL_DIR="$RESULTS_DIR/full"
TRX_DIR="$RESULTS_DIR/trx"
OBJ_ROOT="$ROOT_DIR/Login/testes/.obj_ci/checklist05_ancorar_pdf"
ARTIFACTS_ROOT="${TMPDIR:-/tmp}/protons_checklist05_artifacts"

mkdir -p "$RESULTS_DIR" "$RESULTS_FULL_DIR" "$TRX_DIR" "$OBJ_ROOT" "$ARTIFACTS_ROOT"
rm -f "$RESULTS_DIR"/C5_*.log "$RESULTS_DIR"/summary.json "$RESULTS_DIR"/summary.md "$RESULTS_DIR"/checklist_block.md
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
pending_strict=0
if [[ "${PROTONS_C5_PENDING_STRICT:-0}" == "1" || "${PROTONS_C5_PENDING_STRICT:-0}" == "true" ]]; then
  pending_strict=1
fi

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

  echo "[checklist05] Executando ${gate_id}..."
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

  echo "[checklist05] ${gate_id}: ${gate_status} (${duration_seconds}s, ${gate_blocking})"
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

# --- G1: Build da solucao ---
g1_artifacts="$(gate_artifacts_path "C5_G1_Build")"
gate_cmd_g1="dotnet restore \"$SOLUTION_PATH\" --disable-build-servers -p:ArtifactsPath=\"$g1_artifacts\" && dotnet build \"$SOLUTION_PATH\" -c Debug --no-restore -warnaserror --artifacts-path \"$g1_artifacts\" -m:1 --disable-build-servers"

# --- G2: Testes Core (contratos de modelos C5) ---
gate_cmd_g2="$(build_core_test_command "Checklist=C5&Category=C5_G1_Contratos" "C5_G2_Contratos.trx" "C5_G2")"

# --- G3: Testes Infrastructure (schema v10 + persistencia de fila e lease) ---
gate_cmd_g3_schema="$(build_infra_test_command "Checklist=C5&ChecklistGate=C5_G2_Schema" "C5_G3_Schema.trx" "C5_G3_Schema")"
gate_cmd_g3_repos="$(build_infra_test_command  "Checklist=C5&ChecklistGate=C5_G3_Repos"  "C5_G3_Repos.trx"  "C5_G3_Repos")"
gate_cmd_g3="$(build_infra_test_command        "Checklist=C5"                             "C5_G3_Infra.trx"  "C5_G3")"

# --- G4: Resiliencia do worker (retry, timeout, falha de negocio, restart recovery) ---
gate_cmd_g4="$(build_infra_test_command "Checklist=C5&Category=C5_G4_Resiliencia" "C5_G4_Resiliencia.trx" "C5_G4")"

# --- F1-F3: Gates full (concorrencia multi-worker) ---
gate_cmd_f_conc="$(build_infra_test_command "Checklist=C5&Category=C5_G5_Concorrencia" "C5_F_Concorrencia.trx" "C5_F_Conc")"

# --- Execucao dos gates principais (blocking) ---
run_gate "C5_G1_Build"              "Restore + build da solucao com warnings como erro."                        "$gate_cmd_g1"
run_gate "C5_G2_Core_Contratos"     "Testes Core: contratos dos modelos de fila de execucao C5."               "$gate_cmd_g2"
run_gate "C5_G3_Infra_Persistencia" "Testes Infrastructure: schema v10, repos fila e lease SQLite."            "$gate_cmd_g3"
run_gate "C5_G4_Resiliencia"        "Testes Integration: retry, timeout, falha negocio, restart recovery."     "$gate_cmd_g4"

# --- Gates pendentes (non_blocking por padrao; strict via env PROTONS_C5_PENDING_STRICT=1) ---
pending_gate_blocking="non_blocking"
if [[ $pending_strict -eq 1 ]]; then
  pending_gate_blocking="blocking"
fi

run_gate "C5_P1" "Schema v10: tabela AncorarPdfExecucaoFila e indices criados."         "$gate_cmd_g3_schema" "baseline" "$pending_gate_blocking"
run_gate "C5_P2" "Schema v10: tabela AncorarPdfExecucaoLease e indices criados."        "$gate_cmd_g3_schema" "baseline" "$pending_gate_blocking"
run_gate "C5_P3" "Enfileirar idempotente: ON CONFLICT DO NOTHING (sem duplicata)."     "$gate_cmd_g3_repos"  "baseline" "$pending_gate_blocking"
run_gate "C5_P4" "Claim exclusivo: segundo worker nao consegue o mesmo item."          "$gate_cmd_g3_repos"  "baseline" "$pending_gate_blocking"
run_gate "C5_P5" "Cancelar item Aguardando: sucesso; EmProcessamento: falha esperada." "$gate_cmd_g3_repos"  "baseline" "$pending_gate_blocking"
run_gate "C5_P6" "Reidratacao: ListarParaReidratar retorna apenas sem lease ativo."    "$gate_cmd_g3_repos"  "baseline" "$pending_gate_blocking"
run_gate "C5_P7" "Lease exclusivo: ON CONFLICT garante 1 ativo por item."              "$gate_cmd_g3_repos"  "baseline" "$pending_gate_blocking"
run_gate "C5_P8" "Lease expirado: ListarExpirados retorna leases com ExpiresAtUtc<now." "$gate_cmd_g3_repos"  "baseline" "$pending_gate_blocking"

if [[ "$PROFILE" == "full" ]]; then
  run_gate "C5_F1" "Concorrencia: 2 workers, 10 itens, cada item processado exatamente 1x."  "$gate_cmd_f_conc" "full"
  run_gate "C5_F2" "ObterProfundidadeAtual: reflete canal em tempo real (>= 0)."             "$gate_cmd_f_conc" "full"
  run_gate "C5_F3" "Back-pressure: channel Wait mode nao descarta dados sob pico."           "$gate_cmd_f_conc" "full"
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
    echo "  \"checklist\": \"Checklist05_AncorarPdf\","
    echo "  \"generatedAtUtc\": \"$(json_escape "$utc_timestamp")\","
    echo "  \"gitCommit\": \"$(json_escape "$git_sha")\","
    echo "  \"profile\": \"$(json_escape "$PROFILE")\","
    echo "  \"ci\": $([[ $CI_MODE -eq 1 ]] && echo "true" || echo "false"),"
    echo "  \"syncChecklist\": \"$(json_escape "$SYNC_CHECKLIST")\","
    echo "  \"pendingStrict\": $([[ $pending_strict -eq 1 ]] && echo "true" || echo "false"),"
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
    echo "# Checklist 05 - Ancorar PDF (validacao automatica)"
    echo
    echo "- Gerado em UTC: \`$utc_timestamp\`"
    echo "- Commit: \`$git_sha\`"
    echo "- Perfil: \`$PROFILE\`"
    echo "- CI: \`$([[ $CI_MODE -eq 1 ]] && echo "true" || echo "false")\`"
    echo "- Sync checklist: \`$SYNC_CHECKLIST\`"
    echo "- C5_P strict mode: \`$([[ $pending_strict -eq 1 ]] && echo "true" || echo "false")\`"
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
      gate_blocking="${GATE_BLOCKING[$idx]}"
      echo "- gate_${gate_id}: \`$gate_status\` [\`$gate_blocking\`] (\`$gate_log_rel\`)"
    done
  } >"$summary_block"

  if [[ "$SYNC_CHECKLIST" == "release" ]]; then
    bash "$ROOT_DIR/Login/scripts/checklist05_ancorar_pdf_sync_md.sh" \
      --block-file "$summary_block" \
      --checklist "$CHECKLIST_PATH"
  fi
fi

if [[ "$overall_status" != "PASS" ]]; then
  echo "Checklist 05 falhou. Veja logs em: $RESULTS_DIR"
  exit 1
fi

echo "Checklist 05 concluido com sucesso."
