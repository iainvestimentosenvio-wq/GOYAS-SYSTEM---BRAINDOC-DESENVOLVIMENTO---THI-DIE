#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

CHECKLIST_PATH="$INSTALADOR_ROOT/CHECKLIST_EXECUCAO_INSTALADOR_2FASES_TEMP.md"
RELEASE_APPROVAL_PATH="$INSTALADOR_ROOT/documentos/RELEASE_APPROVAL.md"
MATRIZ_PATH="$INSTALADOR_ROOT/documentos/MATRIZ_EVIDENCIAS_FINAL.md"
RISCOS_PATH="$INSTALADOR_ROOT/documentos/RISCOS_PENDENCIAS_FINAL.md"
RESULTADO_PATH="$INSTALADOR_ROOT/saida/RESULTADO-FINAL.txt"
OUTPUT_PATH=""
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"

usage() {
  cat <<'EOF'
Uso:
  bash comum/scripts/check-go-nogo.sh [opcoes]

Opcoes:
  --checklist <path>         (default: CHECKLIST_EXECUCAO_INSTALADOR_2FASES_TEMP.md)
  --release-approval <path>  (default: documentos/RELEASE_APPROVAL.md)
  --matriz <path>            (default: documentos/MATRIZ_EVIDENCIAS_FINAL.md)
  --riscos <path>            (default: documentos/RISCOS_PENDENCIAS_FINAL.md)
  --output <path>            (default: saida/go-nogo/go-nogo-<RUN_ID>.md)
  -h, --help                 Exibe ajuda
EOF
}

