#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
CHECKLIST_FILE="$PROJECT_ROOT/INSTALADOR/CHECKLIST.md"

[ -f "$CHECKLIST_FILE" ] || {
  echo "FAIL: checklist file not found: $CHECKLIST_FILE" >&2
  exit 1
}

count_hashes() {
  local bar="$1"
  local only_hash
  only_hash="${bar//[^#]/}"
  echo "${#only_hash}"
}

expected_hashes() {
  local score="$1"
  echo $(((score + 2) / 5))
}

assert_bar_compatible() {
  local label="$1"
  local score="$2"
  local bar="$3"
  local actual_hashes expected diff

  actual_hashes="$(count_hashes "$bar")"
  expected="$(expected_hashes "$score")"
  diff=$(( actual_hashes - expected ))
  if [ "$diff" -lt 0 ]; then
    diff=$(( -diff ))
  fi

  if [ "${#bar}" -ne 20 ]; then
    echo "FAIL: $label bar length must be 20, got ${#bar} ($bar)" >&2
    return 1
  fi

  if [ "$diff" -gt 1 ]; then
    echo "FAIL: $label bar incompatible with score=$score (hashes=$actual_hashes expected~$expected)" >&2
    return 1
  fi

  return 0
}

declare -A rank_from rank_to rank_dist rank_bar_current rank_bar_top
declare -A panel_score panel_bar_current panel_bar_top

section=""
current_rank_idx=""
current_panel_idx=""

while IFS= read -r line; do
  if [[ "$line" == "#### Ranking de prioridade"* ]]; then
    section="ranking"
    continue
  fi
  if [[ "$line" == "#### Painel visual por pilar"* ]]; then
    section="panel"
    continue
  fi

  if [[ "$section" == "ranking" ]]; then
    if [[ "$line" =~ ^([0-9]+)\.\  ]]; then
      idx="${BASH_REMATCH[1]}"
      parsed="$(sed -nE 's/.*`([0-9]+) -> ([0-9]+)`, distancia `([0-9]+)`.*/\1 \2 \3/p' <<< "$line")"
      if [ -n "$parsed" ]; then
        read -r from to dist <<< "$parsed"
        rank_from["$idx"]="$from"
        rank_to["$idx"]="$to"
        rank_dist["$idx"]="$dist"
        current_rank_idx="$idx"
      fi
      continue
    fi

    if [ -n "$current_rank_idx" ]; then
      parsed_current="$(sed -nE 's/^Atual \(`([0-9]+)`\): `\[([#.]+)\]`/\1 \2/p' <<< "$line")"
      if [ -n "$parsed_current" ]; then
        read -r current_score current_bar <<< "$parsed_current"
        if [ "${rank_from[$current_rank_idx]:-}" != "$current_score" ]; then
          echo "FAIL: ranking $current_rank_idx current score mismatch (title=${rank_from[$current_rank_idx]:-NA}, line=$current_score)" >&2
          exit 1
        fi
        rank_bar_current["$current_rank_idx"]="$current_bar"
        continue
      fi

      parsed_top="$(sed -nE 's/^Meta.*\(`([0-9]+)`\): `\[([#.]+)\]`/\1 \2/p' <<< "$line")"
      if [ -n "$parsed_top" ]; then
        read -r top_score top_bar <<< "$parsed_top"
        if [ "${rank_to[$current_rank_idx]:-}" != "$top_score" ]; then
          echo "FAIL: ranking $current_rank_idx top score mismatch (title=${rank_to[$current_rank_idx]:-NA}, line=$top_score)" >&2
          exit 1
        fi
        rank_bar_top["$current_rank_idx"]="$top_bar"
        continue
      fi
    fi
  fi

  if [[ "$section" == "panel" ]]; then
    parsed_header="$(sed -nE 's/^([0-9]+)\..*\(`([0-9]+)\/100`.*$/\1 \2/p' <<< "$line")"
    if [ -n "$parsed_header" ]; then
      read -r idx score <<< "$parsed_header"
      panel_score["$idx"]="$score"
      current_panel_idx="$idx"
      continue
    fi

    if [ -n "$current_panel_idx" ]; then
      parsed_current="$(sed -nE 's/^Atual: `\[([#.]+)\]`/\1/p' <<< "$line")"
      if [ -n "$parsed_current" ]; then
        panel_bar_current["$current_panel_idx"]="$parsed_current"
        continue
      fi

      parsed_top="$(sed -nE 's/^Topo : `\[([#.]+)\]`/\1/p' <<< "$line")"
      if [ -n "$parsed_top" ]; then
        panel_bar_top["$current_panel_idx"]="$parsed_top"
        continue
      fi
    fi
  fi
done < "$CHECKLIST_FILE"

for idx in "${!rank_from[@]}"; do
  from="${rank_from[$idx]}"
  to="${rank_to[$idx]}"
  dist="${rank_dist[$idx]}"
  expected_dist=$((to - from))

  if [ "$dist" -ne "$expected_dist" ]; then
    echo "FAIL: ranking $idx distance mismatch (have=$dist expected=$expected_dist)" >&2
    exit 1
  fi

  assert_bar_compatible "ranking[$idx].Atual" "$from" "${rank_bar_current[$idx]}"
  assert_bar_compatible "ranking[$idx].Meta" "$to" "${rank_bar_top[$idx]}"

done

for idx in "${!panel_score[@]}"; do
  if [ "${panel_score[$idx]}" != "${rank_from[$idx]:-}" ]; then
    echo "FAIL: panel $idx score mismatch with ranking (panel=${panel_score[$idx]} ranking=${rank_from[$idx]:-NA})" >&2
    exit 1
  fi

  assert_bar_compatible "panel[$idx].Atual" "${panel_score[$idx]}" "${panel_bar_current[$idx]}"
  assert_bar_compatible "panel[$idx].Topo" "${rank_to[$idx]}" "${panel_bar_top[$idx]}"

  if [ "${panel_bar_current[$idx]}" != "${rank_bar_current[$idx]}" ]; then
    echo "FAIL: panel $idx current bar differs from ranking" >&2
    exit 1
  fi

done

echo "PASS: checklist ranking/panel consistency validated"
