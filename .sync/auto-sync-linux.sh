#!/usr/bin/env bash
# Auto-sync Linux: monitora alteracoes e faz push/pull automatico com seguranca.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${REPO:-$(cd "$SCRIPT_DIR/.." && pwd)}"
EXPECTED_BRANCH="${EXPECTED_BRANCH:-colega-dev}"
STATE_DIR_NAME="goyas-systems-auto-sync"
STATE_ROOT="${XDG_STATE_HOME:-$HOME/.local/state}"
LOG_DIR="${LOG_DIR:-$STATE_ROOT/$STATE_DIR_NAME/$EXPECTED_BRANCH}"
LOG="${LOG:-$LOG_DIR/sync.log}"
DEBOUNCE="${DEBOUNCE:-5}"
LAST_BRANCH_WARNING=""

# shellcheck source=/dev/null
source "$SCRIPT_DIR/lib-auto-sync-linux.sh"

log() {
    mkdir -p "$(dirname "$LOG")"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG"
}

current_branch() {
    git branch --show-current
}

ensure_expected_branch() {
    local branch
    branch="$(current_branch)"

    if [[ "$branch" == "$EXPECTED_BRANCH" ]]; then
        LAST_BRANCH_WARNING=""
        return 0
    fi

    if [[ "$LAST_BRANCH_WARNING" != "$branch" ]]; then
        log "Branch atual '$branch' nao eh '$EXPECTED_BRANCH'; auto-sync pausado"
        LAST_BRANCH_WARNING="$branch"
    fi

    return 1
}

has_relevant_changes() {
    [[ -n "$(list_relevant_changes)" ]]
}

stage_relevant_changes() {
    mapfile -t paths < <(list_relevant_changes)
    if [[ "${#paths[@]}" -eq 0 ]]; then
        return 1
    fi

    git add -A -- "${paths[@]}"
}

remote_branch_exists() {
    git show-ref --verify --quiet "refs/remotes/origin/$EXPECTED_BRANCH"
}

ensure_upstream() {
    if ! ensure_expected_branch; then
        return 1
    fi

    local desired="origin/$EXPECTED_BRANCH"
    local upstream=""

    upstream="$(git rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>/dev/null || true)"
    if [[ "$upstream" == "$desired" ]]; then
        return 0
    fi

    if ! remote_branch_exists; then
        return 1
    fi

    git branch --set-upstream-to="$desired" "$EXPECTED_BRANCH" >> "$LOG" 2>&1
    log "Upstream ajustado para $desired"
}

pull_branch_if_clean() {
    if ! ensure_expected_branch; then
        return 1
    fi

    if ! remote_branch_exists; then
        return 0
    fi

    if has_relevant_changes; then
        log "Pull adiado: ha alteracoes locais relevantes"
        return 1
    fi

    git pull --rebase origin "$EXPECTED_BRANCH" >> "$LOG" 2>&1
}

push_changes() {
    cd "$REPO" || exit 1

    if ! ensure_expected_branch; then
        return 0
    fi

    if ! has_relevant_changes; then
        return 0
    fi

    log "Alteracoes detectadas; fazendo commit e push..."

    if ! stage_relevant_changes >> "$LOG" 2>&1; then
        log "Nenhuma alteracao relevante para commit"
        return 0
    fi

    local message
    message="auto-sync: $(date '+%Y-%m-%d %H:%M:%S') [linux]"
    git commit -m "$message" >> "$LOG" 2>&1

    if remote_branch_exists; then
        ensure_upstream || return 1

        if ! pull_branch_if_clean; then
            log "Push cancelado porque o pull nao ficou seguro"
            return 1
        fi

        git push origin "$EXPECTED_BRANCH" >> "$LOG" 2>&1
    else
        git push -u origin "$EXPECTED_BRANCH" >> "$LOG" 2>&1
    fi

    log "Push concluido com sucesso"
}

pull_if_behind() {
    cd "$REPO" || exit 1

    if ! ensure_expected_branch; then
        return 0
    fi

    git fetch origin >> "$LOG" 2>&1

    if ! remote_branch_exists; then
        return 0
    fi

    ensure_upstream || return 1

    local local_head remote_head
    local_head="$(git rev-parse HEAD)"
    remote_head="$(git rev-parse "origin/$EXPECTED_BRANCH" 2>/dev/null || true)"

    if [[ "$local_head" != "$remote_head" && -n "$remote_head" ]]; then
        log "Commits novos detectados no GitHub; fazendo pull..."
        if pull_branch_if_clean; then
            log "Pull concluido"
        else
            log "Pull nao aplicado automaticamente"
        fi
    fi
}

main() {
    mkdir -p "$LOG_DIR"
    log "=== Auto-sync iniciado (Linux) ==="
    log "Monitorando: $REPO"
    log "Branch monitorada: $EXPECTED_BRANCH"
    log "Log local: $LOG"

    local ciclo=0

    pull_if_behind || true

    while true; do
        sleep "$DEBOUNCE"
        ciclo=$((ciclo + 1))

        push_changes || true

        if (( ciclo % 6 == 0 )); then
            pull_if_behind || true
        fi
    done
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
