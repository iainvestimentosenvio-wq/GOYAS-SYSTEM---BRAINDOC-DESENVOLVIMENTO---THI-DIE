#!/usr/bin/env bash
# run-windows-ux-p10-gate.sh
# Runner semantico de gate estendido para ciclo UX + P10.

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
POWERSHELL_RESOLVER="$INSTALADOR_ROOT/comum/scripts/powershell-resolver.sh"
[ -f "$POWERSHELL_RESOLVER" ] || { echo "ERRO: helper ausente: $POWERSHELL_RESOLVER" >&2; exit 1; }
source "$POWERSHELL_RESOLVER"

cd "$INSTALADOR_ROOT"

virsh_domstate() {
  local vm="$1"
  sg libvirt -c "virsh domstate $vm" 2>/dev/null | tr -d '\r'
}

usage() {
  cat <<'EOF'
Uso:
  bash run-windows-ux-p10-gate.sh [opcoes]

Opcoes:
  -h, --help                    Exibe ajuda e sai

Variaveis de ambiente:
  QGA_PAYLOAD_TRANSPORT_MODE    iso-strict|iso|qga|auto (default: iso-strict)
  PROTONS_SIGNATURE_PROFILE     technical|production (default: technical)
EOF
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

echo "==============================================================="
echo "GATE UX + P10 - Regressao Estendida"
echo "==============================================================="
echo ""

echo "Preflight: validando frescor dos artefatos..."
if ! bash testes/windows/test-artifact-freshness.sh; then
  PS_CMD="$(resolve_powershell_cmd)"
  echo ""
  echo "ERRO: preflight de artefatos falhou. Rebuild obrigatorio antes de rodar o gate UX+P10."
  echo "Comandos de rebuild:"
  echo "  $PS_CMD -ExecutionPolicy Bypass -File windows/scripts/build-msi.ps1"
  echo "  $PS_CMD -ExecutionPolicy Bypass -File windows/scripts/build-inno.ps1"
  echo "  Ou fluxo automatico Linux->VM:"
  echo "  bash build-and-run-windows-gate.sh"
  exit 1
fi
echo ""

export QGA_WRITE_CHUNK_SIZE="${QGA_WRITE_CHUNK_SIZE:-8192}"
export QGA_WRITE_PROGRESS_EVERY_MB="${QGA_WRITE_PROGRESS_EVERY_MB:-5}"
export QGA_VIRSH_TIMEOUT_SEC="${QGA_VIRSH_TIMEOUT_SEC:-15}"
export QGA_UPLOAD_MAX_ATTEMPTS="${QGA_UPLOAD_MAX_ATTEMPTS:-3}"
export QGA_UPLOAD_RETRY_SLEEP_SEC="${QGA_UPLOAD_RETRY_SLEEP_SEC:-10}"
export QGA_PAYLOAD_TRANSPORT_MODE="${QGA_PAYLOAD_TRANSPORT_MODE:-iso-strict}"
export PROTONS_REGRESSION_EXTRA_ARGS="-EnableResilienceSuite -EnableUxSuite -RequireDefenderActive"
export PROTONS_SIGNATURE_PROFILE="${PROTONS_SIGNATURE_PROFILE:-technical}"

if ! [[ "$QGA_WRITE_CHUNK_SIZE" =~ ^[0-9]+$ ]]; then
  echo "WARN: QGA_WRITE_CHUNK_SIZE invalido ('$QGA_WRITE_CHUNK_SIZE'). Usando 8192."
  export QGA_WRITE_CHUNK_SIZE=8192
elif [ "$QGA_WRITE_CHUNK_SIZE" -gt 16384 ]; then
  echo "WARN: QGA_WRITE_CHUNK_SIZE=$QGA_WRITE_CHUNK_SIZE pode gerar instabilidade no QGA. Ajustando para 8192 (seguro)."
  export QGA_WRITE_CHUNK_SIZE=8192
elif [ "$QGA_WRITE_CHUNK_SIZE" -lt 4096 ]; then
  echo "WARN: QGA_WRITE_CHUNK_SIZE=$QGA_WRITE_CHUNK_SIZE pode deixar upload muito lento. Recomendado: 8192."
fi

echo "Parametros de regressao estendida:"
echo "  - PROTONS_REGRESSION_EXTRA_ARGS=$PROTONS_REGRESSION_EXTRA_ARGS"
echo "  - QGA_WRITE_CHUNK_SIZE=$QGA_WRITE_CHUNK_SIZE"
echo "  - QGA_WRITE_PROGRESS_EVERY_MB=$QGA_WRITE_PROGRESS_EVERY_MB"
echo "  - QGA_UPLOAD_MAX_ATTEMPTS=$QGA_UPLOAD_MAX_ATTEMPTS"
echo "  - QGA_UPLOAD_RETRY_SLEEP_SEC=$QGA_UPLOAD_RETRY_SLEEP_SEC"
echo "  - QGA_PAYLOAD_TRANSPORT_MODE=$QGA_PAYLOAD_TRANSPORT_MODE"
echo "  - PROTONS_SIGNATURE_PROFILE=$PROTONS_SIGNATURE_PROFILE"
echo ""

for vm in win10-lite win11-lite; do
  state="$(virsh_domstate "$vm" || true)"
  [ -n "$state" ] || state="undefined"
  echo "VM $vm: $state"
done
echo ""

RUN_ID="UXP10-GATE-$(date -u +%Y%m%d%H%M%S)"
echo "RunId: $RUN_ID"
echo ""

bash comum/scripts/windows-e2e-sequencial.sh \
  --run-id "$RUN_ID" \
  --vm-order "win10-lite,win11-lite" \
  --cooldown-sec 20 \
  --signature-profile "$PROTONS_SIGNATURE_PROFILE" \
  --bootstrap-mode manual-ready \
  2>&1 | tee "saida/validacao-windows-$RUN_ID.log"

EXIT_CODE=${PIPESTATUS[0]:-$?}

echo ""
echo "Finalizado. Exit code: $EXIT_CODE"
echo "Log: saida/validacao-windows-$RUN_ID.log"
echo "Artefatos: saida/validacao-windows-$RUN_ID/"

exit "$EXIT_CODE"
