#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
if PROJECT_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null)"; then
  :
else
  PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd -P)"
fi
if [ -d "$PROJECT_ROOT/INSTALADOR" ]; then
  INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
else
  INSTALADOR_ROOT="$PROJECT_ROOT"
fi

AUTON_SCRIPT="$INSTALADOR_ROOT/comum/scripts/windows-autonomous-round.sh"
AUDIT_SCRIPT="$INSTALADOR_ROOT/comum/scripts/audit-host-state.sh"
VM_CONTROL_SCRIPT="$INSTALADOR_ROOT/comum/scripts/windows-vm-control.sh"
BOOTSTRAP_PS1="$INSTALADOR_ROOT/comum/scripts/windows-guest-bootstrap.ps1"
ORCHESTRATOR_PY="$INSTALADOR_ROOT/comum/scripts/windows_e2e_orchestrator.py"

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
VM_ORDER=("win10-lite" "win11-lite")
COOLDOWN_SEC=15
STRICT_SIGNATURE=0
SIGNATURE_PROFILE="technical"
BOOTSTRAP_MODE="manual-ready"
MANUAL_FALLBACK_ON_AUTO_FAIL=1

RUN_DIR=""
CSV_FILE=""
SUMMARY_MD=""
GATES_MD=""
MANDATORY_CSV=""
FINAL_VM_STATUS_LOG=""

usage() {
  cat <<'USAGE'
Uso:
  bash comum/scripts/windows-e2e-sequencial.sh [opcoes]

Opcoes:
  --run-id <id>                 RunId da rodada (default: UTC atual)
  --vm-order <vm1,vm2>          Ordem de VMs (default: win10-lite,win11-lite)
  --cooldown-sec <n>            Espera entre VMs (default: 15)
  --bootstrap-mode <modo>       Modo bootstrap QGA: auto|manual-ready (default: manual-ready)
  --manual-fallback-on-auto-fail
                                Se bootstrap em auto falhar, tenta fallback manual 1x guiado (default: on)
  --no-manual-fallback-on-auto-fail
                                Desliga fallback manual automatico
  --strict-signature            Forca regressao com assinatura estrita
  --signature-profile <perfil>  Perfil de assinatura: technical|production
  -h, --help                    Exibe ajuda
USAGE
}

log() {
  printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

validate_vm() {
  case "$1" in
    win10-lite|win11-lite) ;;
    *)
      echo "ERROR: VM invalida: $1 (use win10-lite ou win11-lite)" >&2
      exit 1
      ;;
  esac
}

parse_args() {
  while [ $# -gt 0 ]; do
    case "$1" in
      --run-id)
        RUN_ID="${2:-}"
        [ -n "$RUN_ID" ] || { echo "ERROR: --run-id requer valor" >&2; exit 1; }
        shift 2
        ;;
      --vm-order)
        [ -n "${2:-}" ] || { echo "ERROR: --vm-order requer valor" >&2; exit 1; }
        IFS=',' read -r -a VM_ORDER <<< "${2:-}"
        shift 2
        ;;
      --cooldown-sec)
        COOLDOWN_SEC="${2:-}"
        [ -n "$COOLDOWN_SEC" ] || { echo "ERROR: --cooldown-sec requer valor" >&2; exit 1; }
        if ! [[ "$COOLDOWN_SEC" =~ ^[0-9]+$ ]]; then
          echo "ERROR: --cooldown-sec deve ser inteiro >= 0" >&2
          exit 1
        fi
        shift 2
        ;;
      --bootstrap-mode)
        BOOTSTRAP_MODE="${2:-}"
        [ -n "$BOOTSTRAP_MODE" ] || { echo "ERROR: --bootstrap-mode requer valor" >&2; exit 1; }
        case "$BOOTSTRAP_MODE" in
          auto|manual-ready) ;;
          *)
            echo "ERROR: --bootstrap-mode invalido: $BOOTSTRAP_MODE (use auto ou manual-ready)" >&2
            exit 1
            ;;
        esac
        shift 2
        ;;
      --strict-signature)
        STRICT_SIGNATURE=1
        SIGNATURE_PROFILE="production"
        shift
        ;;
      --signature-profile)
        SIGNATURE_PROFILE="${2:-}"
        [ -n "$SIGNATURE_PROFILE" ] || { echo "ERROR: --signature-profile requer valor" >&2; exit 1; }
        case "$SIGNATURE_PROFILE" in
          technical|production) ;;
          *)
            echo "ERROR: --signature-profile invalido: $SIGNATURE_PROFILE (use technical ou production)" >&2
            exit 1
            ;;
        esac
        shift 2
        ;;
      --manual-fallback-on-auto-fail)
        MANUAL_FALLBACK_ON_AUTO_FAIL=1
        shift
        ;;
      --no-manual-fallback-on-auto-fail)
        MANUAL_FALLBACK_ON_AUTO_FAIL=0
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "ERROR: opcao desconhecida: $1" >&2
        usage
        exit 1
        ;;
    esac
  done

  [ "${#VM_ORDER[@]}" -ge 1 ] || {
    echo "ERROR: --vm-order vazio" >&2
    exit 1
  }

  local vm
  for vm in "${VM_ORDER[@]}"; do
    validate_vm "$vm"
  done
}

