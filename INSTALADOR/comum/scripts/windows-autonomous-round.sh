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

QGA_LIB="$INSTALADOR_ROOT/comum/scripts/windows-qga-lib.sh"
[ -f "$QGA_LIB" ] || {
  echo "ERROR: missing library: $QGA_LIB" >&2
  exit 1
}
source "$QGA_LIB"

DEFAULT_TIMEOUT_BOOTSTRAP=240
DEFAULT_TIMEOUT_REGRESSAO=5400

usage() {
  cat <<'EOF'
Uso:
  bash comum/scripts/windows-autonomous-round.sh <comando> --vm <win10-lite|win11-lite> [opcoes]

Comandos:
  bootstrap-qga
      Tenta habilitar qemu-guest-agent automaticamente (sem interacao humana).

  run-regressao
      Executa regressao Windows com payload via ISO/QGA.
      Modo via QGA_PAYLOAD_TRANSPORT_MODE:
      - iso-strict: falha imediata se ISO falhar (sem fallback para QGA)
      - iso|auto: ISO primario com fallback para QGA
      - qga: QGA direto

Opcoes:
  --vm <nome>           VM alvo (win10-lite ou win11-lite)
  --run-id <id>         Reutiliza RunId existente
  --timeout <segundos>  Timeout do comando (default por comando)
  -h, --help            Exibe ajuda
EOF
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    exit 1
  }
}

require_vm() {
  case "${1:-}" in
    win10-lite|win11-lite) ;;
    *)
      echo "ERROR: vm invalida: ${1:-<vazia>} (use win10-lite ou win11-lite)" >&2
      exit 1
      ;;
  esac
}

RUN_ID=""
RUN_DIR=""
CSV_FILE=""
SUMMARY_MD=""
VM=""
TIMEOUT=""

init_run() {
  if [ -z "$RUN_ID" ]; then
    RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
  fi
  # Padrao oficial da rodada: validacao-windows-<RUN_ID>
  RUN_DIR="$INSTALADOR_ROOT/saida/validacao-windows-${RUN_ID}"
  CSV_FILE="$RUN_DIR/resumo.csv"
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
  else
    local rc=$?
    record_step "$step" "FAIL" "$rc" "$log_file"
    return "$rc"
  fi
}

record_custom_step() {
  local step="$1"
  local status="$2"
  local exit_code="$3"
  local evidence="$4"
  record_step "$step" "$status" "$exit_code" "$evidence"
}

vm_is_running() {
  local vm="$1"
  local state
  state="$(sg libvirt -c "virsh domstate $vm" 2>/dev/null | tr -d '\r' || true)"
  [ "$state" = "running" ]
}

ensure_vm_running() {
  local vm="$1"
  if vm_is_running "$vm"; then
    return 0
  fi
  sg libvirt -c "virsh start $vm" >/dev/null
}

ensure_qga_channel() {
  local vm="$1"
  local log_file="$2"
  local channel_xml="$RUN_DIR/${vm}-qga-channel.xml"
  local has_channel="0"

  if sg libvirt -c "virsh dumpxml $vm" | rg -q "org.qemu.guest_agent.0"; then
    has_channel="1"
  fi

  if [ "$has_channel" = "1" ]; then
    {
      echo "QGA channel already present for $vm"
      sg libvirt -c "virsh dumpxml $vm | rg -n 'org.qemu.guest_agent.0|<channel type=.unix.'"
    } >"$log_file" 2>&1 || true
    return 0
  fi

  cat >"$channel_xml" <<'XML'
<channel type='unix'>
  <source mode='bind'/>
  <target type='virtio' name='org.qemu.guest_agent.0'/>
</channel>
XML

  {
    echo "QGA channel missing for $vm; attaching device."
    sg libvirt -c "virsh attach-device $vm '$channel_xml' --live --config"
    sg libvirt -c "virsh dumpxml $vm | rg -n 'org.qemu.guest_agent.0|<channel type=.unix.'"
  } >"$log_file" 2>&1 || {
    return 1
  }

  sg libvirt -c "virsh dumpxml $vm" | rg -q "org.qemu.guest_agent.0"
}

detect_cdrom_target() {
  local vm="$1"
  local log_file="$2"
  local dev=""

  if ! dev="$(sg libvirt -c "virsh dumpxml $vm" | awk '
/<disk / && /device='\''cdrom'\''/ {in_cd=1; next}
in_cd && /<target / {
  if (match($0, /dev='\''[^'\'']+'\''/)) {
    value = substr($0, RSTART + 5, RLENGTH - 6)
    print value
    exit 0
  }
}
in_cd && /<\/disk>/ {in_cd=0}
')"; then
    {
      echo "Could not detect CD-ROM target for $vm"
      sg libvirt -c "virsh dumpxml $vm | rg -n '<disk |device=.cdrom.|target dev='"
    } >"$log_file" 2>&1 || true
    return 1
  fi

  {
    echo "Detected CD-ROM target for $vm: $dev"
    sg libvirt -c "virsh dumpxml $vm | rg -n '<disk |device=.cdrom.|source file=|target dev='"
  } >"$log_file" 2>&1 || true
  printf '%s' "$dev"
}

cdrom_has_iso() {
  local vm="$1"
  local cdrom_dev="$2"
  local iso_path="$3"

  sg libvirt -c "virsh dumpxml $vm" | awk -v dev="$cdrom_dev" -v iso="$iso_path" '
/<disk / && /device='\''cdrom'\''/ {in_cd=1; src=""; tgt=""; next}
in_cd && /<source / {
  if (match($0, /file='\''[^'\'']+'\''/)) {
    src = substr($0, RSTART + 6, RLENGTH - 7)
  }
}
in_cd && /<target / {
  if (match($0, /dev='\''[^'\'']+'\''/)) {
    tgt = substr($0, RSTART + 5, RLENGTH - 6)
  }
}
in_cd && /<\/disk>/ {
  if (tgt == dev && src == iso) {
    found = 1
    exit 0
  }
  in_cd = 0
}
END { exit(found ? 0 : 1) }
'
}

attach_helper_iso() {
  local vm="$1"
  local iso_path="$2"
  local cdrom_dev="$3"
  local persist_config="${4:-0}"
  local log_file="$5"
  local media_flags="--live --force"

  if [ "$persist_config" = "1" ]; then
    media_flags="--live --config --force"
  fi

  {
    echo "VM=$vm"
    echo "CDROM=$cdrom_dev"
    echo "ISO=$iso_path"
    echo "persist_config=$persist_config"
    echo "media_flags=$media_flags"

    echo "[1/2] Trying change-media --update --force"
    if sg libvirt -c "virsh change-media $vm $cdrom_dev --source '$iso_path' --update $media_flags"; then
      echo "update_media=PASS"
    else
      rc="$?"
      echo "update_media=FAIL rc=$rc"
      echo "[fallback] Trying eject + insert"
      sg libvirt -c "virsh change-media $vm $cdrom_dev --eject $media_flags" || true
      sg libvirt -c "virsh change-media $vm $cdrom_dev --source '$iso_path' --insert $media_flags"
    fi

    echo "[2/2] Current cdrom mapping"
    sg libvirt -c "virsh dumpxml $vm | rg -n '<disk |device=.cdrom.|source file=|target dev='"
  } >"$log_file" 2>&1 || true

  cdrom_has_iso "$vm" "$cdrom_dev" "$iso_path"
}

stage_iso_for_qemu() {
  local source_iso="$1"
  local vm="$2"
  local iso_mode="${3:-stable}"
  local log_file="$4"
  local staged_iso=""
  local stable_dir="${QGA_HELPER_ISO_STABLE_DIR:-/tmp}"
  local source_tag=""
  local nonce=""

  source_tag="$(basename "$source_iso" | tr -c 'A-Za-z0-9._-' '_')"
  [ -n "$source_tag" ] || source_tag="iso"
  nonce="$(date +%s%N)-$$-${RANDOM:-0}"

  case "$iso_mode" in
    stable)
      staged_iso="${stable_dir%/}/${vm}-guest-tools-auto-stable.iso"
      ;;
    tmp)
      staged_iso="/tmp/${vm}-${source_tag}-${RUN_ID}-${nonce}.iso"
      ;;
    *)
      {
        echo "invalid_iso_mode=$iso_mode"
        echo "expected=stable|tmp"
      } >"$log_file" 2>&1
      return 1
      ;;
  esac

  {
    echo "ISO mode: $iso_mode"
    echo "Source ISO: $source_iso"
    echo "Staged ISO: $staged_iso"
    if [ "$source_iso" != "$staged_iso" ]; then
      cp -f "$source_iso" "$staged_iso" || {
        echo "ERROR: failed to copy ISO to staged path"
        return 1
      }
    fi
    chmod 0644 "$staged_iso" || {
      echo "ERROR: failed to chmod staged ISO"
      return 1
    }
    ls -lh "$staged_iso" || {
      echo "ERROR: failed to stat staged ISO"
      return 1
    }
  } >"$log_file" 2>&1 || return 1

  printf '%s' "$staged_iso"
}

