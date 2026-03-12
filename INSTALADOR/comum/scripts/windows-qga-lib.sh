#!/usr/bin/env bash
set -euo pipefail

QGA_LAST_STATUS_JSON=""
QGA_LAST_EXITCODE=""
QGA_LAST_STDOUT=""
QGA_LAST_STDERR=""

qga_require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: required command not found: $1" >&2
    return 1
  }
}

qga_virsh() {
  local cmd="$*"
  if command -v timeout >/dev/null 2>&1; then
    timeout "${QGA_VIRSH_TIMEOUT_SEC:-8}" sg libvirt -c "$cmd"
  else
    sg libvirt -c "$cmd"
  fi
}

qga_ping() {
  local vm="$1"
  local payload='{"execute":"guest-ping"}'
  qga_virsh "virsh qemu-agent-command $vm '$payload'"
}

qga_guest_exec() {
  local vm="$1"
  local program="$2"
  shift 2
  local payload
  local out

  qga_require_cmd python3

  payload="$(python3 - "$program" "$@" <<'PY'
import json
import sys

program = sys.argv[1]
args = sys.argv[2:]
print(json.dumps({
    "execute": "guest-exec",
    "arguments": {
        "path": program,
        "arg": args,
        "capture-output": True
    }
}))
PY
)"

  if ! out="$(qga_virsh "virsh qemu-agent-command $vm '$payload'" 2>&1)"; then
    echo "$out" >&2
    return 1
  fi

  python3 - "$out" <<'PY'
import json
import sys

obj = json.loads(sys.argv[1])
ret = obj.get("return") or {}
pid = ret.get("pid")
if pid is None:
    raise SystemExit(1)
print(pid)
PY
}

qga_wait_exec() {
  local vm="$1"
  local pid="$2"
  local timeout_sec="${3:-600}"
  local started now elapsed
  local payload out parsed
  local exited exitcode out_b64 err_b64

  qga_require_cmd python3
  qga_require_cmd base64

  QGA_LAST_STATUS_JSON=""
  QGA_LAST_EXITCODE=""
  QGA_LAST_STDOUT=""
  QGA_LAST_STDERR=""

  started="$(date +%s)"

  while :; do
    now="$(date +%s)"
    elapsed=$((now - started))
    if [ "$elapsed" -ge "$timeout_sec" ]; then
      echo "ERROR: guest-exec timeout for pid=$pid (vm=$vm timeout=${timeout_sec}s)" >&2
      return 124
    fi

    payload="$(python3 - "$pid" <<'PY'
import json
import sys

pid = int(sys.argv[1])
print(json.dumps({
    "execute": "guest-exec-status",
    "arguments": {
        "pid": pid
    }
}))
PY
)"

    if ! out="$(qga_virsh "virsh qemu-agent-command $vm '$payload'" 2>&1)"; then
      QGA_LAST_STATUS_JSON="$out"
      # QGA may lose exec status tracking when the agent restarts.
      # Waiting until timeout in this case only stalls automation.
      if printf '%s' "$out" | grep -qiE 'guest-exec-status.*does not exist|PID .* does not exist|process does not exist'; then
        QGA_LAST_EXITCODE="PID_MISSING"
        return 125
      fi
      sleep 2
      continue
    fi

    QGA_LAST_STATUS_JSON="$out"
    parsed="$(printf '%s' "$out" | python3 -c '
import json
import sys

raw = sys.stdin.read()
obj = json.loads(raw) if raw else {}
ret = obj.get("return") or {}

exited = 1 if ret.get("exited") else 0
exitcode = ret.get("exitcode")
out_b64 = ret.get("out-data") or ""
err_b64 = ret.get("err-data") or ""
exitcode_txt = "" if exitcode is None else str(exitcode)