init_paths() {
  RUN_DIR="$INSTALADOR_ROOT/saida/validacao-windows-${RUN_ID}"
  CSV_FILE="$RUN_DIR/resumo.csv"
  SUMMARY_MD="$RUN_DIR/windows-round-summary.md"
  GATES_MD="$RUN_DIR/gates-summary.md"
  MANDATORY_CSV="$RUN_DIR/mandatory-suite.csv"
  FINAL_VM_STATUS_LOG="$RUN_DIR/final_vm_status.log"

  mkdir -p "$RUN_DIR"
  if [ ! -f "$CSV_FILE" ]; then
    echo "step,status,exit_code,evidence_file" > "$CSV_FILE"
  fi
}

record_step() {
  local step="$1"
  local status="$2"
  local exit_code="$3"
  local evidence="$4"
  echo "${step},${status},${exit_code},${evidence}" >> "$CSV_FILE"
}

run_logged_step() {
  local step="$1"
  local log_file="$2"
  shift 2
  if "$@" >"$log_file" 2>&1; then
    record_step "$step" "PASS" "0" "$log_file"
    return 0
  fi

  local rc=$?
  record_step "$step" "FAIL" "$rc" "$log_file"
  return "$rc"
}

run_soft_step() {
  local step="$1"
  local log_file="$2"
  shift 2
  if "$@" >"$log_file" 2>&1; then
    record_step "$step" "PASS" "0" "$log_file"
  else
    local rc=$?
    record_step "$step" "FAIL" "$rc" "$log_file"
  fi
}

extract_step_status() {
  local step="$1"
  awk -F, -v s="$step" '$1==s{val=$2} END{print val}' "$CSV_FILE"
}

ensure_vm_stopped() {
  local vm="$1"
  local log_file="$2"
  local stop_timeout_sec="${VM_STOP_TIMEOUT_SEC:-180}"
  local state=""

  if command -v timeout >/dev/null 2>&1; then
    if timeout "$stop_timeout_sec" bash "$VM_CONTROL_SCRIPT" stop "$vm" >"$log_file" 2>&1; then
      return 0
    fi
  else
    if bash "$VM_CONTROL_SCRIPT" stop "$vm" >"$log_file" 2>&1; then
      return 0
    fi
  fi

  # Fallback hard stop if helper script fails or hangs.
  {
    echo "--- fallback: virsh destroy/shutdown ---"
    if command -v timeout >/dev/null 2>&1; then
      sg libvirt -c "timeout 15 virsh destroy $vm >/dev/null 2>&1 || true"
      sg libvirt -c "timeout 15 virsh shutdown $vm >/dev/null 2>&1 || true"
    else
      sg libvirt -c "virsh destroy $vm >/dev/null 2>&1 || true"
      sg libvirt -c "virsh shutdown $vm >/dev/null 2>&1 || true"
    fi
    state="$(sg libvirt -c "virsh domstate $vm" 2>/dev/null | tr -d '\r' || true)"
    echo "fallback_domstate=$state"
  } >>"$log_file" 2>&1

  [ "$state" = "shut off" ]
}

