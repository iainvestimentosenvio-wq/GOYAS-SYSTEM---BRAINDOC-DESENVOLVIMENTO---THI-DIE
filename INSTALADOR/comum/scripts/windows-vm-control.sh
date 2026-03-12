#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

WIN10_VM="win10-lite"
WIN11_VM="win11-lite"

usage() {
  cat <<'EOF'
Uso:
  bash comum/scripts/windows-vm-control.sh <comando> [vm]

Comandos:
  status
      Mostra estado das VMs e snapshots.

  start <win10-lite|win11-lite>
      Inicia a VM (se necessario) e mostra URI SPICE.

  stop <win10-lite|win11-lite>
      Faz shutdown gracioso (com fallback destroy).

  display <win10-lite|win11-lite>
      Mostra URI de display (SPICE) da VM.

  screenshot <win10-lite|win11-lite>
      Salva screenshot atual da VM em /tmp e mostra caminho.

  boot-iso <win10-lite|win11-lite>
      Ajusta boot (CD-ROM primeiro, depois disco) e habilita bootmenu.

  snapshot-clean <win10-lite|win11-lite>
      Desliga a VM e cria snapshot "clean" (atomic).

  snapshot-list <win10-lite|win11-lite>
      Lista snapshots da VM.

  restore-clean <win10-lite|win11-lite>
      Restaura snapshot "clean".

  audit
      Executa auditoria factual do host.

Exemplos:
  bash comum/scripts/windows-vm-control.sh status
  bash comum/scripts/windows-vm-control.sh start win10-lite
  bash comum/scripts/windows-vm-control.sh snapshot-clean win11-lite
EOF
}

fail() {
  echo "ERRO: $*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "comando obrigatorio ausente: $1"
}

virsh_sg() {
  sg libvirt -c "$*"
}

require_vm_name() {
  local vm="${1:-}"
  [ -n "$vm" ] || fail "informe a VM: $WIN10_VM ou $WIN11_VM"
  case "$vm" in
    "$WIN10_VM"|"$WIN11_VM") ;;
    *) fail "VM invalida: $vm (use $WIN10_VM ou $WIN11_VM)" ;;
  esac
}

vm_exists() {
  local vm="$1"
  virsh_sg "virsh dominfo $vm >/dev/null 2>&1"
}

vm_state() {
  local vm="$1"
  virsh_sg "virsh domstate $vm" | tr -d '\r'
}

wait_vm_state() {
  local vm="$1"
  local wanted="$2"
  local timeout_sec="$3"
  local elapsed=0
  local state=""
  while [ "$elapsed" -lt "$timeout_sec" ]; do
    state="$(vm_state "$vm" | tr -d '\n')"
    if [ "$state" = "$wanted" ]; then
      return 0
    fi
    sleep 2
    elapsed=$((elapsed + 2))
  done
  return 1
}

cmd_status() {
  virsh_sg "virsh list --all"
  echo
  for vm in "$WIN10_VM" "$WIN11_VM"; do
    if vm_exists "$vm"; then
      echo "### $vm"
      virsh_sg "virsh dominfo $vm | sed -n '1,20p'"
      echo
      virsh_sg "virsh snapshot-list $vm || true"
      echo
    else
      echo "### $vm"
      echo "nao criada"
      echo
    fi
  done
}

cmd_start() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  local state
  state="$(vm_state "$vm" | tr -d '\n')"
  if [ "$state" = "shut off" ]; then
    virsh_sg "virsh start $vm >/dev/null"
  fi
  echo "STATE: $(vm_state "$vm" | tr -d '\n')"
  echo "DISPLAY: $(virsh_sg "virsh domdisplay $vm" 2>/dev/null || echo unavailable)"
}

