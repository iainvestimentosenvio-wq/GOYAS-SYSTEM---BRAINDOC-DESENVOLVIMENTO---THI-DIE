#!/usr/bin/env bash
# run-windows-base-gate.sh
# Runner semantico do gate base Windows (tecnico).

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
  bash run-windows-base-gate.sh [opcoes]

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
echo "WINDOWS BASE GATE - Regressao Base"
echo "==============================================================="
echo ""

export QGA_WRITE_CHUNK_SIZE="${QGA_WRITE_CHUNK_SIZE:-65536}"
export QGA_WRITE_PROGRESS_EVERY_MB="${QGA_WRITE_PROGRESS_EVERY_MB:-10}"
export QGA_VIRSH_TIMEOUT_SEC="${QGA_VIRSH_TIMEOUT_SEC:-15}"
export QGA_PAYLOAD_TRANSPORT_MODE="${QGA_PAYLOAD_TRANSPORT_MODE:-iso-strict}"
export PROTONS_SIGNATURE_PROFILE="${PROTONS_SIGNATURE_PROFILE:-technical}"

echo "Parametros de execucao:"
echo "  - QGA_WRITE_CHUNK_SIZE=$QGA_WRITE_CHUNK_SIZE"
echo "  - QGA_WRITE_PROGRESS_EVERY_MB=$QGA_WRITE_PROGRESS_EVERY_MB"
echo "  - QGA_PAYLOAD_TRANSPORT_MODE=$QGA_PAYLOAD_TRANSPORT_MODE"
echo "  - PROTONS_SIGNATURE_PROFILE=$PROTONS_SIGNATURE_PROFILE"
echo ""

# Preflight de artefatos antes da rodada longa
echo "0. Preflight de artefatos..."
if ! bash testes/windows/test-artifact-freshness.sh; then
  PS_CMD="$(resolve_powershell_cmd)"
  echo ""
  echo "ERRO: artefatos desatualizados ou ausentes."
  echo "Rebuild obrigatorio antes do gate base:"
  echo "  $PS_CMD -ExecutionPolicy Bypass -File windows/scripts/build-msi.ps1"
  echo "  $PS_CMD -ExecutionPolicy Bypass -File windows/scripts/build-inno.ps1"
  echo "  Ou fluxo automatico Linux->VM:"
  echo "  bash build-and-run-windows-gate.sh"
  exit 1
fi
echo ""

# Verificar estado das VMs
echo "1. Verificando estado das VMs..."
for vm in win10-lite win11-lite; do
  state="$(virsh_domstate "$vm" || true)"
  [ -n "$state" ] || state="undefined"
  echo "   $vm: $state"
done
echo ""

# Criar RUN_ID
RUN_ID="BASE-GATE-$(date -u +%Y%m%d%H%M%S)"
echo "2. RUN_ID: $RUN_ID"
echo ""

# Confirmar execucao
echo "3. Pronto para executar gate base?"
echo ""
echo "   Parametros:"
echo "   - VM order: win10-lite → win11-lite"
echo "   - Cooldown: 20s entre VMs"
echo "   - Mode: manual-ready (QGA ja instalado)"
echo "   - Signature profile: $PROTONS_SIGNATURE_PROFILE"
echo "   - Payload transport: $QGA_PAYLOAD_TRANSPORT_MODE"
echo ""

if [ -t 0 ]; then
  read -r -p "   Continuar? [s/N]: " confirm
else
  confirm="s"
  echo "   Shell nao interativo detectado: continuando automaticamente."
fi
if [[ ! "$confirm" =~ ^[sS]$ ]]; then
  echo "Cancelado pelo usuario."
  exit 0
fi

echo ""
echo "4. Executando gate base..."
echo "   (Duracao estimada: 60-90 minutos automaticos)"
echo "   Logs em: saida/validacao-windows-$RUN_ID/"
echo ""

# Executar rodada
bash comum/scripts/windows-e2e-sequencial.sh \
  --run-id "$RUN_ID" \
  --vm-order "win10-lite,win11-lite" \
  --cooldown-sec 20 \
  --signature-profile "$PROTONS_SIGNATURE_PROFILE" \
  --bootstrap-mode manual-ready \
  2>&1 | tee "saida/validacao-windows-$RUN_ID.log"

EXIT_CODE=${PIPESTATUS[0]:-$?}

echo ""
echo "==============================================================="
echo "WINDOWS BASE GATE - FINALIZADO"
echo "==============================================================="
echo ""
echo "Exit code: $EXIT_CODE"
echo ""

# Mostrar resumo
if [ -f "saida/validacao-windows-$RUN_ID/resumo.csv" ]; then
  echo "5. Resumo dos resultados:"
  echo ""

  PASS_COUNT=$(grep -c ",PASS," "saida/validacao-windows-$RUN_ID/resumo.csv" || echo "0")
  FAIL_COUNT=$(grep -c ",FAIL," "saida/validacao-windows-$RUN_ID/resumo.csv" || echo "0")
  PARCIAL_COUNT=$(grep -c ",PARCIAL," "saida/validacao-windows-$RUN_ID/resumo.csv" || echo "0")

  echo "   PASS: $PASS_COUNT"
  echo "   FAIL: $FAIL_COUNT"
  echo "   PARCIAL: $PARCIAL_COUNT"
  echo ""

  # Verificar se upload passou
  if grep -q "run_regressao.*bundle_upload,PASS" "saida/validacao-windows-$RUN_ID/resumo.csv"; then
    echo "   SUCESSO: upload de bundle concluido."
    echo ""
    echo "   Proximo passo:"
    echo "   1. Verificar resultados: cat saida/validacao-windows-$RUN_ID/gates-summary.md"
  else
    echo "   Upload ainda falhou. Possibilidades:"
    echo ""
    echo "   1. QGA desconectou durante upload (reiniciar VM e repetir)"
    echo "   2. Chunk 64KB ainda muito lento (tentar 128KB ou 256KB)"
    echo "   3. Usar fallback de diagnostico: bash troubleshoot-upload.sh"
    echo ""
    echo "   Para diagnostico detalhado:"
    echo "   cat saida/validacao-windows-$RUN_ID/run_regressao_win10-lite_bundle_upload.log"
    echo "   cat saida/validacao-windows-$RUN_ID/run_regressao_win11-lite_bundle_upload.log"
  fi
else
  echo "AVISO: Arquivo resumo.csv nao encontrado."
  echo "Verifique o log completo: saida/validacao-windows-$RUN_ID.log"
fi

echo ""
echo "Log completo: saida/validacao-windows-$RUN_ID.log"
echo "Artefatos: saida/validacao-windows-$RUN_ID/"
echo ""

exit "$EXIT_CODE"
