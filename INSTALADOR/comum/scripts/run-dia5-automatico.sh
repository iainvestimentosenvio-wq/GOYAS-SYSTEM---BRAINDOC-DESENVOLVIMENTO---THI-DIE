#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALADOR_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
QGA_LIB="$SCRIPT_DIR/windows-qga-lib.sh"
source "$QGA_LIB"

RUN_ID="${1:-DIA5-$(date -u +%Y%m%dT%H%M%SZ)}"
OUT_DIR="$INSTALADOR_ROOT/saida/dia5-$RUN_ID"
HTTP_ROOT="$OUT_DIR/http-root"
HTTP_PORT="${DIA5_HTTP_PORT:-18080}"
MSI_SRC="$INSTALADOR_ROOT/saida/windows/Protons-1.0.0-x64.msi"
EXE_SRC="$INSTALADOR_ROOT/saida/windows/ProtonsSetup-1.0.0.exe"
MSI_UPDATE_NAME="Protons-1.0.1-x64.msi"
EXE_UPDATE_NAME="ProtonsSetup-1.0.1.exe"
VM_LIST=(win10-lite win11-lite)

mkdir -p "$OUT_DIR" "$HTTP_ROOT/windows"

if [ ! -f "$MSI_SRC" ]; then
  echo "ERROR: MSI não encontrado: $MSI_SRC" >&2
  exit 1
fi
if [ ! -f "$EXE_SRC" ]; then
  echo "ERROR: EXE não encontrado: $EXE_SRC" >&2
  exit 1
fi

cp -f "$MSI_SRC" "$HTTP_ROOT/windows/$MSI_UPDATE_NAME"
cp -f "$EXE_SRC" "$HTTP_ROOT/windows/$EXE_UPDATE_NAME"

MSI_SHA="$(sha256sum "$HTTP_ROOT/windows/$MSI_UPDATE_NAME" | awk '{print tolower($1)}')"
EXE_SHA="$(sha256sum "$HTTP_ROOT/windows/$EXE_UPDATE_NAME" | awk '{print tolower($1)}')"

HOST_IP="$(ip -4 addr show virbr0 2>/dev/null | awk '/inet /{print $2}' | cut -d/ -f1 | head -1)"
if [ -z "${HOST_IP:-}" ]; then
  HOST_IP="192.168.122.1"
fi

MANIFEST_PATH="$OUT_DIR/update-manifest-dia5.json"
cat > "$MANIFEST_PATH" <<EOF
{
  "version": "1.0.1",
  "channel": "stable",
  "published_at_utc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "min_supported_version": "1.0.0",
  "release_notes_url": "http://$HOST_IP:$HTTP_PORT/release-notes/v1.0.1",
  "rollout_percent": 100,
  "artifacts": [
    {
      "platform": "windows",
      "type": "msi",
      "url": "http://$HOST_IP:$HTTP_PORT/windows/$MSI_UPDATE_NAME",
      "sha256": "$MSI_SHA",
      "size_bytes": $(stat -c '%s' "$HTTP_ROOT/windows/$MSI_UPDATE_NAME"),
      "signature": "UNSIGNED"
    },
    {
      "platform": "windows",
      "type": "exe",
      "url": "http://$HOST_IP:$HTTP_PORT/windows/$EXE_UPDATE_NAME",
      "sha256": "$EXE_SHA",
      "size_bytes": $(stat -c '%s' "$HTTP_ROOT/windows/$EXE_UPDATE_NAME"),
      "signature": "UNSIGNED"
    }
  ]
}
EOF

HTTP_LOG="$OUT_DIR/http-server.log"
python3 -m http.server "$HTTP_PORT" --directory "$HTTP_ROOT" >"$HTTP_LOG" 2>&1 &
HTTP_PID=$!
echo "$HTTP_PID" > "$OUT_DIR/http-server.pid"

