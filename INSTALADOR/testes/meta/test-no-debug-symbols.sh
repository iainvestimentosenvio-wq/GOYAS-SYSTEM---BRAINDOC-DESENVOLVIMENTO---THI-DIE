#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
INSTALADOR_ROOT="$PROJECT_ROOT/INSTALADOR"

scan_dirs=(
  "$INSTALADOR_ROOT/saida/appimage"
  "$INSTALADOR_ROOT/saida/deb"
  "$INSTALADOR_ROOT/saida/deb/extract"
  "$INSTALADOR_ROOT/saida/entrega-final"
)

existing_dirs=()
for dir in "${scan_dirs[@]}"; do
  [ -d "$dir" ] && existing_dirs+=("$dir")
done

if [ "${#existing_dirs[@]}" -eq 0 ]; then
  echo "PASS: nenhum diretorio de entrega Linux encontrado para varredura"
  exit 0
fi

violations_file="$(mktemp)"
trap 'rm -f "$violations_file"' EXIT

for dir in "${existing_dirs[@]}"; do
  find "$dir" -type f -iname '*.pdb' >> "$violations_file"
done

# Inspecao adicional de conteudo interno dos .deb finais.
mapfile -t deb_files < <(find "$INSTALADOR_ROOT/saida/deb" "$INSTALADOR_ROOT/saida/entrega-final" -maxdepth 1 -type f -name '*.deb' 2>/dev/null | sort || true)

if [ "${#deb_files[@]}" -gt 0 ]; then
  if ! command -v dpkg-deb >/dev/null 2>&1; then
    echo "FAIL: .deb encontrado, mas dpkg-deb nao esta disponivel para inspecao interna" >&2
    exit 1
  fi

  for deb in "${deb_files[@]}"; do
    if dpkg-deb -c "$deb" 2>/dev/null | awk '{print $6}' | rg -i '\.pdb$' >/dev/null; then
      echo "$deb::internal-pdb" >> "$violations_file"
    fi
  done
fi

if [ -s "$violations_file" ]; then
  echo "FAIL: simbolos de debug (.pdb) encontrados em entrega Linux:" >&2
  cat "$violations_file" >&2
  exit 1
fi

echo "PASS: nenhum .pdb encontrado nos diretorios de entrega Linux"