print("EXITED=" + str(exited))
print("EXITCODE=" + exitcode_txt)
print("OUT_B64=" + out_b64)
print("ERR_B64=" + err_b64)
')"

    exited=""
    exitcode=""
    out_b64=""
    err_b64=""

    while IFS='=' read -r key value; do
      case "$key" in
        EXITED) exited="$value" ;;
        EXITCODE) exitcode="$value" ;;
        OUT_B64) out_b64="$value" ;;
        ERR_B64) err_b64="$value" ;;
      esac
    done <<< "$parsed"

    if [ "${exited:-0}" != "1" ]; then
      sleep 2
      continue
    fi

    if [ -n "${out_b64:-}" ]; then
      QGA_LAST_STDOUT="$(printf '%s' "$out_b64" | base64 -d 2>/dev/null || true)"
    else
      QGA_LAST_STDOUT=""
    fi

    if [ -n "${err_b64:-}" ]; then
      QGA_LAST_STDERR="$(printf '%s' "$err_b64" | base64 -d 2>/dev/null || true)"
    else
      QGA_LAST_STDERR=""
    fi

    QGA_LAST_EXITCODE="${exitcode:-}"

    if [ "${exitcode:-1}" = "0" ]; then
      return 0
    fi
    return 1
  done
}

qga_read_file() {
  local vm="$1"
  local guest_path="$2"
  local out_file="$3"
  local open_payload read_payload close_payload
  local open_out read_out close_out
  local handle eof_flag buf_b64
  local parsed

  qga_require_cmd python3
  qga_require_cmd base64

  : > "$out_file"

  open_payload="$(python3 - "$guest_path" <<'PY'
import json
import sys

path = sys.argv[1]
print(json.dumps({
    "execute": "guest-file-open",
    "arguments": {
        "path": path,
        "mode": "r"
    }
}))
PY
)"

  if ! open_out="$(qga_virsh "virsh qemu-agent-command $vm '$open_payload'" 2>&1)"; then
    echo "$open_out" >&2
    return 1
  fi

  handle="$(python3 - "$open_out" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
ret = obj.get("return")
if ret is None:
    raise SystemExit(1)
print(ret)
PY
)"

  while :; do
    read_payload="$(python3 - "$handle" <<'PY'
import json
import sys

handle = int(sys.argv[1])
print(json.dumps({
    "execute": "guest-file-read",
    "arguments": {
        "handle": handle,
        "count": 4096
    }
}))
PY
)"

    if ! read_out="$(qga_virsh "virsh qemu-agent-command $vm '$read_payload'" 2>&1)"; then
      echo "$read_out" >&2
      break
    fi

    parsed="$(python3 - "$read_out" <<'PY'
import json
import sys

obj = json.loads(sys.argv[1])
ret = obj.get("return") or {}
eof = 1 if ret.get("eof") else 0
buf = ret.get("buf-b64") or ""
print(f"EOF={eof}")
print(f"BUF_B64={buf}")
PY
)"

    eof_flag="0"
    buf_b64=""
    while IFS='=' read -r key value; do
      case "$key" in
        EOF) eof_flag="$value" ;;
        BUF_B64) buf_b64="$value" ;;
      esac
    done <<< "$parsed"

    if [ -n "$buf_b64" ]; then
      printf '%s' "$buf_b64" | base64 -d >> "$out_file" 2>/dev/null || true
    fi

    if [ "$eof_flag" = "1" ]; then
      break
    fi
  done

  close_payload="$(python3 - "$handle" <<'PY'
import json
import sys

handle = int(sys.argv[1])
print(json.dumps({
    "execute": "guest-file-close",
    "arguments": {
        "handle": handle
    }
}))
PY
)"

  close_out="$(qga_virsh "virsh qemu-agent-command $vm '$close_payload'" 2>&1 || true)"
  if [ -n "$close_out" ]; then
    :
  fi

  return 0
}

