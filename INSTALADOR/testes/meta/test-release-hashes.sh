#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

artifact_dirs=(
  "$INSTALADOR_ROOT/saida/windows"
  "$INSTALADOR_ROOT/saida/appimage"
  "$INSTALADOR_ROOT/saida/deb"
  "$INSTALADOR_ROOT/saida/entrega-final"
)

declare -a artifacts=()
declare -A seen=()

for dir in "${artifact_dirs[@]}"; do
  [ -d "$dir" ] || continue
  while IFS= read -r -d '' file; do
    [ -n "${seen[$file]:-}" ] && continue
    seen["$file"]=1
    artifacts+=("$file")
  done < <(find "$dir" -maxdepth 1 -type f \( -name '*.msi' -o -name '*.exe' -o -name '*.AppImage' -o -name '*.deb' \) -print0)
done

if [ "${#artifacts[@]}" -eq 0 ]; then
  echo "FAIL: nenhum artefato oficial encontrado para validar hash" >&2
  exit 1
fi

fail=0
checked=0

for artifact in "${artifacts[@]}"; do
  sha_file="${artifact}.sha256"
  checked=$((checked + 1))

  if [ ! -f "$sha_file" ]; then
    echo "FAIL: arquivo de hash ausente: $sha_file" >&2
    fail=1
    continue
  fi

  expected="$(head -n1 "$sha_file" | tr -d '\r' | awk '{print $1}')"
  if ! [[ "$expected" =~ ^[a-fA-F0-9]{64}$ ]]; then
    echo "FAIL: hash com formato invalido em $sha_file" >&2
    fail=1
    continue
  fi

  actual="$(sha256sum "$artifact" | awk '{print $1}')"
  if [ "${expected,,}" != "${actual,,}" ]; then
    echo "FAIL: hash divergente para $artifact" >&2
    echo "  expected: $expected" >&2
    echo "  actual:   $actual" >&2
    fail=1
    continue
  fi

  echo "PASS: $artifact"
done

if [ "$fail" -ne 0 ]; then
  echo "FAIL: validacao de hashes concluiu com erros (itens=$checked)" >&2
  exit 1
fi

echo "PASS: hashes validados com sucesso (itens=$checked)"