cleanup_helper_media() {
  local vm="$1"
  local cdrom_dev="$2"
  local persist_config="${3:-0}"
  local log_file="$4"
  local media_flags="--live --force"

  if [ "$persist_config" = "1" ]; then
    media_flags="--live --config --force"
  fi

  {
    echo "VM=$vm"
    echo "CDROM=$cdrom_dev"
    echo "persist_config=$persist_config"
    echo "media_flags=$media_flags"
    sg libvirt -c "virsh change-media $vm $cdrom_dev --eject $media_flags"
    sg libvirt -c "virsh dumpxml $vm | rg -n '<disk |device=.cdrom.|source file=|target dev='"
  } >"$log_file" 2>&1
}

send_keys() {
  local vm="$1"
  local log_file="$2"
  shift 2
  local cmd="virsh send-key $vm"
  local key
  for key in "$@"; do
    cmd="$cmd $key"
  done
  echo "\$ sg libvirt -c \"$cmd\"" >> "$log_file"
  sg libvirt -c "$cmd" >> "$log_file" 2>&1 || true
  echo >> "$log_file"
}

send_bootstrap_sequence() {
  local vm="$1"
  local log_file="$2"

  # Fluxo minimizado e deterministico:
  # abre cmd elevado uma vez e tenta instalador em D: e E:
  # para reduzir impacto de layout de teclado e evitar multiplas janelas Run/cmd.
  send_keys "$vm" "$log_file" KEY_ENTER
  sleep 1

  # 1) Win+R -> cmd -> Ctrl+Shift+Enter (elevado)
  send_keys "$vm" "$log_file" KEY_LEFTMETA KEY_R
  sleep 2
  send_keys "$vm" "$log_file" KEY_C
  send_keys "$vm" "$log_file" KEY_M
  send_keys "$vm" "$log_file" KEY_D
  send_keys "$vm" "$log_file" KEY_LEFTCTRL KEY_LEFTSHIFT KEY_ENTER
  sleep 2

  # UAC best-effort para pt-BR (Alt+S) e en-US (Alt+Y).
  # Repetimos por alguns segundos para cobrir atraso de abertura da tela segura.
  local uac_try
  for uac_try in 1 2 3 4 5 6 7 8; do
    send_keys "$vm" "$log_file" KEY_LEFTALT KEY_S
    sleep 0.35
    send_keys "$vm" "$log_file" KEY_LEFTALT KEY_Y
    sleep 0.35
    send_keys "$vm" "$log_file" KEY_ENTER
    sleep 0.35
  done
  # Limpa eventual lixo digitado no cmd apos elevacao.
  send_keys "$vm" "$log_file" KEY_ESC
  sleep 3

  # 2) Tentativa D:
  send_keys "$vm" "$log_file" KEY_D
  # linux keycodes 42+53 reliably produce ":" on this VM keyboard layout.
  send_keys "$vm" "$log_file" 42 53
  send_keys "$vm" "$log_file" KEY_ENTER
  sleep 1
  send_keys "$vm" "$log_file" KEY_A
  send_keys "$vm" "$log_file" KEY_DOT
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_X
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_KPSLASH
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_ENTER
  sleep 3

  # 2b) Forca script de bootstrap no D: para garantir qemu-ga em Running.
  send_keys "$vm" "$log_file" KEY_P
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_W
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_R
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_H
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_L
  send_keys "$vm" "$log_file" KEY_L
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_MINUS
  send_keys "$vm" "$log_file" KEY_N
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_P
  send_keys "$vm" "$log_file" KEY_R
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_F
  send_keys "$vm" "$log_file" KEY_I
  send_keys "$vm" "$log_file" KEY_L
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_MINUS
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_X
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_C
  send_keys "$vm" "$log_file" KEY_U
  send_keys "$vm" "$log_file" KEY_T
  send_keys "$vm" "$log_file" KEY_I
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_N
  send_keys "$vm" "$log_file" KEY_P
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_L
  send_keys "$vm" "$log_file" KEY_I
  send_keys "$vm" "$log_file" KEY_C
  send_keys "$vm" "$log_file" KEY_Y
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_B
  send_keys "$vm" "$log_file" KEY_Y
  send_keys "$vm" "$log_file" KEY_P
  send_keys "$vm" "$log_file" KEY_A
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_MINUS
  send_keys "$vm" "$log_file" KEY_F
  send_keys "$vm" "$log_file" KEY_I
  send_keys "$vm" "$log_file" KEY_L
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_D
  send_keys "$vm" "$log_file" 42 53
  send_keys "$vm" "$log_file" KEY_KPSLASH
  send_keys "$vm" "$log_file" KEY_W
  send_keys "$vm" "$log_file" KEY_I
  send_keys "$vm" "$log_file" KEY_N
  send_keys "$vm" "$log_file" KEY_D
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_W
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_MINUS
  send_keys "$vm" "$log_file" KEY_G
  send_keys "$vm" "$log_file" KEY_U
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_T
  send_keys "$vm" "$log_file" KEY_MINUS
  send_keys "$vm" "$log_file" KEY_B
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_O
  send_keys "$vm" "$log_file" KEY_T
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_T
  send_keys "$vm" "$log_file" KEY_R
  send_keys "$vm" "$log_file" KEY_A
  send_keys "$vm" "$log_file" KEY_P
  send_keys "$vm" "$log_file" KEY_DOT
  send_keys "$vm" "$log_file" KEY_P
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_1
  send_keys "$vm" "$log_file" KEY_ENTER
  sleep 4

  # 3) Tentativa E:
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" 42 53
  send_keys "$vm" "$log_file" KEY_ENTER
  sleep 1
  send_keys "$vm" "$log_file" KEY_A
  send_keys "$vm" "$log_file" KEY_DOT
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_X
  send_keys "$vm" "$log_file" KEY_E
  send_keys "$vm" "$log_file" KEY_SPACE
  send_keys "$vm" "$log_file" KEY_KPSLASH
  send_keys "$vm" "$log_file" KEY_S
  send_keys "$vm" "$log_file" KEY_ENTER
  sleep 2
}

wait_for_qga() {
  local vm="$1"
  local timeout_sec="$2"
  local log_file="$3"
  local started now elapsed
  started="$(date +%s)"
  : > "$log_file"

  while :; do
    now="$(date +%s)"
    elapsed=$((now - started))
    if [ "$elapsed" -ge "$timeout_sec" ]; then
      echo "TIMEOUT after ${timeout_sec}s" >> "$log_file"
      return 1
    fi
    {
      echo "--- $(date -u +%Y-%m-%dT%H:%M:%SZ) ---"
      if qga_ping "$vm"; then
        echo "QGA_PING=PASS"
        return 0
      fi
      echo "QGA_PING=FAIL"
    } >> "$log_file" 2>&1
    sleep 5
  done
}

write_summary_md() {
  local command_name="$1"
  local vm="$2"

  local pass fail parcial bloqueado total
  pass="$(awk -F, 'NR>1 && $2=="PASS"{c++} END{print c+0}' "$CSV_FILE")"
  fail="$(awk -F, 'NR>1 && $2=="FAIL"{c++} END{print c+0}' "$CSV_FILE")"
  parcial="$(awk -F, 'NR>1 && $2=="PARCIAL"{c++} END{print c+0}' "$CSV_FILE")"
  bloqueado="$(awk -F, 'NR>1 && $2=="BLOQUEADO"{c++} END{print c+0}' "$CSV_FILE")"
  total="$(awk -F, 'NR>1{c++} END{print c+0}' "$CSV_FILE")"

  {
    echo "# Rodada Windows Autonoma (host + QGA)"
    echo
    echo "- RunId: ${RUN_ID}"
    echo "- Command: \`${command_name}\`"
    echo "- VM alvo: \`${vm}\`"
    echo "- TimestampUTC: \`$(date -u +%Y-%m-%dT%H:%M:%SZ)\`"
    echo
    echo "## Resumo"
    echo "- PASS: ${pass}"
    echo "- FAIL: ${fail}"
    echo "- PARCIAL: ${parcial}"
    echo "- BLOQUEADO: ${bloqueado}"
    echo "- TOTAL: ${total}"
    echo
    echo "## Evidencias"
    awk -F, 'NR>1{printf "- `%s` | `%s` | `%s`\n", $1, $2, $4}' "$CSV_FILE"
  } > "$SUMMARY_MD"
}