qga_write_file() {
  local vm="$1"
  local host_file="$2"
  local guest_path="$3"
  local open_payload write_payload close_payload
  local open_out write_out close_out
  local handle chunk_b64
  local chunk_size progress_every_mb

  qga_require_cmd python3

  [ -f "$host_file" ] || {
    echo "ERROR: host file not found: $host_file" >&2
    return 1
  }

  chunk_size="${QGA_WRITE_CHUNK_SIZE:-8192}"
  progress_every_mb="${QGA_WRITE_PROGRESS_EVERY_MB:-0}"

  if ! [[ "$chunk_size" =~ ^[0-9]+$ ]] || [ "$chunk_size" -le 0 ]; then
    echo "WARN: invalid QGA_WRITE_CHUNK_SIZE='$chunk_size'; using 8192" >&2
    chunk_size=8192
  fi
  if ! [[ "$progress_every_mb" =~ ^[0-9]+$ ]]; then
    echo "WARN: invalid QGA_WRITE_PROGRESS_EVERY_MB='$progress_every_mb'; using 0" >&2
    progress_every_mb=0
  fi

  open_payload="$(python3 - "$guest_path" <<'PY'
import json
import sys

path = sys.argv[1]
print(json.dumps({
    "execute": "guest-file-open",
    "arguments": {
        "path": path,
        "mode": "wb"
    }
}))
PY
)"

  if ! open_out="$(qga_virsh "virsh qemu-agent-command $vm '$open_payload'" 2>&1)"; then
    echo "$open_out" >&2
    return 1
  fi

  handle="$(python3 - "$open_out" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
ret = obj.get("return")
if ret is None:
    raise SystemExit(1)
print(ret)
PY
)"

  while IFS= read -r chunk_b64; do
    write_payload="$(python3 - "$handle" "$chunk_b64" <<'PY'
import json
import sys

handle = int(sys.argv[1])
chunk = sys.argv[2]
print(json.dumps({
    "execute": "guest-file-write",
    "arguments": {
        "handle": handle,
        "buf-b64": chunk
    }
}))
PY
)"

    if ! write_out="$(qga_virsh "virsh qemu-agent-command $vm '$write_payload'" 2>&1)"; then
      echo "$write_out" >&2
      close_payload="$(python3 - "$handle" <<'PY'
import json
import sys
print(json.dumps({
    "execute": "guest-file-close",
    "arguments": {"handle": int(sys.argv[1])}
}))
PY
)"
      qga_virsh "virsh qemu-agent-command $vm '$close_payload'" >/dev/null 2>&1 || true
      return 1
    fi
  done < <(QGA_WRITE_CHUNK_SIZE="$chunk_size" QGA_WRITE_PROGRESS_EVERY_MB="$progress_every_mb" python3 - "$host_file" <<'PY'
import base64
import os
import sys

path = sys.argv[1]
chunk_size = int(os.environ.get("QGA_WRITE_CHUNK_SIZE", "8192"))
progress_every_mb = int(os.environ.get("QGA_WRITE_PROGRESS_EVERY_MB", "0"))
progress_every_bytes = progress_every_mb * 1024 * 1024
file_size = os.path.getsize(path)
written = 0
next_progress = progress_every_bytes if progress_every_bytes > 0 else 0

with open(path, "rb") as f:
    while True:
        chunk = f.read(chunk_size)
        if not chunk:
            break
        written += len(chunk)
        print(base64.b64encode(chunk).decode("ascii"))
        if progress_every_bytes > 0 and written >= next_progress:
            pct = (written * 100.0 / file_size) if file_size > 0 else 100.0
            print(
                f"qga_write_file_progress bytes={written}/{file_size} pct={pct:.1f}",
                file=sys.stderr,
                flush=True,
            )
            next_progress += progress_every_bytes

if progress_every_bytes > 0:
    print(
        f"qga_write_file_progress bytes={written}/{file_size} pct=100.0 done=1",
        file=sys.stderr,
        flush=True,
    )
PY
)

  close_payload="$(python3 - "$handle" <<'PY'
import json
import sys

handle = int(sys.argv[1])
print(json.dumps({
    "execute": "guest-file-close",
    "arguments": {
        "handle": handle
    }
}))
PY
)"

  if ! close_out="$(qga_virsh "virsh qemu-agent-command $vm '$close_payload'" 2>&1)"; then
    echo "$close_out" >&2
    return 1
  fi

  return 0
}