cleanup() {
  if [ -n "${HTTP_PID:-}" ] && kill -0 "$HTTP_PID" >/dev/null 2>&1; then
    kill "$HTTP_PID" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

sleep 1
if ! kill -0 "$HTTP_PID" >/dev/null 2>&1; then
  echo "ERROR: HTTP server não iniciou." >&2
  exit 1
fi

json_escape() {
  python3 - <<'PY' "$1"
import json, sys
print(json.dumps(sys.argv[1]))
PY
}

encode_ps() {
  python3 - <<'PY' "$1"
import base64, sys
print(base64.b64encode(sys.argv[1].encode("utf-16le")).decode("ascii"))
PY
}

run_ps_in_vm() {
  local vm="$1"
  local ps_script="$2"
  local timeout_sec="${3:-3600}"
  local encoded pid try
  encoded="$(encode_ps "$ps_script")"

  for try in $(seq 1 8); do
    if pid="$(qga_guest_exec "$vm" "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" "-NoProfile" "-ExecutionPolicy" "Bypass" "-EncodedCommand" "$encoded" 2>"$OUT_DIR/${vm}-guest-exec.err")"; then
      if qga_wait_exec "$vm" "$pid" "$timeout_sec"; then
        return 0
      else
        return 1
      fi
    fi
    sleep 2
  done
  return 1
}

ensure_vm_running_and_qga() {
  local vm="$1"
  local state
  state="$(virsh domstate "$vm" 2>/dev/null || true)"
  if [[ "$state" != *running* ]]; then
    virsh start "$vm" >/dev/null
  fi

  local i
  for i in $(seq 1 90); do
    if qga_ping "$vm" >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done
  return 1
}

for vm in "${VM_LIST[@]}"; do
  echo "== DIA5: executando em $vm =="
  if ! ensure_vm_running_and_qga "$vm"; then
    echo "ERROR: QGA indisponível em $vm" | tee "$OUT_DIR/$vm.status"
    continue
  fi

  msi_url="http://$HOST_IP:$HTTP_PORT/windows/$MSI_UPDATE_NAME"
  expected_msi_sha="$MSI_SHA"
  run_tag="$RUN_ID"

  ps_script="
\$ErrorActionPreference = 'Stop'
\$runTag = $(json_escape "$run_tag")
\$msiUrl = $(json_escape "$msi_url")
\$expectedSha = $(json_escape "$expected_msi_sha")
\$work = 'C:\\Windows\\Temp\\dia5-' + \$runTag
New-Item -ItemType Directory -Path \$work -Force | Out-Null
\$msiPath = Join-Path \$work 'update.msi'
\$installLog = Join-Path \$work 'install.log'
\$reinstallLog = Join-Path \$work 'reinstall.log'
\$upgradeLog = Join-Path \$work 'upgrade.log'
\$uninstallLog = Join-Path \$work 'uninstall.log'
\$resultJson = Join-Path \$work 'dia5-result.json'

Invoke-WebRequest -UseBasicParsing -Uri \$msiUrl -OutFile \$msiPath
\$hash = (Get-FileHash -Algorithm SHA256 -Path \$msiPath).Hash.ToLowerInvariant()
if (\$hash -ne \$expectedSha) {
  throw ('hash_mismatch expected=' + \$expectedSha + ' actual=' + \$hash)
}

\$installArgs = @('/i', \$msiPath, '/qn', '/norestart', '/L*v', \$installLog)
\$install = Start-Process -FilePath 'msiexec.exe' -ArgumentList \$installArgs -Wait -PassThru
if (\$install.ExitCode -ne 0) { throw ('install_failed:' + \$install.ExitCode) }

\$appData = Join-Path \$env:APPDATA 'Protons'
New-Item -ItemType Directory -Path \$appData -Force | Out-Null
\$sentinel = Join-Path \$appData ('dia5-sentinel-' + \$runTag + '.txt')
\$sentinelContent = 'DIA5:' + \$runTag
\$sentinelContent | Set-Content -Path \$sentinel -Encoding ASCII

\$reinstallArgs = @('/i', \$msiPath, '/qn', '/norestart', '/L*v', \$reinstallLog)
\$reinstall = Start-Process -FilePath 'msiexec.exe' -ArgumentList \$reinstallArgs -Wait -PassThru
if (\$reinstall.ExitCode -ne 0) { throw ('reinstall_failed:' + \$reinstall.ExitCode) }
\$afterReinstall = Test-Path \$sentinel
\$afterReinstallContent = if (\$afterReinstall) { \$sentinelContent } else { '' }

\$upgradeMsi = Join-Path \$work 'update-1.0.1.msi'
Copy-Item -Path \$msiPath -Destination \$upgradeMsi -Force
\$upgradeArgs = @('/i', \$upgradeMsi, '/qn', '/norestart', '/L*v', \$upgradeLog)
\$upgrade = Start-Process -FilePath 'msiexec.exe' -ArgumentList \$upgradeArgs -Wait -PassThru
if (\$upgrade.ExitCode -ne 0) { throw ('upgrade_failed:' + \$upgrade.ExitCode) }
\$afterUpgrade = Test-Path \$sentinel
\$afterUpgradeContent = if (\$afterUpgrade) { \$sentinelContent } else { '' }

\$uninstallArgs = @('/x', \$msiPath, '/qn', '/norestart', '/L*v', \$uninstallLog)
\$uninstall = Start-Process -FilePath 'msiexec.exe' -ArgumentList \$uninstallArgs -Wait -PassThru
if (\$uninstall.ExitCode -ne 0) { throw ('uninstall_failed:' + \$uninstall.ExitCode) }
\$afterUninstall = Test-Path \$sentinel
\$status = if (\$afterReinstall -and \$afterUpgrade) { 'PASS' } else { 'FAIL' }

\$result = [ordered]@{
  vm = $(json_escape "$vm")
  run_id = \$runTag
  msi_url = \$msiUrl
  expected_sha256 = \$expectedSha
  actual_sha256 = \$hash
  install_exit = \$install.ExitCode
  reinstall_exit = \$reinstall.ExitCode
  upgrade_exit = \$upgrade.ExitCode
  uninstall_exit = \$uninstall.ExitCode
  sentinel_path = \$sentinel
  sentinel_expected = \$sentinelContent
  sentinel_after_reinstall = \$afterReinstall
  sentinel_content_after_reinstall = \$afterReinstallContent
  sentinel_after_upgrade = \$afterUpgrade
  sentinel_content_after_upgrade = \$afterUpgradeContent
  sentinel_after_uninstall = \$afterUninstall
  status = \$status
}
  \$result | ConvertTo-Json -Depth 6 | Set-Content -Path \$resultJson -Encoding UTF8
  Write-Output ('RESULT_JSON=' + \$resultJson)
"

  result_guest_path="C:\\Windows\\Temp\\dia5-$run_tag\\dia5-result.json"
  run_ok=1
  if run_ps_in_vm "$vm" "$ps_script" 5400; then
    printf '%s\n' "$QGA_LAST_STDOUT" > "$OUT_DIR/$vm.stdout.log"
    printf '%s\n' "$QGA_LAST_STDERR" > "$OUT_DIR/$vm.stderr.log"
  else
    printf '%s\n' "$QGA_LAST_STDOUT" > "$OUT_DIR/$vm.stdout.log"
    printf '%s\n' "$QGA_LAST_STDERR" > "$OUT_DIR/$vm.stderr.log"
    run_ok=0
    echo "WARN: execução PowerShell reportou falha em $vm; tentando coletar JSON final por caminho fixo." | tee -a "$OUT_DIR/$vm.status"
  fi

  pulled=0
  for _ in $(seq 1 60); do
    if qga_read_file "$vm" "$result_guest_path" "$OUT_DIR/$vm.result.json" >/dev/null 2>&1; then
      pulled=1
      break
    fi
    sleep 3
  done

  if [ "$pulled" -eq 1 ]; then
    vm_status="$(python3 - "$OUT_DIR/$vm.result.json" <<'PY'
import json
import sys
try:
    with open(sys.argv[1], 'r', encoding='utf-8-sig') as f:
        obj = json.load(f)
    print((obj.get('status') or 'FAIL').strip().upper())
except Exception:
    print('FAIL')
PY
)"
    if [ "$run_ok" -eq 0 ] && [ "$vm_status" = "PASS" ]; then
      echo "WARN+PASS" > "$OUT_DIR/$vm.status"
    else
      echo "$vm_status" > "$OUT_DIR/$vm.status"
    fi
  else
    echo "FAIL: não foi possível puxar resultado JSON de $vm em $result_guest_path" | tee "$OUT_DIR/$vm.status"
  fi
done

SUMMARY_MD="$OUT_DIR/summary.md"
{
  echo "# DIA 5 - Execucao Automatica"
  echo
  echo "- RunId: \`$RUN_ID\`"
  echo "- Host: \`$HOST_IP:$HTTP_PORT\`"
  echo "- Manifest: \`$MANIFEST_PATH\`"
  echo
  for vm in "${VM_LIST[@]}"; do
    status="$(cat "$OUT_DIR/$vm.status" 2>/dev/null || echo "FAIL")"
    echo "## $vm"
    echo "- Status: \`$status\`"
    if [ -f "$OUT_DIR/$vm.result.json" ]; then
      echo "- Resultado: \`$OUT_DIR/$vm.result.json\`"
    else
      echo "- Resultado: não disponível"
    fi
    echo
  done
} > "$SUMMARY_MD"

echo "DIA5 concluído: $OUT_DIR"
echo "Resumo: $SUMMARY_MD"
