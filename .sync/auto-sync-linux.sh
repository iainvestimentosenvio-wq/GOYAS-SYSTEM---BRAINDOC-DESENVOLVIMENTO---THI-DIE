#!/bin/bash
# Auto-sync: monitora alterações e faz push automático para o GitHub
# Também faz pull automático quando detecta commits novos

REPO="/home/u/Documentos/GOYAS SYSTEMS"
LOG="/home/u/Documentos/GOYAS SYSTEMS/.sync/sync.log"
DEBOUNCE=5  # segundos de espera após última alteração antes de commitar

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG"
}

push_changes() {
    cd "$REPO" || exit 1

    # Verifica se há alterações
    if git diff --quiet && git diff --cached --quiet && [ -z "$(git ls-files --others --exclude-standard)" ]; then
        return 0
    fi

    log "Alterações detectadas — fazendo commit e push..."
    git add -A
    MENSAGEM="auto-sync: $(date '+%Y-%m-%d %H:%M:%S') [linux]"
    git commit -m "$MENSAGEM" >> "$LOG" 2>&1

    # Pull antes do push para evitar conflitos
    git pull --rebase origin "$(git branch --show-current)" >> "$LOG" 2>&1
    git push origin "$(git branch --show-current)" >> "$LOG" 2>&1

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
    REMOTE=$(git rev-parse "origin/$(git branch --show-current)" 2>/dev/null)

    if [ "$LOCAL" != "$REMOTE" ] && [ -n "$REMOTE" ]; then
        log "Commits novos detectados no GitHub — fazendo pull..."
        git pull --rebase origin "$(git branch --show-current)" >> "$LOG" 2>&1
        log "Pull concluído"
    fi
}

log "=== Auto-sync iniciado ==="
log "Monitorando: $REPO"

CICLO=0

# Loop principal — verifica alterações a cada 5 segundos e pull a cada 30s
while true; do
    sleep $DEBOUNCE
    CICLO=$((CICLO + 1))

    # Tenta push se houver alterações
    push_changes

    # Pull a cada 6 ciclos (~30 segundos)
    if [ $((CICLO % 6)) -eq 0 ]; then
        pull_if_behind
    fi
done
