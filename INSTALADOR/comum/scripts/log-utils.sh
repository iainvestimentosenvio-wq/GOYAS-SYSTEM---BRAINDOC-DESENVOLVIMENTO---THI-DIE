#!/bin/bash
# =============================================================================
# log-utils.sh - Utilitarios de log padronizados para scripts de build
# =============================================================================
# OB-01: Padronizar logs de build com formato consistente
#
# Uso:
#   source log-utils.sh
#   log_init "build-appimage"
#   log INFO "Iniciando build"
#   log STEP "Publicando aplicacao"
#   log OK "Concluido"
#   log_finish
#
# Formato: [YYYY-MM-DD HH:MM:SS] [NIVEL] Mensagem
# =============================================================================

# Variaveis globais
LOG_FILE=""
LOG_START_TIME=""
LOG_SCRIPT_NAME=""

# Inicializar sistema de log
log_init() {
    local script_name="${1:-build}"
    LOG_SCRIPT_NAME="$script_name"
    LOG_START_TIME=$SECONDS

    # Criar diretorio de logs se necessario
    local log_dir="${LOG_DIR:-$PROJECT_ROOT/INSTALADOR/saida/logs}"
    mkdir -p "$log_dir"

    # Nome do arquivo de log com timestamp
    local timestamp="$(date '+%Y%m%d_%H%M%S')"
    LOG_FILE="$log_dir/${script_name}_${timestamp}.log"

    # Iniciar log
    {
        echo "=========================================="
        echo "BUILD LOG - $script_name"
        echo "Inicio: $(date '+%Y-%m-%d %H:%M:%S')"
        echo "Host: $(hostname)"
        echo "Usuario: $(whoami)"
        echo "=========================================="
        echo ""
    } > "$LOG_FILE"

    log INFO "Log iniciado: $LOG_FILE"
}

# Funcao principal de log
log() {
    local level="$1"
    shift
    local message="$*"
    local timestamp="$(date '+%Y-%m-%d %H:%M:%S')"
    local prefix=""
    local color=""

    case "$level" in
        INFO)  prefix="[INFO]"; color="\033[0;37m" ;;      # Cinza
        STEP)  prefix="[STEP]"; color="\033[0;36m" ;;      # Ciano
        OK)    prefix="[ OK ]"; color="\033[0;32m" ;;      # Verde
        WARN)  prefix="[WARN]"; color="\033[0;33m" ;;      # Amarelo
        ERROR) prefix="[ERRO]"; color="\033[0;31m" ;;      # Vermelho
        *)     prefix="[????]"; color="\033[0m" ;;
    esac

    local reset="\033[0m"
    local log_line="[$timestamp] $prefix $message"

    # Exibir no console com cor
    echo -e "${color}${log_line}${reset}"

    # Gravar no arquivo de log (sem codigos de cor)
    if [ -n "$LOG_FILE" ] && [ -f "$LOG_FILE" ]; then
        echo "$log_line" >> "$LOG_FILE"
    fi
}

# Registrar duracao de uma etapa
# Uso: step_start "Publish"; ... ; step_end "Publish" -> retorna duracao em segundos
declare -A STEP_TIMES

step_start() {
    local step_name="$1"
    STEP_TIMES["${step_name}_start"]=$SECONDS
    log STEP "Iniciando: $step_name"
}

step_end() {
    local step_name="$1"
    local start_time="${STEP_TIMES[${step_name}_start]:-$SECONDS}"
    local duration=$((SECONDS - start_time))
    STEP_TIMES["${step_name}_duration"]=$duration
    log OK "$step_name concluido em ${duration}s"
    echo "$duration"
}

# Obter duracao de uma etapa
get_duration() {
    local step_name="$1"
    echo "${STEP_TIMES[${step_name}_duration]:-0}"
}

# Gerar checksum SHA256
generate_checksum() {
    local file="$1"
    local checksum_file="${file}.sha256"

    if [ ! -f "$file" ]; then
        log WARN "Arquivo nao encontrado para checksum: $file"
        return 1
    fi

    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$file" > "$checksum_file"
        local hash=$(cut -d' ' -f1 "$checksum_file")
        log OK "Checksum SHA256 gerado: $checksum_file"
        log INFO "  $hash"
        echo "$hash"
    else
        log WARN "sha256sum nao disponivel - checksum nao gerado"
        return 1
    fi
}

# Finalizar log com resumo
log_finish() {
    local status="${1:-SUCCESS}"
    local artifact="${2:-}"
    local artifact_size="${3:-}"
    local checksum="${4:-}"

    local total_duration=$((SECONDS - LOG_START_TIME))

    echo ""
    log INFO "=========================================="

    if [ "$status" = "SUCCESS" ]; then
        log OK "BUILD CONCLUIDO - $LOG_SCRIPT_NAME"
    else
        log ERROR "BUILD FALHOU - $LOG_SCRIPT_NAME"
    fi

    log INFO "=========================================="

    if [ -n "$artifact" ]; then
        log INFO "Artefato:  $artifact"
    fi
    if [ -n "$artifact_size" ]; then
        log INFO "Tamanho:   $artifact_size"
    fi
    if [ -n "$checksum" ]; then
        log INFO "SHA256:    $checksum"
    fi

    log INFO "------------------------------------------"
    log INFO "Duracoes:"

    # Listar todas as duracoes registradas
    for key in "${!STEP_TIMES[@]}"; do
        if [[ "$key" == *"_duration" ]]; then
            local step_name="${key%_duration}"
            local duration="${STEP_TIMES[$key]}"
            log INFO "  $step_name: ${duration}s"
        fi
    done

    log INFO "  Total: ${total_duration}s"
    log INFO "------------------------------------------"
    log INFO "Log: $LOG_FILE"
    log INFO "=========================================="

    # Gravar resumo no arquivo de log
    if [ -n "$LOG_FILE" ] && [ -f "$LOG_FILE" ]; then
        {
            echo ""
            echo "=========================================="
            echo "RESUMO FINAL"
            echo "=========================================="
            echo "Status: $status"
            echo "Duracao total: ${total_duration}s"
            [ -n "$artifact" ] && echo "Artefato: $artifact"
            [ -n "$artifact_size" ] && echo "Tamanho: $artifact_size"
            [ -n "$checksum" ] && echo "SHA256: $checksum"
            echo "Fim: $(date '+%Y-%m-%d %H:%M:%S')"
            echo "=========================================="
        } >> "$LOG_FILE"
    fi
}

# Trap para garantir log mesmo em falha
setup_error_trap() {
    trap 'log ERROR "Build falhou na linha $LINENO"; log_finish "FAILED"' ERR
}

# Adicionar duracoes ao build-info.txt
append_durations_to_buildinfo() {
    local buildinfo_file="$1"

    if [ -f "$buildinfo_file" ]; then
        {
            echo ""
            echo "# Duracoes (segundos)"
            for key in "${!STEP_TIMES[@]}"; do
                if [[ "$key" == *"_duration" ]]; then
                    local step_name="${key%_duration}"
                    local duration="${STEP_TIMES[$key]}"
                    echo "DURATION_${step_name^^}=$duration"
                fi
            done
            echo "DURATION_TOTAL=$((SECONDS - LOG_START_TIME))"
        } >> "$buildinfo_file"
        log OK "Duracoes adicionadas a $buildinfo_file"
    fi
}