encode_ps_command() {
  local raw="$1"
  python3 - "$raw" <<'PY'
import base64
import sys
raw = sys.argv[1]
print(base64.b64encode(raw.encode("utf-16le")).decode("ascii"))
PY
}

qga_exec_powershell() {
  local vm="$1"
  local ps_script="$2"
  local timeout_sec="$3"
  local tag="$4"
  local wrapped_script encoded pid rc
  local pid_file="$RUN_DIR/${tag}.pid.log"
  local stdout_file="$RUN_DIR/${tag}.stdout.log"
  local stderr_file="$RUN_DIR/${tag}.stderr.log"
  local status_file="$RUN_DIR/${tag}.status.log"

  wrapped_script="\$ProgressPreference = 'SilentlyContinue'
$ps_script"
  encoded="$(encode_ps_command "$wrapped_script")"

  if ! pid="$(qga_guest_exec "$vm" "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" "-NoProfile" "-NonInteractive" "-ExecutionPolicy" "Bypass" "-EncodedCommand" "$encoded" 2>"$RUN_DIR/${tag}.exec.err.log")"; then
    echo "QGA guest-exec start failed" > "$status_file"
    return 1
  fi
  echo "$pid" > "$pid_file"

  if qga_wait_exec "$vm" "$pid" "$timeout_sec"; then
    rc=0
  else
    rc=1
  fi

  printf '%s' "${QGA_LAST_STDOUT:-}" > "$stdout_file"
  printf '%s' "${QGA_LAST_STDERR:-}" > "$stderr_file"
  {
    echo "qga_wait_rc=$rc"
    echo "guest_exitcode=${QGA_LAST_EXITCODE:-}"
  } > "$status_file"

  return "$rc"
}

sanitize_name() {
  printf '%s' "$1" | tr '\\/:*?"<>| ' '_'
}

create_autonomous_bundle() {
  local run_bundle_dir="$RUN_DIR/bundle-src"
  local bundle_zip="$RUN_DIR/protons-autonoma-bundle.zip"
  local version_file="$INSTALADOR_ROOT/comum/version.env"
  local installer_contract="$INSTALADOR_ROOT/testes/windows/installer-contract.json"
  local inno_source="$INSTALADOR_ROOT/windows/innosetup/protons-setup.iss"
  local wix_product_source="$INSTALADOR_ROOT/windows/wix/Product.wxs"
  local prepare_ux_assets_script="$INSTALADOR_ROOT/windows/scripts/prepare-inno-ux-assets.ps1"
  local installer_assets_dir="$INSTALADOR_ROOT/windows/ativos/installer"
  local asset_back_light="$installer_assets_dir/wizard_back_light.png"
  local asset_back_dark="$installer_assets_dir/wizard_back_dark.png"
  local asset_logo_light="$installer_assets_dir/wizard_small_logo_light.png"
  local asset_logo_dark="$installer_assets_dir/wizard_small_logo_dark.png"
  local version msi exe

  [ -f "$version_file" ] || {
    echo "ERROR: missing version file: $version_file" >&2
    return 1
  }
  [ -f "$installer_contract" ] || {
    echo "ERROR: missing installer contract: $installer_contract" >&2
    return 1
  }
  version="$(grep -E '^VERSION=' "$version_file" | head -n1 | cut -d= -f2 | tr -d '"')"
  [ -n "$version" ] || {
    echo "ERROR: could not parse VERSION from $version_file" >&2
    return 1
  }

  msi="$INSTALADOR_ROOT/saida/windows/Protons-${version}-x64.msi"
  exe="$INSTALADOR_ROOT/saida/windows/ProtonsSetup-${version}.exe"
  [ -f "$msi" ] || {
    echo "ERROR: missing MSI artifact: $msi" >&2
    return 1
  }
  [ -f "$exe" ] || {
    echo "ERROR: missing EXE artifact: $exe" >&2
    return 1
  }

  rm -rf "$run_bundle_dir"
  mkdir -p "$run_bundle_dir/INSTALADOR/testes/windows"
  mkdir -p "$run_bundle_dir/INSTALADOR/comum"
  mkdir -p "$run_bundle_dir/INSTALADOR/saida/windows"
  mkdir -p "$run_bundle_dir/INSTALADOR/windows/innosetup"
  mkdir -p "$run_bundle_dir/INSTALADOR/windows/wix"
  mkdir -p "$run_bundle_dir/INSTALADOR/windows/scripts"
  mkdir -p "$run_bundle_dir/INSTALADOR/windows/ativos/installer"

  cp "$INSTALADOR_ROOT/comum/version.env" "$run_bundle_dir/INSTALADOR/comum/version.env"
  cp "$installer_contract" "$run_bundle_dir/INSTALADOR/testes/windows/installer-contract.json"
  cp "$INSTALADOR_ROOT/testes/windows/"*.ps1 "$run_bundle_dir/INSTALADOR/testes/windows/"
  [ -f "$inno_source" ] || {
    echo "ERROR: missing Inno source: $inno_source" >&2
    return 1
  }
  [ -f "$wix_product_source" ] || {
    echo "ERROR: missing WiX source: $wix_product_source" >&2
    return 1
  }
  [ -f "$prepare_ux_assets_script" ] || {
    echo "ERROR: missing UX assets script: $prepare_ux_assets_script" >&2
    return 1
  }
  for asset in "$asset_back_light" "$asset_back_dark" "$asset_logo_light" "$asset_logo_dark"; do
    [ -f "$asset" ] || {
      echo "ERROR: missing UX installer asset: $asset" >&2
      echo "ERROR: run windows/scripts/build-inno.ps1 to regenerate light/dark assets." >&2
      return 1
    }
  done
  cp "$inno_source" "$run_bundle_dir/INSTALADOR/windows/innosetup/protons-setup.iss"
  cp "$wix_product_source" "$run_bundle_dir/INSTALADOR/windows/wix/Product.wxs"
  cp "$prepare_ux_assets_script" "$run_bundle_dir/INSTALADOR/windows/scripts/prepare-inno-ux-assets.ps1"
  cp "$asset_back_light" "$run_bundle_dir/INSTALADOR/windows/ativos/installer/"
  cp "$asset_back_dark" "$run_bundle_dir/INSTALADOR/windows/ativos/installer/"
  cp "$asset_logo_light" "$run_bundle_dir/INSTALADOR/windows/ativos/installer/"
  cp "$asset_logo_dark" "$run_bundle_dir/INSTALADOR/windows/ativos/installer/"
  cp "$msi" "$run_bundle_dir/INSTALADOR/saida/windows/"
  cp "${msi}.sha256" "$run_bundle_dir/INSTALADOR/saida/windows/" 2>/dev/null || true
  cp "$exe" "$run_bundle_dir/INSTALADOR/saida/windows/"
  cp "${exe}.sha256" "$run_bundle_dir/INSTALADOR/saida/windows/" 2>/dev/null || true

  (cd "$run_bundle_dir" && zip -qr "$bundle_zip" INSTALADOR)

  if ! python3 - "$bundle_zip" <<'PY'
import sys
import zipfile

required = {
    "INSTALADOR/testes/windows/run-regressao.ps1",
    "INSTALADOR/testes/windows/Test-Common.ps1",
    "INSTALADOR/testes/windows/installer-contract.json",
}

with zipfile.ZipFile(sys.argv[1]) as zf:
    names = set(zf.namelist())

missing = sorted(required - names)
if missing:
    raise SystemExit("missing required entries: " + ", ".join(missing))
PY
  then
    echo "ERROR: autonomous bundle missing required files." >&2
    return 1
  fi

  printf '%s' "$bundle_zip"
}