sanitize_cdrom_source() {
  local vm="$1"
  local log_file="$2"
  local domstate=""
  local cdrom_map=""

  domstate="$(sg libvirt -c "virsh domstate $vm" 2>/dev/null | tr -d '\r' || true)"

  if ! cdrom_map="$(sg libvirt -c "virsh dumpxml $vm" | python3 -c '
import sys
import xml.etree.ElementTree as ET

data = sys.stdin.read()
root = ET.fromstring(data)
for disk in root.findall("./devices/disk"):
    if disk.get("device") != "cdrom":
        continue
    target = disk.find("target")
    source = disk.find("source")
    dev = target.get("dev") if target is not None else ""
    path = source.get("file") if source is not None else ""
    if dev:
        print(f"{dev}|{path}")
')"; then
    {
      echo "vm=$vm"
      echo "domstate=$domstate"
      echo "sanitize_cdrom_parse=FAIL"
    } >"$log_file" 2>&1
    return 1
  fi

  {
    echo "vm=$vm"
    echo "domstate=$domstate"
    echo "sanitize_cdrom_parse=PASS"

    if [ -z "$cdrom_map" ]; then
      echo "cdrom_entries=0"
    else
      while IFS='|' read -r dev source_file; do
        [ -n "$dev" ] || continue
        echo "cdrom_dev=$dev source_file=${source_file:-<empty>}"
        if [ -z "${source_file:-}" ]; then
          echo "action=none reason=no_source_file"
          continue
        fi
        if [ -e "$source_file" ]; then
          echo "action=none reason=source_exists"
          continue
        fi

        echo "action=eject reason=missing_source_file"
        if [ "$domstate" = "running" ]; then
          sg libvirt -c "virsh change-media $vm $dev --eject --live --config --force"
        else
          sg libvirt -c "virsh change-media $vm $dev --eject --config --force"
        fi
      done <<< "$cdrom_map"
    fi

    echo "--- cdrom_after_sanitize ---"
    sg libvirt -c "virsh dumpxml $vm | rg -n '<disk |device=.cdrom.|source file=|target dev='"
  } >"$log_file" 2>&1
}