normalize_path() {
  local p="$1"
  if [[ "$p" = /* ]]; then
    printf '%s' "$p"
  else
    printf '%s/%s' "$INSTALADOR_ROOT" "$p"
  fi
}

status_of_bool() {
  local b="$1"
  if [ "$b" = "1" ]; then
    printf 'PASS'
  else
    printf 'FAIL'
  fi
}

while [ $# -gt 0 ]; do
  case "$1" in
    --checklist)
      CHECKLIST_PATH="$(normalize_path "${2:-}")"
      shift 2
      ;;
    --release-approval)
      RELEASE_APPROVAL_PATH="$(normalize_path "${2:-}")"
      shift 2
      ;;
    --matriz)
      MATRIZ_PATH="$(normalize_path "${2:-}")"
      shift 2
      ;;
    --riscos)
      RISCOS_PATH="$(normalize_path "${2:-}")"
      shift 2
      ;;
    --output)
      OUTPUT_PATH="$(normalize_path "${2:-}")"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: argumento invalido: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [ -z "$OUTPUT_PATH" ]; then
  OUTPUT_PATH="$INSTALADOR_ROOT/saida/go-nogo/go-nogo-$RUN_ID.md"
fi
mkdir -p "$(dirname "$OUTPUT_PATH")"

files_ok=1
missing_files_count=0
for req in \
  "$CHECKLIST_PATH" \
  "$RELEASE_APPROVAL_PATH" \
  "$MATRIZ_PATH" \
  "$RISCOS_PATH" \
  "$RESULTADO_PATH"; do
  if [ ! -f "$req" ]; then
    files_ok=0
    missing_files_count=$((missing_files_count + 1))
  fi
done

g1="$(sed -nE 's/^- G1:[[:space:]]+([A-Z-]+).*/\1/p' "$RESULTADO_PATH" 2>/dev/null | head -n1)"
g2="$(sed -nE 's/^- G2:[[:space:]]+([A-Z-]+).*/\1/p' "$RESULTADO_PATH" 2>/dev/null | head -n1)"
g3="$(sed -nE 's/^- G3:[[:space:]]+([A-Z-]+).*/\1/p' "$RESULTADO_PATH" 2>/dev/null | head -n1)"
g4="$(sed -nE 's/^- G4:[[:space:]]+([A-Z-]+).*/\1/p' "$RESULTADO_PATH" 2>/dev/null | head -n1)"

gates_ok=0
if [ "$g1" = "SIM" ] && [ "$g2" = "SIM" ] && [ "$g3" = "SIM" ] && [ "$g4" = "SIM" ]; then
  gates_ok=1
fi

pending_final_count="$(rg -n '\[FINAL-[0-9]+\].*(❌ PENDENTE|⚠️ PARCIAL)' "$RELEASE_APPROVAL_PATH" 2>/dev/null | wc -l | tr -d ' ')"
release_ok=0
if [ "${pending_final_count:-0}" -eq 0 ]; then
  release_ok=1
fi

critical_risk_count="$(awk '
  /^## Riscos abertos \(criticos\)/ {in_sec=1; next}
  in_sec && /^## / {in_sec=0}
  in_sec && /^- / {c++}
  END {print c+0}
' "$RISCOS_PATH" 2>/dev/null)"
risk_ok=0
if [ "${critical_risk_count:-0}" -eq 0 ]; then
  risk_ok=1
fi

score_policy_ok=0
if rg -n "Sem alteração de score global nesta rodada paralela|Sem alteracao de score global nesta rodada paralela" "$CHECKLIST_PATH" >/dev/null 2>&1; then
  score_policy_ok=1
fi

decision="NO-GO"
if [ "$files_ok" = "1" ] && [ "$gates_ok" = "1" ] && [ "$release_ok" = "1" ] && [ "$risk_ok" = "1" ]; then
  decision="GO"
fi

{
  echo "# Gate GO/NO-GO"
  echo
  echo "- RunId: \`$RUN_ID\`"
  echo "- TimestampUTC: \`$(date -u +%Y-%m-%dT%H:%M:%SZ)\`"
  echo
  echo "## Entradas"
  echo "- Checklist: \`$CHECKLIST_PATH\`"
  echo "- Release Approval: \`$RELEASE_APPROVAL_PATH\`"
  echo "- Matriz: \`$MATRIZ_PATH\`"
  echo "- Riscos: \`$RISCOS_PATH\`"
  echo "- Resultado Final: \`$RESULTADO_PATH\`"
  echo
  echo "## Criterios"
  echo "| ID | Criterio | Status | Evidencia | Detalhes |"
  echo "| --- | --- | --- | --- | --- |"
  echo "| C1 | Arquivos obrigatorios presentes | $(status_of_bool "$files_ok") | \`$CHECKLIST_PATH\`, \`$RELEASE_APPROVAL_PATH\`, \`$MATRIZ_PATH\`, \`$RISCOS_PATH\`, \`$RESULTADO_PATH\` | missing_count=$missing_files_count |"
  echo "| C2 | Gates globais G1-G4 em SIM | $(status_of_bool "$gates_ok") | \`$RESULTADO_PATH\` | G1=$g1; G2=$g2; G3=$g3; G4=$g4 |"
  echo "| C3 | Itens [FINAL] sem PENDENTE/PARCIAL | $(status_of_bool "$release_ok") | \`$RELEASE_APPROVAL_PATH\` | pending_or_partial=$pending_final_count |"
  echo "| C4 | Sem risco critico aberto | $(status_of_bool "$risk_ok") | \`$RISCOS_PATH\` | critical_risk_count=$critical_risk_count |"
  echo "| C5 | Politica de score conservadora registrada no checklist | $(status_of_bool "$score_policy_ok") | \`$CHECKLIST_PATH\` | frase_explicita=$( [ "$score_policy_ok" = "1" ] && echo yes || echo no ) |"
  echo
  echo "## Decisao final"
  echo "- Decisao final: **$decision**"
  if [ "$decision" = "NO-GO" ]; then
    echo "- Motivos:"
    [ "$files_ok" = "1" ] || echo "  - C1 falhou (arquivos obrigatorios ausentes)"
    [ "$gates_ok" = "1" ] || echo "  - C2 falhou (gates globais ainda nao estao em SIM)"
    [ "$release_ok" = "1" ] || echo "  - C3 falhou (itens [FINAL] ainda pendentes/parciais)"
    [ "$risk_ok" = "1" ] || echo "  - C4 falhou (riscos criticos abertos)"
  fi
} > "$OUTPUT_PATH"

echo "GO/NO-GO report: $OUTPUT_PATH"

if [ "$decision" = "GO" ]; then
  exit 0
fi
exit 1