command_bootstrap_qga() {
  local vm="$1"
  local step_prefix="bootstrap_qga_${vm}"
  local bootstrap_mode="${QGA_BOOTSTRAP_MODE:-auto}"
  local helper_iso_mode="${QGA_HELPER_ISO_MODE:-tmp}"
  local helper_persist_config=1
  local iso_path=""
  local qga_channel_log="$RUN_DIR/${step_prefix}_qga_channel.log"
  local qga_precheck_log="$RUN_DIR/${step_prefix}_qga_precheck.log"
  local cdrom_detect_log="$RUN_DIR/${step_prefix}_cdrom_detect.log"
  local stage_iso_log="$RUN_DIR/${step_prefix}_stage_iso.log"
  local change_media_log="$RUN_DIR/${step_prefix}_change_media.log"
  local cleanup_media_log="$RUN_DIR/${step_prefix}_cleanup_media.log"
  local send_keys_log="$RUN_DIR/${step_prefix}_send_keys.log"
  local pre_keys_wait_log="$RUN_DIR/${step_prefix}_pre_keys_wait.log"
  local qga_wait_log="$RUN_DIR/${step_prefix}_qga_wait.log"
  local cdrom_dev=""
  local staged_iso=""
  local helper_attached=0
  local qga_ok=0
  local qga_precheck_ok=0
  local attempt
  local max_attempts="${QGA_BOOTSTRAP_MAX_ATTEMPTS:-3}"
  local post_keys_sleep="${QGA_BOOTSTRAP_POST_KEYS_SLEEP_SEC:-8}"
  local pre_keys_wait="${QGA_BOOTSTRAP_PRE_KEYS_WAIT_SEC:-90}"
  local ping_timeout="${QGA_BOOTSTRAP_PING_TIMEOUT_SEC:-240}"
  local reboot_sleep="${QGA_BOOTSTRAP_REBOOT_SLEEP_SEC:-45}"

  case "$bootstrap_mode" in
    auto|manual-ready) ;;
    *)
      {
        echo "invalid_bootstrap_mode=$bootstrap_mode"
        echo "expected=auto|manual-ready"
      } > "$qga_precheck_log"
      record_custom_step "${step_prefix}_qga_precheck" "BLOQUEADO" "1" "$qga_precheck_log"
      record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$qga_precheck_log"
      write_summary_md "bootstrap-qga" "$vm"
      return 0
      ;;
  esac

  case "$helper_iso_mode" in
    stable)
      helper_persist_config=1
      ;;
    tmp)
      helper_persist_config=0
      ;;
    *)
      {
        echo "invalid_qga_helper_iso_mode=$helper_iso_mode"
        echo "expected=stable|tmp"
      } > "$stage_iso_log"
      record_custom_step "${step_prefix}_stage_iso" "BLOQUEADO" "1" "$stage_iso_log"
      record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$stage_iso_log"
      write_summary_md "bootstrap-qga" "$vm"
      return 0
      ;;
  esac

  if ! [[ "$pre_keys_wait" =~ ^[0-9]+$ ]]; then
    pre_keys_wait=60
  fi
  if ! [[ "$reboot_sleep" =~ ^[0-9]+$ ]]; then
    reboot_sleep=45
  fi
  if ! [[ "$ping_timeout" =~ ^[0-9]+$ ]]; then
    ping_timeout=240
  fi

  if [ "${QGA_SKIP_RESTORE_CLEAN:-0}" = "1" ] || [ "$bootstrap_mode" = "manual-ready" ]; then
    {
      echo "skip_restore_clean=1"
      echo "reason=$( [ "$bootstrap_mode" = "manual-ready" ] && echo 'manual-ready-mode' || echo 'QGA_SKIP_RESTORE_CLEAN=1' )"
    } > "$RUN_DIR/${step_prefix}_restore_clean.log"
    record_custom_step "${step_prefix}_restore_clean" "PARCIAL" "0" "$RUN_DIR/${step_prefix}_restore_clean.log"
  else
    run_logged_step "${step_prefix}_restore_clean" "$RUN_DIR/${step_prefix}_restore_clean.log" \
      sg libvirt -c "virsh snapshot-revert $vm clean --running" || true
  fi

  run_logged_step "${step_prefix}_ensure_running" "$RUN_DIR/${step_prefix}_ensure_running.log" \
    ensure_vm_running "$vm" || true

  if ensure_qga_channel "$vm" "$qga_channel_log"; then
    record_custom_step "${step_prefix}_qga_channel" "PASS" "0" "$qga_channel_log"
  else
    record_custom_step "${step_prefix}_qga_channel" "BLOQUEADO" "1" "$qga_channel_log"
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$qga_channel_log"
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi

  {
    echo "bootstrap_mode=$bootstrap_mode"
    echo "--- $(date -u +%Y-%m-%dT%H:%M:%SZ) ---"
    if qga_ping "$vm"; then
      echo "QGA_PING=PASS"
      qga_precheck_ok=1
    else
      echo "QGA_PING=FAIL"
    fi
  } > "$qga_precheck_log" 2>&1 || true

  if [ "$qga_precheck_ok" -eq 1 ]; then
    record_custom_step "${step_prefix}_qga_precheck" "PASS" "0" "$qga_precheck_log"
    record_custom_step "${step_prefix}_qga_ping" "PASS" "0" "$qga_precheck_log"
    record_custom_step "${step_prefix}_result" "PASS" "0" "$qga_precheck_log"
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi
  record_custom_step "${step_prefix}_qga_precheck" "PARCIAL" "0" "$qga_precheck_log"

  if [ "$bootstrap_mode" = "manual-ready" ]; then
    {
      echo "manual-ready mode enabled"
      echo "send_keys=disabled"
      echo "waiting_for_qga_timeout_sec=$ping_timeout"
    } > "$send_keys_log"
    record_custom_step "${step_prefix}_send_keys" "PARCIAL" "0" "$send_keys_log"

    if wait_for_qga "$vm" "$ping_timeout" "$qga_wait_log"; then
      record_custom_step "${step_prefix}_qga_ping" "PASS" "0" "$qga_wait_log"
      record_custom_step "${step_prefix}_result" "PASS" "0" "$qga_wait_log"
    else
      record_custom_step "${step_prefix}_qga_ping" "BLOQUEADO" "1" "$qga_wait_log"
      record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$qga_wait_log"
    fi
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi

  if cdrom_dev="$(detect_cdrom_target "$vm" "$cdrom_detect_log")"; then
    record_custom_step "${step_prefix}_detect_cdrom" "PASS" "0" "$cdrom_detect_log"
  else
    record_custom_step "${step_prefix}_detect_cdrom" "BLOQUEADO" "1" "$cdrom_detect_log"
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$cdrom_detect_log"
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi

  if [ "$helper_iso_mode" = "stable" ]; then
    if [ -f "$RUN_DIR/guest-tools-auto.iso" ]; then
      iso_path="$RUN_DIR/guest-tools-auto.iso"
    elif [ -f "$INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso" ]; then
      iso_path="$INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso"
    elif [ -f /tmp/guest-tools-auto.iso ]; then
      iso_path="/tmp/guest-tools-auto.iso"
    fi
  else
    if [ -f /tmp/guest-tools-auto.iso ]; then
      iso_path="/tmp/guest-tools-auto.iso"
    elif [ -f "$RUN_DIR/guest-tools-auto.iso" ]; then
      iso_path="$RUN_DIR/guest-tools-auto.iso"
    elif [ -f "$INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso" ]; then
      iso_path="$INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso"
    fi
  fi

  if [ -z "$iso_path" ]; then
    {
      echo "ISO helper not found"
      echo "helper_iso_mode=$helper_iso_mode"
      echo "candidates:"
      echo "- $RUN_DIR/guest-tools-auto.iso"
      echo "- $INSTALADOR_ROOT/saida/guest-agent-bootstrap/guest-tools-auto.iso"
      echo "- /tmp/guest-tools-auto.iso"
    } > "$change_media_log"
    record_custom_step "${step_prefix}_change_media" "BLOQUEADO" "1" "$change_media_log"
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$change_media_log"
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi

  if staged_iso="$(stage_iso_for_qemu "$iso_path" "$vm" "$helper_iso_mode" "$stage_iso_log")"; then
    record_custom_step "${step_prefix}_stage_iso" "PASS" "0" "$stage_iso_log"
  else
    record_custom_step "${step_prefix}_stage_iso" "BLOQUEADO" "1" "$stage_iso_log"
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$stage_iso_log"
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi

  if attach_helper_iso "$vm" "$staged_iso" "$cdrom_dev" "$helper_persist_config" "$change_media_log"; then
    record_custom_step "${step_prefix}_change_media" "PASS" "0" "$change_media_log"
    helper_attached=1
  else
    record_custom_step "${step_prefix}_change_media" "BLOQUEADO" "1" "$change_media_log"
    if cleanup_helper_media "$vm" "$cdrom_dev" "$helper_persist_config" "$cleanup_media_log"; then
      record_custom_step "${step_prefix}_cleanup_media" "PASS" "0" "$cleanup_media_log"
    else
      record_custom_step "${step_prefix}_cleanup_media" "PARCIAL" "1" "$cleanup_media_log"
    fi
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$change_media_log"
    write_summary_md "bootstrap-qga" "$vm"
    return 0
  fi

  {
    echo "pre_keys_wait_sec=$pre_keys_wait"
    echo "ping_timeout_sec=$ping_timeout"
    echo "post_keys_sleep_sec=$post_keys_sleep"
    echo "reboot_sleep_sec=$reboot_sleep"
    echo "bootstrap_mode=$bootstrap_mode"
    echo "helper_iso_mode=$helper_iso_mode"
    echo "wait_started_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$pre_keys_wait_log"
  if [ "$pre_keys_wait" -gt 0 ]; then
    sleep "$pre_keys_wait"
  fi
  echo "wait_finished_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$pre_keys_wait_log"
  record_custom_step "${step_prefix}_pre_keys_wait" "PASS" "0" "$pre_keys_wait_log"

  run_logged_step "${step_prefix}_screenshot_before" "$RUN_DIR/${step_prefix}_screenshot_before.log" \
    sg libvirt -c "virsh screenshot $vm '$RUN_DIR/${vm}-bootstrap-before.ppm' --screen 0" || true

  : > "$send_keys_log"
  for ((attempt=1; attempt<=max_attempts; attempt++)); do
    echo "=== ATTEMPT ${attempt} ===" >> "$send_keys_log"
    send_bootstrap_sequence "$vm" "$send_keys_log"
    sleep "$post_keys_sleep"
    run_logged_step "${step_prefix}_screenshot_attempt_${attempt}" "$RUN_DIR/${step_prefix}_screenshot_attempt_${attempt}.log" \
      sg libvirt -c "virsh screenshot $vm '$RUN_DIR/${vm}-bootstrap-attempt-${attempt}.ppm' --screen 0" || true

    if wait_for_qga "$vm" "$ping_timeout" "$qga_wait_log"; then
      qga_ok=1
      break
    fi

    if [ "$attempt" -lt "$max_attempts" ]; then
      run_logged_step "${step_prefix}_reboot_attempt_${attempt}" "$RUN_DIR/${step_prefix}_reboot_attempt_${attempt}.log" \
        bash -lc "timeout 12 sg libvirt -c 'virsh reboot $vm' || (sg libvirt -c 'virsh destroy $vm' >/dev/null 2>&1 || true; sg libvirt -c 'virsh start $vm' >/dev/null 2>&1 || true)"
      sleep "$reboot_sleep"
    fi
  done
  record_custom_step "${step_prefix}_send_keys" "PASS" "0" "$send_keys_log"

  if [ "$qga_ok" -eq 1 ]; then
    record_custom_step "${step_prefix}_qga_ping" "PASS" "0" "$qga_wait_log"
    record_custom_step "${step_prefix}_result" "PASS" "0" "$qga_wait_log"
  else
    record_custom_step "${step_prefix}_qga_ping" "BLOQUEADO" "1" "$qga_wait_log"
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$qga_wait_log"
  fi

  if [ "$helper_attached" -eq 1 ]; then
    if cleanup_helper_media "$vm" "$cdrom_dev" "$helper_persist_config" "$cleanup_media_log"; then
      record_custom_step "${step_prefix}_cleanup_media" "PASS" "0" "$cleanup_media_log"
    else
      record_custom_step "${step_prefix}_cleanup_media" "PARCIAL" "1" "$cleanup_media_log"
    fi
  else
    {
      echo "helper_media_not_attached=1"
      echo "skip_cleanup=1"
    } > "$cleanup_media_log"
    record_custom_step "${step_prefix}_cleanup_media" "PARCIAL" "0" "$cleanup_media_log"
  fi

  write_summary_md "bootstrap-qga" "$vm"
  return 0
}

