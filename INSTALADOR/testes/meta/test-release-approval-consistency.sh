#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"
DOC="$INSTALADOR_ROOT/documentos/RELEASE_APPROVAL.md"

[ -f "$DOC" ] || {
  echo "FAIL: release approval nao encontrado: $DOC" >&2
  exit 1
}

# Nao deve haver itens alem de FINAL-15.
if rg -n '\[FINAL-(1[6-9]|[2-9][0-9]+)\]' "$DOC" >/dev/null; then
  echo "FAIL: encontrado item [FINAL-16+] nao permitido" >&2
  exit 1
fi

# Deve haver exatamente um item para FINAL-01..FINAL-15.
for n in $(seq -w 1 15); do
  count="$(rg -n "^\| \[FINAL-$n\] \|" "$DOC" | wc -l | tr -d ' ')"
  if [ "$count" -ne 1 ]; then
    echo "FAIL: esperado 1 ocorrencia de [FINAL-$n], encontrado $count" >&2
    exit 1
  fi
done

# Status permitidos na tabela FINAL.
while IFS= read -r line; do
  status="$(echo "$line" | awk -F'|' '{print $4}' | sed -E 's/^[[:space:]]+|[[:space:]]+$//g')"
  case "$status" in
    *CONCLUIDO*|*BLOQUEADO*|*PENDENTE*|*PARCIAL*) ;;
    *)
      echo "FAIL: status invalido na tabela FINAL: '$status'" >&2
      exit 1
      ;;
  esac
done < <(rg -n '^\| \[FINAL-[0-9]+\] \|' "$DOC" | sed -E 's/^[0-9]+://')

# FINAL-15 deve refletir workflows reais do instalador.
final15_line="$(rg -n '^\| \[FINAL-15\] \|' "$DOC" | sed -E 's/^[0-9]+://')"
echo "$final15_line" | rg -q 'instalador-ci\.yml' || {
  echo "FAIL: [FINAL-15] nao referencia instalador-ci.yml" >&2
  exit 1
}
echo "$final15_line" | rg -q 'instalador-release\.yml' || {
  echo "FAIL: [FINAL-15] nao referencia instalador-release.yml" >&2
  exit 1
}

# Validar contagem declarada no resumo.
decl_total="$(sed -nE 's/^\*\*Total de itens:\*\*[[:space:]]*([0-9]+).*/\1/p' "$DOC" | head -n1)"
decl_concl="$(sed -nE 's/^\*\*Concluidos:\*\*[[:space:]]*([0-9]+).*/\1/p' "$DOC" | head -n1)"
decl_bloq="$(sed -nE 's/^\*\*Bloqueados:\*\*[[:space:]]*([0-9]+).*/\1/p' "$DOC" | head -n1)"
decl_pend="$(sed -nE 's/^\*\*Pendentes:\*\*[[:space:]]*([0-9]+).*/\1/p' "$DOC" | head -n1)"
decl_parc="$(sed -nE 's/^\*\*Parciais:\*\*[[:space:]]*([0-9]+).*/\1/p' "$DOC" | head -n1)"

calc_total="$({ rg -n '^\| \[FINAL-[0-9]+\] \|' "$DOC" || true; } | wc -l | tr -d ' ')"
calc_concl="$({ rg -n '^\| \[FINAL-[0-9]+\] \|.*CONCLUIDO' "$DOC" || true; } | wc -l | tr -d ' ')"
calc_bloq="$({ rg -n '^\| \[FINAL-[0-9]+\] \|.*BLOQUEADO' "$DOC" || true; } | wc -l | tr -d ' ')"
calc_pend="$({ rg -n '^\| \[FINAL-[0-9]+\] \|.*PENDENTE' "$DOC" || true; } | wc -l | tr -d ' ')"
calc_parc="$({ rg -n '^\| \[FINAL-[0-9]+\] \|.*PARCIAL' "$DOC" || true; } | wc -l | tr -d ' ')"

[ "$decl_total" = "$calc_total" ] || { echo "FAIL: resumo total inconsistente (decl=$decl_total calc=$calc_total)" >&2; exit 1; }
[ "$decl_concl" = "$calc_concl" ] || { echo "FAIL: resumo concluidos inconsistente (decl=$decl_concl calc=$calc_concl)" >&2; exit 1; }
[ "$decl_bloq" = "$calc_bloq" ] || { echo "FAIL: resumo bloqueados inconsistente (decl=$decl_bloq calc=$calc_bloq)" >&2; exit 1; }
[ "$decl_pend" = "$calc_pend" ] || { echo "FAIL: resumo pendentes inconsistente (decl=$decl_pend calc=$calc_pend)" >&2; exit 1; }
[ "$decl_parc" = "$calc_parc" ] || { echo "FAIL: resumo parciais inconsistente (decl=$decl_parc calc=$calc_parc)" >&2; exit 1; }

echo "PASS: release approval consistente (FINAL-01..FINAL-15)"
