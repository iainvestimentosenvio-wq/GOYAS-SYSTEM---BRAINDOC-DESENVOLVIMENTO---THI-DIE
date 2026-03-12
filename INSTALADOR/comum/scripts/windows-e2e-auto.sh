#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

AUTON_SCRIPT="$INSTALADOR_ROOT/comum/scripts/windows-autonomous-round.sh"
BOOTSTRAP_PS1="$INSTALADOR_ROOT/comum/scripts/windows-guest-bootstrap.ps1"

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
ARTIFACTS_DIR="$INSTALADOR_ROOT/saida/windows"
VMS=("win10-lite" "win11-lite")

RUN_DIR=""
CSV_FILE=""
SUMMARY_JSON=""
SUMMARY_MD=""

usage() {
  cat <<'USAGE'
Uso:
  bash comum/scripts/windows-e2e-auto.sh [opcoes]

Opcoes:
  --run-id <id>         Define RunId fixo (default: UTC atual)
  --vm <nome>           VM alvo (repetivel). Default: win10-lite e win11-lite
  --artifacts-dir <dir> Diretorio dos artefatos Windows (default: saida/windows)
  -h, --help            Exibe ajuda

Exemplo:
  bash comum/scripts/windows-e2e-auto.sh --vm win10-lite --vm win11-lite --artifacts-dir saida/windows
USAGE
}

log() {
  printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: comando obrigatorio ausente: $1" >&2
    exit 1
  }
}