command_run_regressao() {
  local vm="$1"
  local step_prefix="run_regressao_${vm}"
  local qga_log="$RUN_DIR/${step_prefix}_qga_ping.log"
  local bundle_zip=""
  local bundle_upload_log="$RUN_DIR/${step_prefix}_bundle_upload.log"
  local transfer_metrics_log="$RUN_DIR/${step_prefix}_bundle_transfer_metrics.log"
  local bundle_zip_name=""
  local guest_bundle_zip="C:\\Windows\\Temp\\protons-autonoma-bundle-${RUN_ID}.zip"
  local guest_project_root="C:\\protons-runs\\${RUN_ID}"
  local transport_mode="unknown"
  local transport_mode_log="$RUN_DIR/${step_prefix}_transport_mode.log"
  local payload_transport_mode="${QGA_PAYLOAD_TRANSPORT_MODE:-iso-strict}"
  local iso_build_log="$RUN_DIR/${step_prefix}_bundle_iso_build.log"
  local iso_stage_log="$RUN_DIR/${step_prefix}_bundle_iso_stage.log"
  local iso_attach_log="$RUN_DIR/${step_prefix}_bundle_iso_attach.log"
  local iso_copy_log="$RUN_DIR/${step_prefix}_bundle_iso_copy.log"
  local iso_cleanup_log="$RUN_DIR/${step_prefix}_bundle_iso_cleanup.log"
  local iso_bundle_file="$RUN_DIR/${step_prefix}-bundle.iso"
  local staged_iso_bundle=""
  local cdrom_dev=""
  local helper_persist_config=0
  local iso_transfer_ok=0
  local iso_strict_mode=0
  local iso_build_attempted=0
  local iso_build_ok=0
  local iso_stage_attempted=0
  local iso_stage_ok=0
  local iso_attach_attempted=0
  local iso_attach_ok=0
  local iso_copy_attempted=0
  local iso_failure_stage=""
  local regression_ps=""
  local index_ps=""
  local index_file="$RUN_DIR/${step_prefix}_guest_file_index.log"
  local strict_signature_enabled="${WINDOWS_REGRESSION_STRICT_SIGNATURE:-0}"
  local signature_profile="${WINDOWS_REGRESSION_SIGNATURE_PROFILE:-technical}"
  local strict_signature_arg=""
  local regression_extra_args="${PROTONS_REGRESSION_EXTRA_ARGS:-}"
  local regression_extra_args_escaped=""
  local upload_timeout_sec="${QGA_UPLOAD_TIMEOUT_SEC:-1800}"
  local write_chunk_size="${QGA_WRITE_CHUNK_SIZE:-8192}"
  local write_progress_every_mb="${QGA_WRITE_PROGRESS_EVERY_MB:-0}"
  local upload_max_attempts="${QGA_UPLOAD_MAX_ATTEMPTS:-2}"
  local upload_retry_sleep_sec="${QGA_UPLOAD_RETRY_SLEEP_SEC:-8}"
  local qga_recover_timeout_sec="${QGA_UPLOAD_QGA_RECOVER_TIMEOUT_SEC:-45}"
  local upload_bytes=0
  local upload_start=0
  local upload_end=0
  local upload_elapsed=0
  local upload_rate_bps=0
  local upload_rc=0
  local upload_attempt=1
  local upload_attempt_rc=0
  local upload_attempts_used=0
  local upload_attempt_start=0
  local upload_attempt_end=0
  local upload_attempt_elapsed=0
  local recover_started=0
  local recover_now=0
  local recover_elapsed=0
  local invoke_regression_rc=0
  local gpath out_file base_name normalized_path
  local artifact_minimum_ok=1
  local missing_artifacts_log="$RUN_DIR/${step_prefix}_missing_artifacts.log"
  local required_artifacts=(
    "regressao-windows-${RUN_ID}.json"
    "msi-results-${RUN_ID}.json"
    "inno-results-${RUN_ID}.json"
    "artifact-verify-${RUN_ID}.json"
    "msi-metrics-${RUN_ID}.json"
    "inno-metrics-${RUN_ID}.json"
    "upgrade-metrics-${RUN_ID}.json"
  )

  case " ${regression_extra_args} " in
    *" -EnableResilienceSuite "*)
      required_artifacts+=(
        "msi-rollback-results-${RUN_ID}.json"
        "inno-rollback-results-${RUN_ID}.json"
        "transactional-results-${RUN_ID}.json"
        "power-recovery-results-${RUN_ID}.json"
        "msi-repair-results-${RUN_ID}.json"
      )
      ;;
  esac

  case " ${regression_extra_args} " in
    *" -EnableUxSuite "*)
      required_artifacts+=(
        "ux-contract-results-${RUN_ID}.json"
        "cancel-install-results-${RUN_ID}.json"
        "time-estimate-results-${RUN_ID}.json"
      )
      ;;
  esac

  case " ${regression_extra_args} " in
    *" -RequireDefenderActive "*)
      required_artifacts+=("defender-results-${RUN_ID}.json")
      ;;
  esac

  case "$payload_transport_mode" in
    auto)
      payload_transport_mode="iso"
      ;;
    iso-strict|iso|qga) ;;
    *)
      payload_transport_mode="iso"
      ;;
  esac
  if [ "$payload_transport_mode" = "iso-strict" ]; then
    iso_strict_mode=1
  fi

  case "$signature_profile" in
    technical|production) ;;
    *)
      signature_profile="technical"
      ;;
  esac
  if [ "$strict_signature_enabled" = "1" ] && [ "$signature_profile" = "technical" ]; then
    signature_profile="production"
  fi

  run_logged_step "${step_prefix}_ensure_running" "$RUN_DIR/${step_prefix}_ensure_running.log" \
    ensure_vm_running "$vm" || true

  {
    qga_ping "$vm"
  } > "$qga_log" 2>&1 || true

  if ! qga_ping "$vm" >/dev/null 2>&1; then
    record_custom_step "${step_prefix}_qga_ping" "BLOQUEADO" "1" "$qga_log"
    record_custom_step "${step_prefix}_result" "BLOQUEADO" "1" "$qga_log"
    write_summary_md "run-regressao" "$vm"
    return 0
  fi
  record_custom_step "${step_prefix}_qga_ping" "PASS" "0" "$qga_log"

  if ! bundle_zip="$(create_autonomous_bundle 2>"$RUN_DIR/${step_prefix}_bundle.err.log")"; then
    record_custom_step "${step_prefix}_bundle" "FAIL" "1" "$RUN_DIR/${step_prefix}_bundle.err.log"
    record_custom_step "${step_prefix}_result" "FAIL" "1" "$RUN_DIR/${step_prefix}_bundle.err.log"
    write_summary_md "run-regressao" "$vm"
    return 0
  fi
  record_custom_step "${step_prefix}_bundle" "PASS" "0" "$bundle_zip"

  if ! [[ "$upload_max_attempts" =~ ^[0-9]+$ ]] || [ "$upload_max_attempts" -le 0 ]; then
    upload_max_attempts=2
  fi
  if ! [[ "$upload_retry_sleep_sec" =~ ^[0-9]+$ ]] || [ "$upload_retry_sleep_sec" -lt 0 ]; then
    upload_retry_sleep_sec=8
  fi
  if ! [[ "$qga_recover_timeout_sec" =~ ^[0-9]+$ ]] || [ "$qga_recover_timeout_sec" -lt 0 ]; then
    qga_recover_timeout_sec=45
  fi

  upload_bytes="$(wc -c < "$bundle_zip" | tr -d ' ' || printf '0')"
  bundle_zip_name="$(basename "$bundle_zip")"
  upload_start="$(date +%s)"
  {
    echo "vm=$vm"
    echo "bundle_zip=$bundle_zip"
    echo "bundle_zip_name=$bundle_zip_name"
    echo "guest_bundle_zip=$guest_bundle_zip"
    echo "upload_timeout_sec=$upload_timeout_sec"
    echo "upload_bytes=$upload_bytes"
    echo "qga_write_chunk_size=$write_chunk_size"
    echo "qga_write_progress_every_mb=$write_progress_every_mb"
    echo "qga_upload_max_attempts=$upload_max_attempts"
    echo "qga_upload_retry_sleep_sec=$upload_retry_sleep_sec"
    echo "qga_upload_qga_recover_timeout_sec=$qga_recover_timeout_sec"
    echo "payload_transport_mode=$payload_transport_mode"
    echo "signature_profile=$signature_profile"
  } > "$transfer_metrics_log"

  : > "$bundle_upload_log"
  upload_rc=1
  upload_attempt=1
  upload_attempts_used=0
  transport_mode="unknown"

  if [ "$payload_transport_mode" != "qga" ]; then
    {
      echo "transport_attempt=iso"
      echo "iso_bundle_file=$iso_bundle_file"
      echo "iso_strict_mode=$iso_strict_mode"
    } >> "$bundle_upload_log"

    iso_build_attempted=1
    if xorriso -as mkisofs -J -joliet-long -r -o "$iso_bundle_file" -V PROTONSBUNDLE -graft-points "${bundle_zip_name}=${bundle_zip}" >"$iso_build_log" 2>&1; then
      iso_build_ok=1
      iso_attach_attempted=1
      if cdrom_dev="$(detect_cdrom_target "$vm" "$iso_attach_log")"; then
        iso_stage_attempted=1
        if staged_iso_bundle="$(stage_iso_for_qemu "$iso_bundle_file" "$vm" "tmp" "$iso_stage_log")"; then
          iso_stage_ok=1
          if attach_helper_iso "$vm" "$staged_iso_bundle" "$cdrom_dev" "$helper_persist_config" "$iso_attach_log"; then
            iso_attach_ok=1
            iso_copy_attempted=1
            regression_ps="
