#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Uso:
  backup_restore_painel.sh backup  <caminho_db> [diretorio_backups]
  backup_restore_painel.sh restore <arquivo_backup> <caminho_db>
  backup_restore_painel.sh status  <caminho_db>
EOF
}

if [[ $# -lt 2 ]]; then
  usage
  exit 1
fi

modo="$1"
case "$modo" in
  backup)
    db_path="$2"
    backup_dir="${3:-$(dirname "$db_path")/backups}"
    mkdir -p "$backup_dir"
    if [[ ! -f "$db_path" ]]; then
      echo "[erro] banco nao encontrado: $db_path" >&2
      exit 1
    fi
    stamp="$(date +%Y%m%d_%H%M%S)"
    backup_file="$backup_dir/protons_painel_${stamp}.db"
    cp "$db_path" "$backup_file"
    echo "[ok] backup criado: $backup_file"
    ;;

  restore)
    if [[ $# -lt 3 ]]; then
      usage
      exit 1
    fi
    backup_file="$2"
    db_path="$3"
    if [[ ! -f "$backup_file" ]]; then
      echo "[erro] arquivo de backup nao encontrado: $backup_file" >&2
      exit 1
    fi
    mkdir -p "$(dirname "$db_path")"
    cp "$backup_file" "$db_path"
    echo "[ok] restore concluido em: $db_path"
    ;;

  status)
    db_path="$2"
    if [[ -f "$db_path" ]]; then
      size_bytes="$(wc -c <"$db_path" | tr -d ' ')"
      modified="$(date -r "$db_path" '+%Y-%m-%d %H:%M:%S')"
      echo "[ok] banco encontrado"
      echo "  arquivo: $db_path"
      echo "  tamanho_bytes: $size_bytes"
      echo "  atualizado_em: $modified"
    else
      echo "[erro] banco nao encontrado: $db_path" >&2
      exit 1
    fi
    ;;

  *)
    usage
    exit 1
    ;;
esac
