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

ULTIMO_EVENTO=0

# Loop principal
while true; do
    # Aguarda alteração em qualquer arquivo (exceto .git e .sync)
    inotifywait -r -e modify,create,delete,move \
        --exclude '\.git|\.sync|bin/|obj/|\.trx$|TestResults' \
        "$REPO" -q --format '%f' 2>/dev/null &
    INOTIFY_PID=$!

    # Também faz pull periódico a cada 30 segundos
    sleep 30 &
    SLEEP_PID=$!

    wait -n $INOTIFY_PID $SLEEP_PID 2>/dev/null

    # Se foi o inotify que disparou
    if ! kill -0 $INOTIFY_PID 2>/dev/null; then
        kill $SLEEP_PID 2>/dev/null
        # Debounce — espera acumular alterações
        sleep $DEBOUNCE
        push_changes
    else
        # Foi o sleep — faz pull
        kill $INOTIFY_PID 2>/dev/null
        pull_if_behind
    fi
done