\$ErrorActionPreference = 'Stop'
\$dest = '${guest_bundle_zip}'
\$source = \$null
\$bundleName = '${bundle_zip_name}'
\$maxAttempts = 20
\$waitSeconds = 2

for (\$attempt = 1; \$attempt -le \$maxAttempts -and -not \$source; \$attempt++) {
  \$roots = @()
  try {
    \$roots += @(Get-CimInstance -ClassName Win32_LogicalDisk -ErrorAction SilentlyContinue | Where-Object { \$_.DriveType -eq 5 -and \$_.DeviceID } | ForEach-Object { (\$_.DeviceID + '\') })
  } catch {}
  if (\$roots.Count -eq 0) {
    try {
      \$roots += @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue | ForEach-Object { \$_.Root })
    } catch {}
  }
  \$roots = @(\$roots | Where-Object { \$_ -match '^[A-Za-z]:\\\\$' } | Sort-Object -Unique)

  foreach (\$root in \$roots) {
    \$exact = Join-Path \$root \$bundleName
    if (Test-Path -LiteralPath \$exact) {
      \$source = \$exact
      break
    }

    \$zipCandidates = @(Get-ChildItem -LiteralPath \$root -File -Filter '*.zip' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    foreach (\$candidate in \$zipCandidates) {
      if (\$candidate.Name -ieq \$bundleName -or \$candidate.Name -match '^PROTONS.*\\.ZIP$') {
        \$source = \$candidate.FullName
        break
      }
    }
    if (\$source) { break }
  }

  if (-not \$source -and \$attempt -lt \$maxAttempts) {
    Start-Sleep -Seconds \$waitSeconds
  }
}

if (-not \$source) {
  \$rootsInfo = @()
  try {
    \$rootsInfo = @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue | ForEach-Object { \$_.Root })
  } catch {}
  throw ('bundle_not_found_on_iso; bundle=' + \$bundleName + '; roots=' + (\$rootsInfo -join ','))
}

Copy-Item -LiteralPath \$source -Destination \$dest -Force
if (-not (Test-Path -LiteralPath \$dest)) { throw 'bundle_copy_from_iso_failed' }
Write-Output \"bundle_copied_from_iso=\$source\"
"
            if qga_exec_powershell "$vm" "$regression_ps" 180 "${step_prefix}_iso_copy_bundle"; then
              cp "$RUN_DIR/${step_prefix}_iso_copy_bundle.stdout.log" "$iso_copy_log" 2>/dev/null || true
              iso_transfer_ok=1
              upload_rc=0
              upload_attempts_used=1
              transport_mode="iso"
            else
              cp "$RUN_DIR/${step_prefix}_iso_copy_bundle.stderr.log" "$iso_copy_log" 2>/dev/null || true
              iso_failure_stage="copy"
            fi
          else
            iso_failure_stage="attach"
          fi
        else
          iso_failure_stage="stage"
        fi
      else
        iso_failure_stage="attach"
      fi
    else
      iso_failure_stage="build"
    fi

    if [ -z "$iso_failure_stage" ] && [ "$iso_transfer_ok" -ne 1 ]; then
      iso_failure_stage="copy"
    fi

    if [ -n "${cdrom_dev:-}" ]; then
      if cleanup_helper_media "$vm" "$cdrom_dev" "$helper_persist_config" "$iso_cleanup_log"; then
        :
      else
        echo "iso_cleanup_warn=1" >> "$iso_cleanup_log"
      fi
    fi

    if [ "$iso_transfer_ok" -eq 1 ]; then
      record_custom_step "${step_prefix}_bundle_iso_build" "PASS" "0" "$iso_build_log"
      record_custom_step "${step_prefix}_bundle_iso_stage" "PASS" "0" "$iso_stage_log"
      record_custom_step "${step_prefix}_bundle_iso_attach" "PASS" "0" "$iso_attach_log"
      record_custom_step "${step_prefix}_bundle_upload" "PASS" "0" "$iso_copy_log"
      record_custom_step "${step_prefix}_bundle_transfer_metrics" "PASS" "0" "$transfer_metrics_log"
    else
      local iso_build_status="BLOQUEADO"
      local iso_stage_status="BLOQUEADO"
      local iso_attach_status="BLOQUEADO"

      if [ "$iso_build_attempted" -eq 1 ]; then
        if [ "$iso_build_ok" -eq 1 ]; then
          iso_build_status="PASS"
        else
          iso_build_status="FAIL"
        fi
      fi
      if [ "$iso_stage_attempted" -eq 1 ]; then
        if [ "$iso_stage_ok" -eq 1 ]; then
          iso_stage_status="PASS"
        else
          iso_stage_status="FAIL"
        fi
      fi
      if [ "$iso_attach_attempted" -eq 1 ]; then
        if [ "$iso_attach_ok" -eq 1 ]; then
          iso_attach_status="PASS"
        else
          iso_attach_status="FAIL"
        fi
      fi

      record_custom_step "${step_prefix}_bundle_iso_build" "$iso_build_status" "1" "$iso_build_log"
      record_custom_step "${step_prefix}_bundle_iso_stage" "$iso_stage_status" "1" "$iso_stage_log"
      record_custom_step "${step_prefix}_bundle_iso_attach" "$iso_attach_status" "1" "$iso_attach_log"

      if [ "$iso_strict_mode" -eq 1 ]; then
        transport_mode="iso-strict-blocked"
        upload_rc=101
        echo "iso_transfer_failed=1 strict_mode=1 stage=$iso_failure_stage fallback=disabled" >> "$bundle_upload_log"
      else
        echo "iso_transfer_failed=1 strict_mode=0 stage=$iso_failure_stage fallback=qga" >> "$bundle_upload_log"
      fi
    fi
  fi

  if [ "$iso_transfer_ok" -ne 1 ] && [ "$iso_strict_mode" -eq 1 ] && [ "$payload_transport_mode" != "qga" ]; then
    upload_end="$(date +%s)"
    upload_elapsed=$((upload_end - upload_start))
    if [ "$upload_elapsed" -le 0 ]; then
      upload_elapsed=1
    fi
    upload_rate_bps=$((upload_bytes / upload_elapsed))
    {
      echo "upload_rc=${upload_rc:-1}"
      echo "upload_attempts_used=$upload_attempts_used"
      echo "upload_elapsed_sec=$upload_elapsed"
      echo "upload_rate_bps=$upload_rate_bps"
      echo "transport_mode=$transport_mode"
      echo "upload_timeout_reached=0"
    } >> "$transfer_metrics_log"
    {
      echo "transport_mode=$transport_mode"
      echo "payload_transport_mode=$payload_transport_mode"
      echo "iso_failure_stage=$iso_failure_stage"
    } > "$transport_mode_log"
    record_custom_step "${step_prefix}_transport_mode" "FAIL" "${upload_rc:-1}" "$transport_mode_log"
    record_custom_step "${step_prefix}_bundle_upload" "FAIL" "${upload_rc:-1}" "$bundle_upload_log"
    record_custom_step "${step_prefix}_bundle_transfer_metrics" "FAIL" "${upload_rc:-1}" "$transfer_metrics_log"
    record_custom_step "${step_prefix}_result" "FAIL" "${upload_rc:-1}" "$bundle_upload_log"
    write_summary_md "run-regressao" "$vm"
    return 0
  fi

  if [ "$iso_transfer_ok" -ne 1 ] && [ "$payload_transport_mode" != "iso-strict" ]; then
    transport_mode="qga"
    while [ "$upload_attempt" -le "$upload_max_attempts" ]; do
      upload_attempts_used="$upload_attempt"
      {
        echo "upload_attempt=${upload_attempt}/${upload_max_attempts}"
        echo "upload_attempt_started_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
      } >> "$bundle_upload_log"

      upload_attempt_start="$(date +%s)"
      if command -v timeout >/dev/null 2>&1; then
        if timeout "$upload_timeout_sec" bash -lc '
          set -euo pipefail
          source "$1"
          QGA_WRITE_CHUNK_SIZE="$2" \
          QGA_WRITE_PROGRESS_EVERY_MB="$3" \
          qga_write_file "$4" "$5" "$6"
        ' _ "$QGA_LIB" "$write_chunk_size" "$write_progress_every_mb" "$vm" "$bundle_zip" "$guest_bundle_zip" >>"$bundle_upload_log" 2>&1; then
          upload_attempt_rc=0
        else
          upload_attempt_rc=$?
        fi
      else
        if QGA_WRITE_CHUNK_SIZE="$write_chunk_size" \
           QGA_WRITE_PROGRESS_EVERY_MB="$write_progress_every_mb" \
           qga_write_file "$vm" "$bundle_zip" "$guest_bundle_zip" >>"$bundle_upload_log" 2>&1; then
          upload_attempt_rc=0
        else
          upload_attempt_rc=$?
        fi
      fi
      upload_attempt_end="$(date +%s)"
      upload_attempt_elapsed=$((upload_attempt_end - upload_attempt_start))
      if [ "$upload_attempt_elapsed" -le 0 ]; then
        upload_attempt_elapsed=1
      fi

      {
        echo "upload_attempt_rc=$upload_attempt_rc"
        echo "upload_attempt_elapsed_sec=$upload_attempt_elapsed"
      } >> "$bundle_upload_log"

      if [ "$upload_attempt_rc" -eq 0 ]; then
        upload_rc=0
        break
      fi
      upload_rc="$upload_attempt_rc"

      recover_started="$(date +%s)"
      while :; do
        if qga_ping "$vm" >/dev/null 2>&1; then
          recover_now="$(date +%s)"
          recover_elapsed=$((recover_now - recover_started))
          echo "qga_recovered_after_upload_fail=1 elapsed_sec=$recover_elapsed" >> "$bundle_upload_log"
          break
        fi
        recover_now="$(date +%s)"
        recover_elapsed=$((recover_now - recover_started))
        if [ "$recover_elapsed" -ge "$qga_recover_timeout_sec" ]; then
          echo "qga_recovered_after_upload_fail=0 elapsed_sec=$recover_elapsed" >> "$bundle_upload_log"
          break
        fi
        sleep 3
      done

      if [ "$upload_attempt" -lt "$upload_max_attempts" ]; then
        echo "upload_retry_sleep_sec=$upload_retry_sleep_sec" >> "$bundle_upload_log"
        sleep "$upload_retry_sleep_sec"
      fi
      upload_attempt=$((upload_attempt + 1))
    done
  fi

  upload_end="$(date +%s)"
  upload_elapsed=$((upload_end - upload_start))
  if [ "$upload_elapsed" -le 0 ]; then
    upload_elapsed=1
  fi
  upload_rate_bps=$((upload_bytes / upload_elapsed))
  {
    echo "upload_rc=$upload_rc"
    echo "upload_attempts_used=$upload_attempts_used"
    echo "upload_elapsed_sec=$upload_elapsed"
    echo "upload_rate_bps=$upload_rate_bps"
    echo "transport_mode=$transport_mode"
    if [ "$upload_rc" -eq 124 ]; then
      echo "upload_timeout_reached=1"
    else
      echo "upload_timeout_reached=0"
    fi
  } >> "$transfer_metrics_log"

  {
    echo "transport_mode=$transport_mode"
    echo "payload_transport_mode=$payload_transport_mode"
  } > "$transport_mode_log"
  record_custom_step "${step_prefix}_transport_mode" "PASS" "0" "$transport_mode_log"

  if [ "$upload_rc" -eq 0 ] && [ "$iso_transfer_ok" -ne 1 ]; then
    record_custom_step "${step_prefix}_bundle_upload" "PASS" "0" "$bundle_upload_log"
    record_custom_step "${step_prefix}_bundle_transfer_metrics" "PASS" "0" "$transfer_metrics_log"
  elif [ "$upload_rc" -ne 0 ]; then
    record_custom_step "${step_prefix}_bundle_upload" "FAIL" "$upload_rc" "$bundle_upload_log"
    record_custom_step "${step_prefix}_bundle_transfer_metrics" "FAIL" "$upload_rc" "$transfer_metrics_log"
    record_custom_step "${step_prefix}_result" "FAIL" "$upload_rc" "$bundle_upload_log"
    write_summary_md "run-regressao" "$vm"
    return 0
  fi

  if [ "$strict_signature_enabled" = "1" ]; then
    strict_signature_arg="-StrictSignature"
  fi

  if [ -n "$regression_extra_args" ]; then
    # Escape single quotes for safe embedding in PowerShell single-quoted strings.
    regression_extra_args_escaped="${regression_extra_args//\'/\'\'}"
  fi

  regression_ps="
\$ErrorActionPreference = 'Stop'
\$base = '${guest_project_root}'
\$zip = '${guest_bundle_zip}'
if (-not (Test-Path \$zip)) { throw 'bundle_zip_not_found' }
if (Test-Path \$base) {
  Remove-Item -Path \$base -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path \$base -Force | Out-Null
\$expanded = \$false
for (\$attempt = 1; \$attempt -le 5; \$attempt++) {
  try {
    Expand-Archive -Path \$zip -DestinationPath \$base -Force
    \$expanded = \$true
    break
  } catch {
    if (\$attempt -ge 5) { throw }
    Start-Sleep -Seconds 3
  }
}
if (-not \$expanded) { throw 'expand_archive_failed' }
\$runner = Join-Path \$base 'INSTALADOR\\testes\\windows\\run-regressao.ps1'
if (-not (Test-Path \$runner)) { throw 'runner_not_found' }
\$extraArgs = '${regression_extra_args_escaped}'
\$args = @(
  '-NoProfile',
  '-ExecutionPolicy', 'Bypass',
  '-File', \$runner,
  '-ProjectRoot', \$base,
  '-RunId', '${RUN_ID}',
  '-SignatureProfile', '${signature_profile}'
)
if ('${strict_signature_arg}' -ne '') {
  \$args += '${strict_signature_arg}'
}
if (-not [string]::IsNullOrWhiteSpace(\$extraArgs)) {
  \$args += (\$extraArgs -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace(\$_) })
}
\$proc = Start-Process -FilePath 'powershell.exe' -ArgumentList \$args -Wait -PassThru
exit \$proc.ExitCode
"

  if qga_exec_powershell "$vm" "$regression_ps" "${TIMEOUT:-$DEFAULT_TIMEOUT_REGRESSAO}" "${step_prefix}_invoke_regressao"; then
    record_custom_step "${step_prefix}_invoke_regressao" "PASS" "0" "$RUN_DIR/${step_prefix}_invoke_regressao.status.log"
  else
    record_custom_step "${step_prefix}_invoke_regressao" "FAIL" "1" "$RUN_DIR/${step_prefix}_invoke_regressao.status.log"
    invoke_regression_rc=1
  fi

  index_ps="
\$ErrorActionPreference = 'Stop'
\$target = '${guest_project_root}\\INSTALADOR\\saida\\test-logs'
if (-not (Test-Path \$target)) { exit 0 }
Get-ChildItem -Path \$target -File |
  Where-Object {
    \$_.Name -match '^(regressao-windows|msi-results|inno-results|upgrade-results|artifact-verify|msi-metrics|inno-metrics|upgrade-metrics|msi-rollback-results|msi-rollback-metrics|inno-rollback-results|inno-rollback-metrics|transactional-results|transactional-metrics|power-recovery-results|power-recovery-metrics|msi-repair-results|msi-repair-metrics|defender-results|defender-metrics|ux-contract-results|ux-contract-metrics|cancel-install-results|cancel-install-metrics|time-estimate-results|time-estimate-metrics)-${RUN_ID}\\.(json|md)$'
  } |
  Sort-Object LastWriteTime -Descending |
  Select-Object -ExpandProperty FullName
"
  if qga_exec_powershell "$vm" "$index_ps" 120 "${step_prefix}_collect_index"; then
    cp "$RUN_DIR/${step_prefix}_collect_index.stdout.log" "$index_file"
    record_custom_step "${step_prefix}_collect_index" "PASS" "0" "$index_file"
  else
    record_custom_step "${step_prefix}_collect_index" "PARCIAL" "0" "$RUN_DIR/${step_prefix}_collect_index.stderr.log"
  fi

  if [ -f "$index_file" ]; then
    while IFS= read -r gpath || [ -n "$gpath" ]; do
      [ -n "$gpath" ] || continue
      gpath="${gpath%$'\r'}"
      case "$gpath" in
        *regressao-windows-*.json|*regressao-windows-*.md|*results-*.json|*metrics-*.json|*artifact-verify-*.json|*artifact-verify-*.md)
          normalized_path="${gpath//\\//}"
          base_name="$(basename "$normalized_path")"
          out_file="$RUN_DIR/${vm}-guest-$(sanitize_name "$base_name")"
          if qga_read_file "$vm" "$gpath" "$out_file" >/dev/null 2>&1; then
            record_custom_step "${step_prefix}_pull_${base_name}" "PASS" "0" "$out_file"
          else
            record_custom_step "${step_prefix}_pull_${base_name}" "PARCIAL" "0" "$out_file"
          fi
          ;;
      esac
    done < "$index_file"
  fi

  : > "$missing_artifacts_log"
  for required_name in "${required_artifacts[@]}"; do
    if ! compgen -G "$RUN_DIR/${vm}-guest-${required_name}" >/dev/null 2>&1; then
      artifact_minimum_ok=0
      echo "missing_artifact=${required_name}" >> "$missing_artifacts_log"
    fi
  done
  if [ "$artifact_minimum_ok" -eq 1 ]; then
    record_custom_step "${step_prefix}_artifact_set" "PASS" "0" "$index_file"
  else
    record_custom_step "${step_prefix}_artifact_set" "FAIL" "1" "$missing_artifacts_log"
  fi

  if [ "$invoke_regression_rc" -eq 0 ] && [ "$artifact_minimum_ok" -eq 1 ]; then
    record_custom_step "${step_prefix}_result" "PASS" "0" "$RUN_DIR/${step_prefix}_invoke_regressao.status.log"
  else
    if [ "$invoke_regression_rc" -ne 0 ]; then
      record_custom_step "${step_prefix}_result" "FAIL" "1" "$RUN_DIR/${step_prefix}_invoke_regressao.status.log"
    else
      record_custom_step "${step_prefix}_result" "FAIL" "1" "$missing_artifacts_log"
    fi
  fi
  write_summary_md "run-regressao" "$vm"
  return 0
}