write_manual_actions_file() {
  local vm="$1"
  local manual_file="$2"
  cat >"$manual_file" <<EOF
# Manual 1x necessario para ${vm}

1. Inicie/abra a VM:
   - \`bash comum/scripts/windows-vm-control.sh start ${vm}\`
   - \`virt-viewer -c qemu:///system ${vm}\`

2. Dentro da VM:
   - Abra \`Este Computador\`
   - Abra \`Unidade de CD (GUESTTOOLS)\`
   - Execute o aplicativo \`A\` como administrador

3. Valide servico:
   - Abra \`services.msc\`
   - Serviço \`qemu-ga\` em \`Automatico\` e \`Em execucao\`

4. Validacao no host:
   - \`sg libvirt -c "virsh qemu-agent-command ${vm} '{\\"execute\\":\\"guest-ping\\"}'"\`

5. Retomar rodada:
   - reexecutar bootstrap em manual-ready para ${vm}
EOF
}

baseline_capture() {
  run_soft_step "baseline_audit_host_state" "$RUN_DIR/baseline-audit-host-state.log" \
    bash "$AUDIT_SCRIPT"
  run_soft_step "baseline_vm_status" "$RUN_DIR/baseline-vm-status.log" \
    bash "$VM_CONTROL_SCRIPT" status
  run_soft_step "baseline_dumpxml_win10" "$RUN_DIR/win10.dumpxml" \
    sg libvirt -c "virsh dumpxml win10-lite"
  run_soft_step "baseline_dumpxml_win11" "$RUN_DIR/win11.dumpxml" \
    sg libvirt -c "virsh dumpxml win11-lite"
  run_soft_step "baseline_artifacts_sha256" "$RUN_DIR/baseline-artifacts.sha256" \
    sha256sum "$INSTALADOR_ROOT"/saida/windows/Protons-*-x64.msi "$INSTALADOR_ROOT"/saida/windows/ProtonsSetup-*.exe
}

prepare_guest_tools_iso() {
  local src_dir="$RUN_DIR/guest-tools-src"
  local iso_path="$RUN_DIR/guest-tools-auto.iso"
  local listing_file="$RUN_DIR/guest-tools-auto.iso.contents.log"
  local iso_log="$RUN_DIR/prepare_guest_tools_iso.log"
  local shared_iso="$INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso"
  local installer_copy=""

  mkdir -p "$src_dir"
  cp "$BOOTSTRAP_PS1" "$src_dir/windows-guest-bootstrap.ps1"

  if [ -f "$INSTALADOR_ROOT/saida/guest-agent-bootstrap/src/a.exe" ]; then
    cp "$INSTALADOR_ROOT/saida/guest-agent-bootstrap/src/a.exe" "$src_dir/a.exe"
    installer_copy="$src_dir/a.exe"
  fi

  {
    echo "src_dir=$src_dir"
    echo "iso_path=$iso_path"
    echo "shared_iso=$shared_iso"
    echo "bootstrap_ps1=$BOOTSTRAP_PS1"
    if [ -n "$installer_copy" ]; then
      echo "installer_copy=$installer_copy"
    else
      echo "installer_copy=not_found"
    fi

    xorriso -as mkisofs -o "$iso_path" -V GUESTTOOLS "$src_dir"
    test -s "$iso_path"
    xorriso -indev "$iso_path" -find / -type f -exec lsdl > "$listing_file"
    cp -f "$iso_path" "$shared_iso"
    ls -lh "$iso_path" "$shared_iso"
  } >"$iso_log" 2>&1

  record_step "prepare_guest_tools_iso" "PASS" "0" "$iso_log"
}

run_vm_sequence() {
  local vm="$1"
  local vm_boot_log="$RUN_DIR/${vm}_bootstrap-qga.log"
  local vm_boot_retry_log="$RUN_DIR/${vm}_bootstrap-qga-manual-ready.log"
  local vm_reg_log="$RUN_DIR/${vm}_run-regressao.log"
  local vm_stop_log="$RUN_DIR/${vm}_stop.log"
  local vm_sanitize_log="$RUN_DIR/${vm}_sanitize_cdrom.log"
  local manual_actions_file="$RUN_DIR/manual-actions-${vm}.md"
  local manual_prompt_log="$RUN_DIR/${vm}_manual_fallback.log"
  local vm_result_status=""
  local bootstrap_cmd_rc=0
  local bootstrap_status=""

  # Keep host load low: stop every other VM before starting the target.
  local other
  for other in win10-lite win11-lite; do
    if [ "$other" != "$vm" ]; then
      run_soft_step "${vm}_ensure_other_vm_stopped_${other}" "$RUN_DIR/${vm}_ensure_stop_${other}.log" \
        ensure_vm_stopped "$other" "$RUN_DIR/${vm}_ensure_stop_${other}.raw.log"
    fi
  done

  run_soft_step "${vm}_sanitize_cdrom" "$vm_sanitize_log" \
    sanitize_cdrom_source "$vm" "$RUN_DIR/${vm}_sanitize_cdrom.raw.log"

  if QGA_BOOTSTRAP_MODE="$BOOTSTRAP_MODE" bash "$AUTON_SCRIPT" bootstrap-qga --vm "$vm" --run-id "$RUN_ID" >"$vm_boot_log" 2>&1; then
    record_step "${vm}_bootstrap_qga_cmd" "PASS" "0" "$vm_boot_log"
  else
    bootstrap_cmd_rc=$?
    record_step "${vm}_bootstrap_qga_cmd" "FAIL" "$bootstrap_cmd_rc" "$vm_boot_log"
  fi

  bootstrap_status="$(extract_step_status "bootstrap_qga_${vm}_result")"
  if [ -z "$bootstrap_status" ]; then
    bootstrap_status="SEM_RESULTADO"
  fi
  record_step "${vm}_bootstrap_qga_status" "$bootstrap_status" "0" "$CSV_FILE"

  if [ "$BOOTSTRAP_MODE" = "auto" ] && [ "$MANUAL_FALLBACK_ON_AUTO_FAIL" -eq 1 ] && [ "$bootstrap_status" != "PASS" ]; then
    write_manual_actions_file "$vm" "$manual_actions_file"
    record_step "${vm}_manual_fallback_actions" "PARCIAL" "0" "$manual_actions_file"
    {
      echo "bootstrap_status_before_fallback=$bootstrap_status"
      echo "bootstrap_cmd_rc=$bootstrap_cmd_rc"
      if [ -t 0 ] && [ -t 1 ]; then
        echo "interactive_prompt=enabled"
        echo "Realize o Manual 1x para $vm e pressione ENTER para continuar."
        read -r _
      else
        echo "interactive_prompt=disabled (non-interactive shell)"
      fi
    } >"$manual_prompt_log" 2>&1 || true

    if QGA_BOOTSTRAP_MODE="manual-ready" bash "$AUTON_SCRIPT" bootstrap-qga --vm "$vm" --run-id "$RUN_ID" >"$vm_boot_retry_log" 2>&1; then
      record_step "${vm}_bootstrap_qga_cmd_manual_ready" "PASS" "0" "$vm_boot_retry_log"
    else
      local retry_rc=$?
      record_step "${vm}_bootstrap_qga_cmd_manual_ready" "FAIL" "$retry_rc" "$vm_boot_retry_log"
    fi

    bootstrap_status="$(extract_step_status "bootstrap_qga_${vm}_result")"
    if [ -z "$bootstrap_status" ]; then
      bootstrap_status="SEM_RESULTADO"
    fi
    record_step "${vm}_bootstrap_qga_status_after_fallback" "$bootstrap_status" "0" "$CSV_FILE"

    if [ "$bootstrap_status" = "PASS" ]; then
      record_step "${vm}_manual_fallback_wait" "PASS" "0" "$manual_prompt_log"
    else
      record_step "${vm}_manual_fallback_wait" "BLOQUEADO" "1" "$manual_actions_file"
      if ensure_vm_stopped "$vm" "$vm_stop_log"; then
        record_step "${vm}_stop" "PASS" "0" "$vm_stop_log"
      else
        record_step "${vm}_stop" "FAIL" "1" "$vm_stop_log"
      fi
      record_step "${vm}_result_snapshot" "BLOQUEADO" "1" "$manual_actions_file"
      return 2
    fi
  fi

  bootstrap_status="$(extract_step_status "bootstrap_qga_${vm}_result")"
  if [ "$bootstrap_status" = "PASS" ]; then
    if WINDOWS_REGRESSION_STRICT_SIGNATURE="$STRICT_SIGNATURE" \
        WINDOWS_REGRESSION_SIGNATURE_PROFILE="$SIGNATURE_PROFILE" \
        bash "$AUTON_SCRIPT" run-regressao --vm "$vm" --run-id "$RUN_ID" >"$vm_reg_log" 2>&1; then
      record_step "${vm}_run_regressao_cmd" "PASS" "0" "$vm_reg_log"
    else
      local rc=$?
      record_step "${vm}_run_regressao_cmd" "FAIL" "$rc" "$vm_reg_log"
    fi
  else
    {
      echo "skip_run_regressao=1"
      echo "reason=bootstrap_qga_not_passed"
      echo "bootstrap_status=$bootstrap_status"
    } >"$vm_reg_log"
    record_step "${vm}_run_regressao_cmd" "BLOQUEADO" "1" "$vm_reg_log"
  fi

  if ensure_vm_stopped "$vm" "$vm_stop_log"; then
    record_step "${vm}_stop" "PASS" "0" "$vm_stop_log"
  else
    record_step "${vm}_stop" "FAIL" "1" "$vm_stop_log"
  fi

  vm_result_status="$(extract_step_status "run_regressao_${vm}_result")"
  if [ -z "$vm_result_status" ]; then
    vm_result_status="SEM_RESULTADO"
  fi
  record_step "${vm}_result_snapshot" "$vm_result_status" "0" "$CSV_FILE"
}

run_mandatory_suite() {
  local test_boot_id="${RUN_ID}-TBOOT"
  local test_reg_id="${RUN_ID}-TREG"
  local manifest_cmd=(
    bash "$INSTALADOR_ROOT/comum/scripts/validate-update-manifest.sh"
    --manifest "$INSTALADOR_ROOT/saida/update/update-manifest.json"
  )
  if [ "$SIGNATURE_PROFILE" = "production" ]; then
    manifest_cmd+=(--strict-signature)
  fi

  echo "step,status,exit_code,evidence_file" > "$MANDATORY_CSV"

  run_suite_step() {
    local step="$1"
    local log_file="$RUN_DIR/${step}.log"
    shift
    local rc=0
    set +e
    "$@" >"$log_file" 2>&1
    rc=$?
    set -e
    if [ "$rc" -eq 0 ]; then
      echo "${step},PASS,0,${log_file}" >> "$MANDATORY_CSV"
      return 0
    fi
    echo "${step},FAIL,${rc},${log_file}" >> "$MANDATORY_CSV"
    return 1
  }

  run_suite_step "suite_test_qga_bootstrap_autonomo" \
    bash "$INSTALADOR_ROOT/testes/windows/test-qga-bootstrap-autonomo.sh" "$test_boot_id" || true
  run_suite_step "suite_test_regressao_autonoma" \
    bash "$INSTALADOR_ROOT/testes/windows/test-regressao-autonoma.sh" "$test_reg_id" || true
  run_suite_step "suite_test_installer_contract_static" \
    bash "$INSTALADOR_ROOT/testes/windows/test-installer-contract-static.sh" || true
  run_suite_step "suite_validate_update_manifest_strict" \
    "${manifest_cmd[@]}" || true
  run_suite_step "suite_test_update_manifest" \
    bash "$INSTALADOR_ROOT/comum/scripts/test-update-manifest.sh" || true
  run_suite_step "suite_test_checklist_graficos" \
    bash "$INSTALADOR_ROOT/testes/meta/test-checklist-graficos.sh" || true
  run_suite_step "suite_test_checklist_score_evidence" \
    bash "$INSTALADOR_ROOT/testes/meta/test-checklist-score-evidence.sh" || true
  run_suite_step "suite_audit_host_state" \
    bash "$AUDIT_SCRIPT" || true
}

write_round_summary() {
  local vm_order_csv
  vm_order_csv="$(IFS=,; echo "${VM_ORDER[*]}")"
  python3 "$ORCHESTRATOR_PY" \
    --run-id "$RUN_ID" \
    --run-dir "$RUN_DIR" \
    --csv-file "$CSV_FILE" \
    --summary-md "$SUMMARY_MD" \
    --gates-md "$GATES_MD" \
    --mandatory-csv "$MANDATORY_CSV" \
    --vm-order "$vm_order_csv" \
    --cooldown-sec "$COOLDOWN_SEC" \
    --bootstrap-mode "$BOOTSTRAP_MODE" \
    --manual-fallback-on-auto-fail "$MANUAL_FALLBACK_ON_AUTO_FAIL" \
    --strict-signature "$STRICT_SIGNATURE" \
    --signature-profile "$SIGNATURE_PROFILE"
}

main() {
  parse_args "$@"

  require_cmd bash
  require_cmd sg
  require_cmd virsh
  require_cmd rg
  require_cmd awk
  require_cmd python3
  require_cmd xorriso
  require_cmd zip

  [ -x "$AUTON_SCRIPT" ] || { echo "ERROR: script ausente: $AUTON_SCRIPT" >&2; exit 1; }
  [ -x "$AUDIT_SCRIPT" ] || { echo "ERROR: script ausente: $AUDIT_SCRIPT" >&2; exit 1; }
  [ -x "$VM_CONTROL_SCRIPT" ] || { echo "ERROR: script ausente: $VM_CONTROL_SCRIPT" >&2; exit 1; }
  [ -f "$BOOTSTRAP_PS1" ] || { echo "ERROR: script ausente: $BOOTSTRAP_PS1" >&2; exit 1; }
  [ -f "$ORCHESTRATOR_PY" ] || { echo "ERROR: script ausente: $ORCHESTRATOR_PY" >&2; exit 1; }

  init_paths
  log "Inicio rodada sequencial: RUN_ID=$RUN_ID"

  baseline_capture
  prepare_guest_tools_iso

  local idx=0
  local vm=""
  local sequence_blocked=0
  for vm in "${VM_ORDER[@]}"; do
    idx=$((idx + 1))
    log "Executando VM ${idx}/${#VM_ORDER[@]}: $vm"
    if run_vm_sequence "$vm"; then
      :
    else
      rc=$?
      if [ "$rc" -eq 2 ]; then
        sequence_blocked=1
        record_step "sequence_blocked_manual_fallback_${vm}" "BLOQUEADO" "$rc" "$RUN_DIR/manual-actions-${vm}.md"
        log "Sequencia pausada: manual fallback pendente para $vm."
        break
      fi
      record_step "sequence_vm_error_${vm}" "FAIL" "$rc" "$RUN_DIR/${vm}_bootstrap-qga.log"
      log "Falha na sequencia para $vm (rc=$rc)."
      break
    fi
    if [ "$idx" -lt "${#VM_ORDER[@]}" ] && [ "$COOLDOWN_SEC" -gt 0 ]; then
      log "Cooldown de ${COOLDOWN_SEC}s antes da proxima VM."
      sleep "$COOLDOWN_SEC"
    fi
  done

  if [ "$sequence_blocked" -eq 0 ]; then
    run_mandatory_suite
  else
    {
      echo "step,status,exit_code,evidence_file"
      echo "suite_skipped_due_manual_fallback,BLOQUEADO,2,$RUN_DIR/manual-actions-${vm}.md"
    } > "$MANDATORY_CSV"
  fi

  run_soft_step "final_stop_win10-lite" "$RUN_DIR/final_stop_win10-lite.log" \
    ensure_vm_stopped "win10-lite" "$RUN_DIR/final_stop_win10-lite.raw.log"
  run_soft_step "final_stop_win11-lite" "$RUN_DIR/final_stop_win11-lite.log" \
    ensure_vm_stopped "win11-lite" "$RUN_DIR/final_stop_win11-lite.raw.log"

  bash "$VM_CONTROL_SCRIPT" status > "$FINAL_VM_STATUS_LOG" 2>&1 || true
  state_win10="$(sg libvirt -c "virsh domstate win10-lite" 2>/dev/null | tr -d '\r' || true)"
  state_win11="$(sg libvirt -c "virsh domstate win11-lite" 2>/dev/null | tr -d '\r' || true)"
  {
    echo
    echo "state_win10=$state_win10"
    echo "state_win11=$state_win11"
  } >> "$FINAL_VM_STATUS_LOG"
  if [ "$state_win10" = "shut off" ] && [ "$state_win11" = "shut off" ]; then
    record_step "final_vm_status" "PASS" "0" "$FINAL_VM_STATUS_LOG"
  else
    record_step "final_vm_status" "FAIL" "1" "$FINAL_VM_STATUS_LOG"
  fi

  write_round_summary

  log "Rodada finalizada."
  log "RUN_DIR=$RUN_DIR"
  log "CSV=$CSV_FILE"
  log "SUMMARY=$SUMMARY_MD"
  log "GATES=$GATES_MD"
  log "MANDATORY=$MANDATORY_CSV"
}

main "$@"