parse_args() {
  local custom_vms=()
  while [ $# -gt 0 ]; do
    case "$1" in
      --run-id)
        RUN_ID="${2:-}"
        [ -n "$RUN_ID" ] || { echo "ERROR: --run-id requer valor" >&2; exit 1; }
        shift 2
        ;;
      --vm)
        custom_vms+=("${2:-}")
        [ -n "${2:-}" ] || { echo "ERROR: --vm requer valor" >&2; exit 1; }
        shift 2
        ;;
      --artifacts-dir)
        ARTIFACTS_DIR="${2:-}"
        [ -n "$ARTIFACTS_DIR" ] || { echo "ERROR: --artifacts-dir requer valor" >&2; exit 1; }
        shift 2
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

  if [ ${#custom_vms[@]} -gt 0 ]; then
    VMS=("${custom_vms[@]}")
  fi

  local vm
  for vm in "${VMS[@]}"; do
    case "$vm" in
      win10-lite|win11-lite) ;;
      *)
        echo "ERROR: VM invalida: $vm (use win10-lite ou win11-lite)" >&2
        exit 1
        ;;
    esac
  done

  if [[ "$ARTIFACTS_DIR" != /* ]]; then
    ARTIFACTS_DIR="$INSTALADOR_ROOT/$ARTIFACTS_DIR"
  fi
}

setup_paths() {
  RUN_DIR="$INSTALADOR_ROOT/saida/validacao-windows-${RUN_ID}"
  CSV_FILE="$RUN_DIR/resumo.csv"
  SUMMARY_JSON="$RUN_DIR/summary.json"
  SUMMARY_MD="$RUN_DIR/windows-round-summary.md"

  mkdir -p "$RUN_DIR"
  if [ ! -f "$CSV_FILE" ]; then
    echo "step,status,exit_code,evidence_file" > "$CSV_FILE"
  fi
}

record_step() {
  local step="$1"
  local status="$2"
  local exit_code="$3"
  local evidence_file="$4"
  echo "${step},${status},${exit_code},${evidence_file}" >> "$CSV_FILE"
}

run_capture() {
  local step="$1"
  local cmd="$2"
  local log_file="$RUN_DIR/${step}.log"

  if bash -lc "$cmd" >"$log_file" 2>&1; then
    record_step "$step" "PASS" "0" "$log_file"
    return 0
  else
    local rc=$?
    record_step "$step" "FAIL" "$rc" "$log_file"
    return "$rc"
  fi
}

ensure_qga_channel() {
  local vm="$1"
  local state_log="$RUN_DIR/${vm}_qga_channel.log"
  local xml_file="$RUN_DIR/${vm}-qga-channel.xml"

  if sg libvirt -c "virsh dumpxml $vm" | rg -q "org.qemu.guest_agent.0"; then
    printf 'QGA channel ja presente no XML da VM %s\n' "$vm" > "$state_log"
    record_step "${vm}_qga_channel" "PASS" "0" "$state_log"
    return 0
  fi

  {
    printf 'QGA channel ausente. Aplicando canal persistente em %s\n' "$vm"
    cat > "$xml_file" <<'XML'
<channel type='unix'>
  <source mode='bind'/>
  <target type='virtio' name='org.qemu.guest_agent.0'/>
</channel>
XML

    local state
    state="$(sg libvirt -c "virsh domstate $vm" | tr -d '\r')"
    printf 'Estado atual da VM: %s\n' "$state"

    if [ "$state" = "running" ]; then
      sg libvirt -c "virsh shutdown $vm" || true
      sleep 8
      state="$(sg libvirt -c "virsh domstate $vm" | tr -d '\r')"
      if [ "$state" != "shut off" ]; then
        sg libvirt -c "virsh destroy $vm" || true
      fi
    fi

    sg libvirt -c "virsh attach-device $vm '$xml_file' --config"
    sg libvirt -c "virsh dumpxml $vm" | rg -n "channel|org.qemu.guest_agent.0"
  } > "$state_log" 2>&1 || {
    record_step "${vm}_qga_channel" "FAIL" "1" "$state_log"
    return 1
  }

  if sg libvirt -c "virsh dumpxml $vm" | rg -q "org.qemu.guest_agent.0"; then
    record_step "${vm}_qga_channel" "PASS" "0" "$state_log"
    return 0
  fi

  record_step "${vm}_qga_channel" "FAIL" "1" "$state_log"
  return 1
}

prepare_guest_tools_iso() {
  local iso_log="$RUN_DIR/prepare_guest_tools_iso.log"
  local src_dir="$RUN_DIR/guest-tools-src"
  local iso_path="$RUN_DIR/guest-tools-auto.iso"
  local shared_iso_path="$INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso"
  local installer_src=""

  mkdir -p "$src_dir"
  cp "$BOOTSTRAP_PS1" "$src_dir/windows-guest-bootstrap.ps1"

  if [ -f "$INSTALADOR_ROOT/saida/guest-agent-bootstrap/src/a.exe" ]; then
    installer_src="$INSTALADOR_ROOT/saida/guest-agent-bootstrap/src/a.exe"
    cp "$installer_src" "$src_dir/a.exe"
  elif [ -f "/usr/share/virtio-win/qemu-ga/qemu-ga-x86_64.msi" ]; then
    installer_src="/usr/share/virtio-win/qemu-ga/qemu-ga-x86_64.msi"
    cp "$installer_src" "$src_dir/qemu-ga-x86_64.msi"
  fi

  {
    printf 'ISO destino: %s\n' "$iso_path"
    printf 'ISO fallback compartilhada: %s\n' "$shared_iso_path"
    printf 'Bootstrap script: %s\n' "$BOOTSTRAP_PS1"
    if [ -n "$installer_src" ]; then
      printf 'Installer incluido: %s\n' "$installer_src"
    else
      printf 'Installer nao encontrado no host; somente script de bootstrap sera empacotado.\n'
    fi
    if xorriso -as mkisofs -o "$iso_path" -V GUESTTOOLS "$src_dir"; then
      ls -lh "$iso_path"
      mkdir -p "$(dirname "$shared_iso_path")"
      cp "$iso_path" "$shared_iso_path"
      printf 'ISO copiada para fallback compartilhado.\n'
      record_step "prepare_guest_tools_iso" "PASS" "0" "$iso_log"
    else
      rc="$?"
      printf 'Falha ao gerar ISO com xorriso (rc=%s).\n' "$rc"
      if [ -f "$shared_iso_path" ]; then
        printf 'Fallback existente sera usado: %s\n' "$shared_iso_path"
        record_step "prepare_guest_tools_iso" "PARCIAL" "$rc" "$iso_log"
      else
        printf 'Sem fallback de ISO disponivel.\n'
        record_step "prepare_guest_tools_iso" "FAIL" "$rc" "$iso_log"
      fi
    fi
  } > "$iso_log" 2>&1
}

baseline_capture() {
  run_capture "baseline_virsh_list_all" "sg libvirt -c 'virsh list --all'" || true
  run_capture "baseline_snapshot_win10" "sg libvirt -c 'virsh snapshot-list win10-lite'" || true
  run_capture "baseline_snapshot_win11" "sg libvirt -c 'virsh snapshot-list win11-lite'" || true
  run_capture "baseline_dominfo_win10" "sg libvirt -c 'virsh dominfo win10-lite'" || true
  run_capture "baseline_dominfo_win11" "sg libvirt -c 'virsh dominfo win11-lite'" || true
  run_capture "baseline_domxml_win10_core" "sg libvirt -c \"virsh dumpxml win10-lite | rg -n 'loader|nvram|tpm|channel|memory|vcpu|disk|interface'\"" || true
  run_capture "baseline_domxml_win11_core" "sg libvirt -c \"virsh dumpxml win11-lite | rg -n 'loader|nvram|tpm|channel|memory|vcpu|disk|interface'\"" || true
  run_capture "baseline_hash_artifacts" "sha256sum '$ARTIFACTS_DIR'/Protons-*-x64.msi '$ARTIFACTS_DIR'/ProtonsSetup-*.exe" || true
  run_capture "baseline_manifest" "cat '$INSTALADOR_ROOT/saida/update/update-manifest.json'" || true
}

run_vm_round() {
  local vm="$1"
  local bootstrap_log="$RUN_DIR/${vm}_bootstrap_cmd.log"
  local regressao_log="$RUN_DIR/${vm}_run_regressao_cmd.log"
  local stop_log="$RUN_DIR/${vm}_stop_final.log"
  local bootstrap_attempts="${QGA_BOOTSTRAP_MAX_ATTEMPTS:-1}"
  local bootstrap_post_keys_sleep="${QGA_BOOTSTRAP_POST_KEYS_SLEEP_SEC:-2}"
  local bootstrap_ping_timeout="${QGA_BOOTSTRAP_PING_TIMEOUT_SEC:-8}"
  local bootstrap_reboot_sleep="${QGA_BOOTSTRAP_REBOOT_SLEEP_SEC:-3}"

  ensure_qga_channel "$vm" || true

  if QGA_BOOTSTRAP_MAX_ATTEMPTS="$bootstrap_attempts" \
     QGA_BOOTSTRAP_POST_KEYS_SLEEP_SEC="$bootstrap_post_keys_sleep" \
     QGA_BOOTSTRAP_PING_TIMEOUT_SEC="$bootstrap_ping_timeout" \
     QGA_BOOTSTRAP_REBOOT_SLEEP_SEC="$bootstrap_reboot_sleep" \
     bash "$AUTON_SCRIPT" bootstrap-qga --vm "$vm" --run-id "$RUN_ID" >"$bootstrap_log" 2>&1; then
    record_step "${vm}_bootstrap_qga_cmd" "PASS" "0" "$bootstrap_log"
  else
    local rc=$?
    record_step "${vm}_bootstrap_qga_cmd" "FAIL" "$rc" "$bootstrap_log"
  fi

  if bash "$AUTON_SCRIPT" run-regressao --vm "$vm" --run-id "$RUN_ID" >"$regressao_log" 2>&1; then
    record_step "${vm}_run_regressao_cmd" "PASS" "0" "$regressao_log"
  else
    local rc=$?
    record_step "${vm}_run_regressao_cmd" "FAIL" "$rc" "$regressao_log"
  fi

  if sg libvirt -c "virsh shutdown $vm" >"$stop_log" 2>&1; then
    record_step "${vm}_shutdown" "PASS" "0" "$stop_log"
  else
    local rc=$?
    sg libvirt -c "virsh destroy $vm" >>"$stop_log" 2>&1 || true
    record_step "${vm}_shutdown" "PARCIAL" "$rc" "$stop_log"
  fi
}

write_summary_outputs() {
  local pass fail parcial bloqueado total
  local gate_status gate_reason

  pass="$(awk -F, 'NR>1 && $2=="PASS"{c++} END{print c+0}' "$CSV_FILE")"
  fail="$(awk -F, 'NR>1 && $2=="FAIL"{c++} END{print c+0}' "$CSV_FILE")"
  parcial="$(awk -F, 'NR>1 && $2=="PARCIAL"{c++} END{print c+0}' "$CSV_FILE")"
  bloqueado="$(awk -F, 'NR>1 && $2=="BLOQUEADO"{c++} END{print c+0}' "$CSV_FILE")"
  total="$(awk -F, 'NR>1{c++} END{print c+0}' "$CSV_FILE")"

  if [ "$fail" -eq 0 ] && [ "$bloqueado" -eq 0 ]; then
    gate_status="GO_TECNICO_WINDOWS"
    gate_reason="zero fail/bloqueado na rodada automatica"
  else
    gate_status="NO-GO"
    gate_reason="existem fail ou bloqueios tecnicos na rodada automatica"
  fi

  python3 - "$RUN_ID" "$RUN_DIR" "$CSV_FILE" "$pass" "$fail" "$parcial" "$bloqueado" "$total" "$gate_status" "$gate_reason" "${VMS[*]}" > "$SUMMARY_JSON" <<'PY'
import csv
import json
import sys
from datetime import datetime, timezone

run_id, run_dir, csv_file, p, f, parc, bloq, total, gate, reason, vms = sys.argv[1:]
rows = []
with open(csv_file, newline='', encoding='utf-8') as fh:
    reader = csv.DictReader(fh)
    rows.extend(reader)

obj = {
    "run_id": run_id,
    "timestamp_utc": datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
    "run_dir": run_dir,
    "csv": csv_file,
    "vms": [v for v in vms.split(' ') if v],
    "totals": {
        "PASS": int(p),
        "FAIL": int(f),
        "PARCIAL": int(parc),
        "BLOQUEADO": int(bloq),
        "TOTAL": int(total),
    },
    "gate": {
        "status": gate,
        "reason": reason,
    },
    "steps": rows,
}
print(json.dumps(obj, ensure_ascii=True, indent=2))
PY

  {
    echo "# Rodada Windows Automatizada (Win10 + Win11)"
    echo
    printf -- '- RunId: `%s`\n' "$RUN_ID"
    printf -- '- TimestampUTC: `%s`\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf -- '- VMs: `%s`\n' "${VMS[*]}"
    printf -- '- CSV: `%s`\n' "$CSV_FILE"
    printf -- '- JSON: `%s`\n' "$SUMMARY_JSON"
    echo
    echo "## Totais"
    echo "- PASS: $pass"
    echo "- FAIL: $fail"
    echo "- PARCIAL: $parcial"
    echo "- BLOQUEADO: $bloqueado"
    echo "- TOTAL: $total"
    printf -- '- Gate: `%s`\n' "$gate_status"
    echo "- Motivo: $gate_reason"
    echo
    echo "## Resultado por passo"
    echo "| Step | Status | ExitCode | Evidencia |"
    echo "| --- | --- | --- | --- |"
    awk -F, 'NR>1{printf "| %s | %s | %s | `%s` |\n", $1, $2, $3, $4}' "$CSV_FILE"
  } > "$SUMMARY_MD"
}

main() {
  parse_args "$@"

  require_cmd sg
  require_cmd virsh
  require_cmd rg
  require_cmd python3
  require_cmd xorriso
  require_cmd zip

  [ -x "$AUTON_SCRIPT" ] || {
    echo "ERROR: script ausente/sem execucao: $AUTON_SCRIPT" >&2
    exit 1
  }
  [ -f "$BOOTSTRAP_PS1" ] || {
    echo "ERROR: bootstrap ausente: $BOOTSTRAP_PS1" >&2
    exit 1
  }
  [ -d "$ARTIFACTS_DIR" ] || {
    echo "ERROR: artifacts-dir inexistente: $ARTIFACTS_DIR" >&2
    exit 1
  }

  setup_paths
  log "Inicio da rodada automatica Windows."
  log "RUN_ID=$RUN_ID"
  log "RUN_DIR=$RUN_DIR"

  baseline_capture
  prepare_guest_tools_iso

  local vm
  for vm in "${VMS[@]}"; do
    log "Rodando VM: $vm"
    run_vm_round "$vm"
  done

  write_summary_outputs

  log "Rodada concluida."
  log "Resumo CSV: $CSV_FILE"
  log "Resumo JSON: $SUMMARY_JSON"
  log "Resumo MD: $SUMMARY_MD"
}

main "$@"