main() {
  require_cmd sg
  require_cmd virsh
  require_cmd python3
  require_cmd rg
  require_cmd zip
  require_cmd xorriso

  local command="${1:-}"
  if [ -z "$command" ] || [ "$command" = "-h" ] || [ "$command" = "--help" ]; then
    usage
    exit 0
  fi
  shift

  while [ $# -gt 0 ]; do
    case "$1" in
      --vm)
        VM="${2:-}"
        shift 2
        ;;
      --run-id)
        RUN_ID="${2:-}"
        shift 2
        ;;
      --timeout)
        TIMEOUT="${2:-}"
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "ERROR: unknown argument: $1" >&2
        usage
        exit 1
        ;;
    esac
  done

  require_vm "$VM"
  init_run

  case "$command" in
    bootstrap-qga)
      if [ -z "${TIMEOUT:-}" ]; then
        TIMEOUT="$DEFAULT_TIMEOUT_BOOTSTRAP"
      fi
      command_bootstrap_qga "$VM"
      ;;
    run-regressao)
      if [ -z "${TIMEOUT:-}" ]; then
        TIMEOUT="$DEFAULT_TIMEOUT_REGRESSAO"
      fi
      command_run_regressao "$VM"
      ;;
    *)
      echo "ERROR: invalid command: $command" >&2
      usage
      exit 1
      ;;
  esac

  echo "RUN_ID: $RUN_ID"
  echo "RUN_DIR: $RUN_DIR"
  echo "CSV: $CSV_FILE"
  echo "SUMMARY: $SUMMARY_MD"
}

main "$@"