cmd_stop() {
  local vm="$1"
  local shutdown_wait_sec="${VM_SHUTDOWN_WAIT_SEC:-120}"
  local shutdown_cmd_timeout_sec="${VM_SHUTDOWN_CMD_TIMEOUT_SEC:-15}"
  local destroy_cmd_timeout_sec="${VM_DESTROY_CMD_TIMEOUT_SEC:-15}"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  local state
  state="$(vm_state "$vm" | tr -d '\n')"
  if [ "$state" = "shut off" ]; then
    echo "STATE: shut off"
    return 0
  fi

  if command -v timeout >/dev/null 2>&1; then
    virsh_sg "timeout ${shutdown_cmd_timeout_sec} virsh shutdown $vm >/dev/null 2>&1 || true"
  else
    virsh_sg "virsh shutdown $vm >/dev/null 2>&1 || true"
  fi

  if ! wait_vm_state "$vm" "shut off" "$shutdown_wait_sec"; then
    if command -v timeout >/dev/null 2>&1; then
      virsh_sg "timeout ${destroy_cmd_timeout_sec} virsh destroy $vm >/dev/null 2>&1 || true"
    else
      virsh_sg "virsh destroy $vm >/dev/null 2>&1 || true"
    fi

    if ! wait_vm_state "$vm" "shut off" 20; then
      echo "STATE: $(vm_state "$vm" | tr -d '\n')"
      return 1
    fi
  fi

  echo "STATE: $(vm_state "$vm" | tr -d '\n')"
}

cmd_display() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  virsh_sg "virsh domdisplay $vm"
}

cmd_screenshot() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  local out="/tmp/${vm}-$(date -u +%Y%m%dT%H%M%SZ).ppm"
  virsh_sg "virsh screenshot $vm $out --screen 0" >/dev/null
  echo "$out"
}

cmd_boot_iso() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  require_cmd virt-xml
  # Mantem firmware existente da VM e ajusta apenas ordem de boot/menu.
  virsh_sg "virsh destroy $vm >/dev/null 2>&1 || true"
  virsh_sg "virt-xml $vm --edit --boot boot1.dev=cdrom,boot2.dev=hd,bootmenu.enable=on,bootmenu.timeout=5000 >/dev/null"
  virsh_sg "virsh dumpxml $vm | rg 'boot dev|bootmenu' -n"
}

cmd_snapshot_clean() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  cmd_stop "$vm" >/dev/null
  virsh_sg "virsh snapshot-create-as $vm clean clean --atomic"
  virsh_sg "virsh snapshot-list $vm"
}

cmd_snapshot_list() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  virsh_sg "virsh snapshot-list $vm"
}

cmd_restore_clean() {
  local vm="$1"
  vm_exists "$vm" || fail "VM nao encontrada: $vm"
  virsh_sg "virsh snapshot-revert $vm clean --running"
  echo "STATE: $(vm_state "$vm" | tr -d '\n')"
}

cmd_audit() {
  bash "$PROJECT_ROOT/INSTALADOR/comum/scripts/audit-host-state.sh"
}

main() {
  require_cmd sg
  require_cmd virsh
  require_cmd rg

  local cmd="${1:-}"
  local vm="${2:-}"

  case "$cmd" in
    status)
      cmd_status
      ;;
    start)
      require_vm_name "$vm"
      cmd_start "$vm"
      ;;
    stop)
      require_vm_name "$vm"
      cmd_stop "$vm"
      ;;
    display)
      require_vm_name "$vm"
      cmd_display "$vm"
      ;;
    screenshot)
      require_vm_name "$vm"
      cmd_screenshot "$vm"
      ;;
    boot-iso)
      require_vm_name "$vm"
      cmd_boot_iso "$vm"
      ;;
    snapshot-clean)
      require_vm_name "$vm"
      cmd_snapshot_clean "$vm"
      ;;
    snapshot-list)
      require_vm_name "$vm"
      cmd_snapshot_list "$vm"
      ;;
    restore-clean)
      require_vm_name "$vm"
      cmd_restore_clean "$vm"
      ;;
    audit)
      cmd_audit
      ;;
    ""|-h|--help|help)
      usage
      ;;
    *)
      fail "comando invalido: $cmd"
      ;;
  esac
}

main "$@"
