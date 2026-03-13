#!/usr/bin/env bash
# Auto-sync: monitora alterações e faz push automático para o GitHub
# Também faz pull automático quando detecta commits novos

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${REPO:-$(cd "$SCRIPT_DIR/.." && pwd)}"
LOG="${LOG:-$REPO/.sync/sync.log}"
DEBOUNCE=5  # segundos de espera após última alteração antes de commitar

# shellcheck source=/dev/null
source "$SCRIPT_DIR/lib-auto-sync-linux.sh"

log() {
    mkdir -p "$(dirname "$LOG")"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG"
}

current_branch() {
    git branch --show-current
}

has_relevant_changes() {
    [[ -n "$(list_relevant_changes)" ]]
}

stage_relevant_changes() {
    mapfile -t paths < <(list_relevant_changes)
    if [ "${#paths[@]}" -eq 0 ]; then
        return 1
    fi

    git add -A -- "${paths[@]}"
}

ensure_upstream() {
    local branch="$1"
    if git rev-parse --abbrev-ref --symbolic-full-name "@{u}" >/dev/null 2>&1; then
        return 0
    fi

    if git ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
        git branch --set-upstream-to="origin/$branch" "$branch" >> "$LOG" 2>&1
    fi
}

pull_branch_if_clean() {
    local branch="$1"
    if ! git ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
        return 0
    fi

    if has_relevant_changes; then
        log "Pull adiado: ha alteracoes locais relevantes"
        return 1
    fi

    git pull --rebase origin "$branch" >> "$LOG" 2>&1
}

push_changes() {
    cd "$REPO" || exit 1

    if ! has_relevant_changes; then
        return 0
    fi

    log "Alterações detectadas — fazendo commit e push..."
    if ! stage_relevant_changes; then
        log "Nenhuma alteracao relevante para commit"
        return 0
    fi

    MENSAGEM="auto-sync: $(date '+%Y-%m-%d %H:%M:%S') [linux]"
    git commit -m "$MENSAGEM" >> "$LOG" 2>&1

    local branch
    branch="$(current_branch)"
    ensure_upstream "$branch"

    if git ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
        git pull --rebase origin "$branch" >> "$LOG" 2>&1
        git push origin "$branch" >> "$LOG" 2>&1
    else
        git push -u origin "$branch" >> "$LOG" 2>&1
    fi

    if [ $? -eq 0 ]; then
        log "Push concluído com sucesso"
    else
        log "ERRO no push — verifique o log"
    fi
}

pull_if_behind() {
    cd "$REPO" || exit 1
    git fetch origin >> "$LOG" 2>&1
    LOCAL=$(git rev-parse HEAD)
    REMOTE=$(git rev-parse "origin/$(current_branch)" 2>/dev/null || true)

    if [ "$LOCAL" != "$REMOTE" ] && [ -n "$REMOTE" ]; then
        log "Commits novos detectados no GitHub — fazendo pull..."
        if pull_branch_if_clean "$(current_branch)"; then
            log "Pull concluído"
        else
            log "Pull nao aplicado automaticamente"
        fi
    fi
}

main() {
    log "=== Auto-sync iniciado ==="
    log "Monitorando: $REPO"

    CICLO=0

    # Loop principal — verifica alterações a cada 5 segundos e pull a cada 30s
    while true; do
        sleep "$DEBOUNCE"
        CICLO=$((CICLO + 1))

        push_changes

        if [ $((CICLO % 6)) -eq 0 ]; then
            pull_if_behind
        fi
    done
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
