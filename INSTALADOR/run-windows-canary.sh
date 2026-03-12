#!/usr/bin/env bash
# run-windows-canary.sh
# Canary curto para bloquear rodada longa quando a cadeia minima quebrar.

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

AUTON_SCRIPT="$INSTALADOR_ROOT/comum/scripts/windows-autonomous-round.sh"
ORCHESTRATOR_PY="$INSTALADOR_ROOT/comum/scripts/windows_e2e_orchestrator.py"

usage() {
  cat <<'EOF'
Uso:
  bash run-windows-canary.sh [opcoes]

Opcoes:
  -h, --help                    Exibe ajuda e sai

Variaveis de ambiente:
  CANARY_VM                     VM alvo do canario (default: win10-lite)
  PROTONS_SIGNATURE_PROFILE     technical|production (default: technical)
  CANARY_BOOTSTRAP_PRIMARY_MODE manual-ready|auto (default: auto)
  CANARY_BOOTSTRAP_FALLBACK_ON_FAIL
                                1 para tentar fallback auto se modo primario falhar (default: 1)
EOF
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

[ -x "$AUTON_SCRIPT" ] || { echo "ERRO: script ausente: $AUTON_SCRIPT" >&2; exit 1; }
[ -f "$ORCHESTRATOR_PY" ] || { echo "ERRO: script ausente: $ORCHESTRATOR_PY" >&2; exit 1; }

CANARY_VM="${CANARY_VM:-win10-lite}"
SIGNATURE_PROFILE="${PROTONS_SIGNATURE_PROFILE:-technical}"
CANARY_BOOTSTRAP_PRIMARY_MODE="${CANARY_BOOTSTRAP_PRIMARY_MODE:-auto}"
CANARY_BOOTSTRAP_FALLBACK_ON_FAIL="${CANARY_BOOTSTRAP_FALLBACK_ON_FAIL:-1}"
RUN_ID="CANARY-$(date -u +%Y%m%d%H%M%S)"
RUN_DIR="$INSTALADOR_ROOT/saida/validacao-windows-${RUN_ID}"
CSV_FILE="$RUN_DIR/resumo.csv"
SUMMARY_MD="$RUN_DIR/windows-round-summary.md"
GATES_MD="$RUN_DIR/gates-summary.md"
MANDATORY_CSV="$RUN_DIR/mandatory-suite-summary.csv"

mkdir -p "$RUN_DIR"

latest_step_status() {
  local step="$1"
  awk -F',' -v s="$step" '$1==s { status=$2 } END { if (status!="") print status; }' "$CSV_FILE" 2>/dev/null || true
}

latest_step_evidence() {
  local step="$1"
  awk -F',' -v s="$step" '$1==s { evidence=$4 } END { if (evidence!="") print evidence; }' "$CSV_FILE" 2>/dev/null || true
}

echo "==============================================================="
echo "WINDOWS CANARY - PRE GATE LONGO"
echo "==============================================================="
echo "  VM alvo      : $CANARY_VM"
echo "  RunId        : $RUN_ID"
echo "  Signature    : $SIGNATURE_PROFILE"
echo "  Transport    : iso-strict (obrigatorio)"
echo "  Bootstrap    : $CANARY_BOOTSTRAP_PRIMARY_MODE (fallback_auto=$CANARY_BOOTSTRAP_FALLBACK_ON_FAIL)"
echo ""

echo "1. Preflight de artefatos..."
bash "$INSTALADOR_ROOT/testes/windows/test-artifact-freshness.sh"
echo ""

echo "2. Bootstrap QGA (canary)..."
QGA_BOOTSTRAP_MODE="$CANARY_BOOTSTRAP_PRIMARY_MODE" \
bash "$AUTON_SCRIPT" bootstrap-qga --vm "$CANARY_VM" --run-id "$RUN_ID" \
  2>&1 | tee "$RUN_DIR/${CANARY_VM}_bootstrap-qga.log"

BOOTSTRAP_STEP="bootstrap_qga_${CANARY_VM}_result"
bootstrap_status="$(latest_step_status "$BOOTSTRAP_STEP")"
bootstrap_mode_used="$CANARY_BOOTSTRAP_PRIMARY_MODE"

if [ "$bootstrap_status" != "PASS" ] && [ "$CANARY_BOOTSTRAP_PRIMARY_MODE" != "auto" ] && [ "$CANARY_BOOTSTRAP_FALLBACK_ON_FAIL" = "1" ]; then
  echo "   WARN: bootstrap em modo '$CANARY_BOOTSTRAP_PRIMARY_MODE' nao passou (status=${bootstrap_status:-SEM_STATUS}). Tentando fallback em modo auto..."
  QGA_BOOTSTRAP_MODE="auto" \
  bash "$AUTON_SCRIPT" bootstrap-qga --vm "$CANARY_VM" --run-id "$RUN_ID" \
    2>&1 | tee -a "$RUN_DIR/${CANARY_VM}_bootstrap-qga.log"
  bootstrap_status="$(latest_step_status "$BOOTSTRAP_STEP")"
  bootstrap_mode_used="auto"
fi

if [ "$bootstrap_status" != "PASS" ]; then
  bootstrap_evidence="$(latest_step_evidence "$BOOTSTRAP_STEP")"
  echo "ERRO: bootstrap-qga canary nao passou para $CANARY_VM (status=${bootstrap_status:-SEM_STATUS})." >&2
  [ -n "$bootstrap_evidence" ] && echo "Evidencia: $bootstrap_evidence" >&2
  [ -f "$RUN_DIR/bootstrap_qga_${CANARY_VM}_qga_wait.log" ] && tail -n 40 "$RUN_DIR/bootstrap_qga_${CANARY_VM}_qga_wait.log" >&2 || true
  exit 1
fi
echo ""

echo "3. Regressao canary com ISO strict..."
QGA_PAYLOAD_TRANSPORT_MODE="iso-strict" \
WINDOWS_REGRESSION_SIGNATURE_PROFILE="$SIGNATURE_PROFILE" \
bash "$AUTON_SCRIPT" run-regressao --vm "$CANARY_VM" --run-id "$RUN_ID" \
  2>&1 | tee "$RUN_DIR/${CANARY_VM}_run-regressao.log"

RUN_ARTIFACT_STEP="run_regressao_${CANARY_VM}_artifact_set"
RUN_RESULT_STEP="run_regressao_${CANARY_VM}_result"
run_artifact_status="$(latest_step_status "$RUN_ARTIFACT_STEP")"
run_result_status="$(latest_step_status "$RUN_RESULT_STEP")"

if [ "$run_artifact_status" != "PASS" ]; then
  artifact_evidence="$(latest_step_evidence "$RUN_ARTIFACT_STEP")"
  echo "ERRO: conjunto minimo de artefatos do canario nao foi gerado (status=${run_artifact_status:-SEM_STATUS})." >&2
  [ -n "$artifact_evidence" ] && echo "Evidencia: $artifact_evidence" >&2
  exit 1
fi
if [ "$run_result_status" != "PASS" ]; then
  result_evidence="$(latest_step_evidence "$RUN_RESULT_STEP")"
  echo "ERRO: regressao canary falhou para $CANARY_VM (status=${run_result_status:-SEM_STATUS})." >&2
  [ -n "$result_evidence" ] && echo "Evidencia: $result_evidence" >&2
  exit 1
fi

TRANSPORT_LOG="$RUN_DIR/run_regressao_${CANARY_VM}_transport_mode.log"
[ -f "$TRANSPORT_LOG" ] || { echo "ERRO: log de transporte ausente: $TRANSPORT_LOG" >&2; exit 1; }
if ! grep -q "^transport_mode=iso$" "$TRANSPORT_LOG"; then
  echo "ERRO: canario nao confirmou transporte ISO puro." >&2
  cat "$TRANSPORT_LOG" >&2
  exit 1
fi
if grep -q "transport_mode=qga" "$TRANSPORT_LOG"; then
  echo "ERRO: fallback QGA detectado no canario (nao permitido)." >&2
  cat "$TRANSPORT_LOG" >&2
  exit 1
fi
echo ""

echo "4. Contract static..."
if bash "$INSTALADOR_ROOT/testes/windows/test-installer-contract-static.sh" \
  >"$RUN_DIR/suite_test_installer_contract_static.log" 2>&1; then
  contract_status="PASS"
  contract_exit="0"
else
  contract_status="FAIL"
  contract_exit="$?"
fi
{
  echo "step,status,exit_code,evidence_file"
  echo "suite_test_installer_contract_static,${contract_status},${contract_exit},$RUN_DIR/suite_test_installer_contract_static.log"
} > "$MANDATORY_CSV"
if [ "$contract_status" != "PASS" ]; then
  echo "ERRO: installer contract static falhou no canario." >&2
  cat "$RUN_DIR/suite_test_installer_contract_static.log" >&2
  exit 1
fi
echo ""

echo "5. Orquestrador de status..."
python3 "$ORCHESTRATOR_PY" \
  --run-id "$RUN_ID" \
  --run-dir "$RUN_DIR" \
  --csv-file "$CSV_FILE" \
  --summary-md "$SUMMARY_MD" \
  --gates-md "$GATES_MD" \
  --mandatory-csv "$MANDATORY_CSV" \
  --vm-order "$CANARY_VM" \
  --cooldown-sec "0" \
  --bootstrap-mode "$bootstrap_mode_used" \
  --manual-fallback-on-auto-fail "$CANARY_BOOTSTRAP_FALLBACK_ON_FAIL" \
  --strict-signature "0" \
  --signature-profile "$SIGNATURE_PROFILE"
echo ""

echo "CANARY PASS"
echo "RUN_DIR: $RUN_DIR"
echo "CSV: $CSV_FILE"
echo "SUMMARY: $SUMMARY_MD"
echo "GATES: $GATES_MD"
