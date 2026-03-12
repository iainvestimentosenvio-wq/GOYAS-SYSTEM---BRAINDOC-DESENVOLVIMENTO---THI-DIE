#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
OUT_DIR="$PROJECT_ROOT/INSTALADOR/saida/host-audit"
TS="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_FILE="$OUT_DIR/${TS}.md"
DEFAULT_ISO_SEARCH_DIRS="${HOME}/Downloads"
if [ -n "${PROTONS_SHARED_ROOT:-}" ]; then
  DEFAULT_ISO_SEARCH_DIRS="${DEFAULT_ISO_SEARCH_DIRS},${PROTONS_SHARED_ROOT}"
fi
ISO_SEARCH_DIRS_CSV="${ISO_SEARCH_DIRS:-$DEFAULT_ISO_SEARCH_DIRS}"
mapfile -t ISO_SEARCH_DIRS_ARR < <(printf '%s\n' "$ISO_SEARCH_DIRS_CSV" | tr ',' '\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | awk 'NF>0')
if [ "${#ISO_SEARCH_DIRS_ARR[@]}" -eq 0 ]; then
  ISO_SEARCH_DIRS_ARR=("${HOME}/Downloads")
fi
ISO_FIND_ARGS="$(printf '%q ' "${ISO_SEARCH_DIRS_ARR[@]}")"

mkdir -p "$OUT_DIR"

capture_cmd() {
  local label="$1"
  shift
  local tmp_file
  tmp_file="$(mktemp)"

  if "$@" >"$tmp_file" 2>&1; then
    echo "OK::$label::$(cat "$tmp_file")"
  else
    local rc=$?
    echo "ERR::$label::rc=$rc::$(cat "$tmp_file")"
  fi

  rm -f "$tmp_file"
}

is_x64_iso_name() {
  local file_name_lc="$1"
  if [[ "$file_name_lc" != *.iso ]]; then
    return 1
  fi
  if [[ "$file_name_lc" =~ (arm64|aarch64|arm) ]]; then
    return 1
  fi
  if [[ "$file_name_lc" =~ (x64|amd64|x86_64) ]]; then
    return 0
  fi
  return 1
}

find_latest_iso() {
  local flavor="$1"
  local dir=""
  local latest_any=""
  local latest_x64=""
  local candidate_line=""
  local candidate_path=""
  local candidate_name_lc=""
  local find_expr=()

  case "$flavor" in
    win10)
      find_expr=(-iname '*win*10*.iso' -o -iname '*windows*10*.iso')
      ;;
    win11)
      find_expr=(-iname '*win*11*.iso' -o -iname '*windows*11*.iso')
      ;;
    *)
      return 1
      ;;
  esac

  for dir in "${ISO_SEARCH_DIRS_ARR[@]}"; do
    [ -d "$dir" ] || continue
    while IFS= read -r candidate_line; do
      candidate_path="${candidate_line#* }"
      [ -n "$candidate_path" ] || continue
      if [ -z "$latest_any" ]; then
        latest_any="$candidate_path"
      fi
      candidate_name_lc="$(basename "$candidate_path" | tr '[:upper:]' '[:lower:]')"
      if is_x64_iso_name "$candidate_name_lc"; then
        latest_x64="$candidate_path"
        break
      fi
    done < <(find "$dir" -maxdepth 6 -type f \( "${find_expr[@]}" \) -printf '%T@ %p\n' 2>/dev/null | sort -nr)

    if [ -n "$latest_x64" ]; then
      break
    fi
  done

  if [ -n "$latest_x64" ]; then
    printf '%s' "$latest_x64"
    return 0
  fi
  if [ -n "$latest_any" ]; then
    printf '%s' "$latest_any"
    return 0
  fi

  return 1
}

LIBVIRTD_STATE="$(systemctl is-active libvirtd 2>/dev/null || true)"
[ -n "$LIBVIRTD_STATE" ] || LIBVIRTD_STATE="unknown"

VIRTQEMUD_STATE="$(systemctl is-active virtqemud 2>/dev/null || true)"
[ -n "$VIRTQEMUD_STATE" ] || VIRTQEMUD_STATE="unknown"

LIBVIRTD_SOCKET_STATE="$(systemctl is-active libvirtd.socket 2>/dev/null || true)"
[ -n "$LIBVIRTD_SOCKET_STATE" ] || LIBVIRTD_SOCKET_STATE="unknown"

VIRTQEMUD_SOCKET_STATE="$(systemctl is-active virtqemud.socket 2>/dev/null || true)"
[ -n "$VIRTQEMUD_SOCKET_STATE" ] || VIRTQEMUD_SOCKET_STATE="unknown"

if sudo -n true >/dev/null 2>&1; then
  SUDO_STATE="OK"
  SUDO_RC="0"
else
  SUDO_RC="$?"
  SUDO_STATE="BLOCKED_INTERACTIVE_PASSWORD"
fi

if virsh list --all >/tmp/protons_virsh.out 2>/tmp/protons_virsh.err; then
  VIRSH_MODE="direct"
  VIRSH_OUTPUT="$(cat /tmp/protons_virsh.out)"
elif sg libvirt -c 'virsh list --all' >/tmp/protons_virsh.out 2>/tmp/protons_virsh.err; then
  VIRSH_MODE="sg_libvirt"
  VIRSH_OUTPUT="$(cat /tmp/protons_virsh.out)"
else
  VIRSH_MODE="blocked"
  VIRSH_OUTPUT="$(cat /tmp/protons_virsh.err)"
fi
rm -f /tmp/protons_virsh.out /tmp/protons_virsh.err

LIBVIRTD_STATE_AFTER="$(systemctl is-active libvirtd 2>/dev/null || true)"
[ -n "$LIBVIRTD_STATE_AFTER" ] || LIBVIRTD_STATE_AFTER="unknown"
LIBVIRTD_SOCKET_STATE_AFTER="$(systemctl is-active libvirtd.socket 2>/dev/null || true)"
[ -n "$LIBVIRTD_SOCKET_STATE_AFTER" ] || LIBVIRTD_SOCKET_STATE_AFTER="unknown"

ISO_WIN10_PATH="$(find_latest_iso win10 || true)"
ISO_WIN11_PATH="$(find_latest_iso win11 || true)"
if [ -n "$ISO_WIN10_PATH" ] && [ -f "$ISO_WIN10_PATH" ]; then
  ISO_WIN10_STATE="present"
else
  ISO_WIN10_STATE="missing"
  ISO_WIN10_PATH=""
fi
if [ -n "$ISO_WIN11_PATH" ] && [ -f "$ISO_WIN11_PATH" ]; then
  ISO_WIN11_STATE="present"
else
  ISO_WIN11_STATE="missing"
  ISO_WIN11_PATH=""
fi

VMX_COUNT="$(grep -E -c '(vmx|svm)' /proc/cpuinfo 2>/dev/null || true)"
if [ -e /dev/kvm ]; then
  KVM_DEV="present"
else
  KVM_DEV="missing"
fi

{
  echo "# Host Audit Snapshot"
  echo
  echo "- timestamp_utc: \`$(date -u +%Y-%m-%dT%H:%M:%SZ)\`"
  echo "- host_user: \`$(whoami)\`"
  echo "- script: \`comum/scripts/audit-host-state.sh\`"
  echo "- iso_search_dirs: \`$ISO_SEARCH_DIRS_CSV\`"
  echo
  echo "## Runtime"
  echo "- groups_session: \`$(id -nG)\`"
  echo "- vmx_svm_count: \`${VMX_COUNT:-0}\`"
  echo "- /dev/kvm: \`$KVM_DEV\`"
  echo
  echo "## Libvirt"
  echo "- libvirtd.service (pre-virsh): \`$LIBVIRTD_STATE\`"
  echo "- libvirtd.socket (pre-virsh): \`$LIBVIRTD_SOCKET_STATE\`"
  echo "- libvirtd.service (post-virsh): \`$LIBVIRTD_STATE_AFTER\`"
  echo "- libvirtd.socket (post-virsh): \`$LIBVIRTD_SOCKET_STATE_AFTER\`"
  echo "- virtqemud.service: \`$VIRTQEMUD_STATE\`"
  echo "- virtqemud.socket: \`$VIRTQEMUD_SOCKET_STATE\`"
  echo "- virsh_access_mode: \`$VIRSH_MODE\`"
  echo
  echo "### virsh list --all"
  echo '```text'
  if [ -n "$VIRSH_OUTPUT" ]; then
    printf '%s\n' "$VIRSH_OUTPUT"
  else
    echo "(no output)"
  fi
  echo '```'
  echo
  echo "## Privilege"
  echo "- sudo_non_interactive: \`$SUDO_STATE\`"
  echo "- sudo_n_true_exit_code: \`$SUDO_RC\`"
  echo
  echo "## ISO Dependency"
  echo "- win10_iso_state: \`$ISO_WIN10_STATE\`"
  if [ -n "$ISO_WIN10_PATH" ]; then
    echo "- win10_iso_path: \`$ISO_WIN10_PATH\`"
  else
    echo "- win10_iso_path: \`(not found)\`"
  fi
  echo "- win11_iso_state: \`$ISO_WIN11_STATE\`"
  if [ -n "$ISO_WIN11_PATH" ]; then
    echo "- win11_iso_path: \`$ISO_WIN11_PATH\`"
  else
    echo "- win11_iso_path: \`(not found)\`"
  fi
  echo
  echo "## Raw Checks"
  echo '```text'
  capture_cmd "systemctl is-active libvirtd" systemctl is-active libvirtd
  capture_cmd "systemctl is-active libvirtd.socket" systemctl is-active libvirtd.socket
  capture_cmd "id -nG" id -nG
  if [ -n "$ISO_WIN10_PATH" ]; then
    capture_cmd "ls -l $ISO_WIN10_PATH" ls -l "$ISO_WIN10_PATH"
  else
    capture_cmd "find win10 iso" bash -lc "find ${ISO_FIND_ARGS} -maxdepth 6 -type f \\( -iname '*win*10*.iso' -o -iname '*windows*10*.iso' \\) 2>/dev/null"
  fi
  if [ -n "$ISO_WIN11_PATH" ]; then
    capture_cmd "ls -l $ISO_WIN11_PATH" ls -l "$ISO_WIN11_PATH"
  else
    capture_cmd "find win11 iso" bash -lc "find ${ISO_FIND_ARGS} -maxdepth 6 -type f \\( -iname '*win*11*.iso' -o -iname '*windows*11*.iso' \\) 2>/dev/null"
  fi
  echo '```'
} > "$OUT_FILE"

echo "HOST_AUDIT_FILE: $OUT_FILE"
