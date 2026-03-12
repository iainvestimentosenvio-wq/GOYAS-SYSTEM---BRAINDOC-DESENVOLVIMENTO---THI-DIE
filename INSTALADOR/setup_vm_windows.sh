#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"
DEFAULT_SHARED_ROOT="${PROTONS_SHARED_ROOT:-$(cd "$PROJECT_ROOT/.." && pwd)}"

LOG_FILE="./setup_vm_windows_log.txt"
REPORT_FILE="./RELATORIO_EXECUCAO_WINDOWS_VM.md"
GUIDE_FILE="./GUIA_TESTE_WINDOWS_PROTONS.md"
SCRIPT_NAME="$(basename "$0")"

AUTO_MODE=0
SKIP_SMB=0
ISO_WIN10=""
ISO_WIN11=""
ISO_SEARCH_DIRS="${ISO_SEARCH_DIRS:-/home/${USER}/Downloads,${DEFAULT_SHARED_ROOT}}"
ARTIFACT_HINT="em configuracao interna"
VIRSH_CMD="virsh"

UEFI_LOADER=""
UEFI_SECBOOT_LOADER=""
UEFI_VARS=""
VM_CREATION_STATUS="NAO_EXECUTADO"
VM_CREATION_MESSAGE="Ainda nao executado."

HUMAN_USERS_CMD="getent passwd | awk -F: '\$3>=1000 && \$1!=\"nobody\" && \$6 ~ \"^/home/\" && \$7 !~ \"(nologin|false)\$\" {print \$1}'"

timestamp() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

log() {
  printf '[%s] %s\n' "$(timestamp)" "$*" | tee -a "$LOG_FILE"
}

run_cmd() {
  local cmd="$1"
  local output=""
  local rc=0

  log "COMANDO: $cmd"
  output="$(bash -lc "$cmd" 2>&1)"
  rc=$?

  if [[ -n "$output" ]]; then
    printf '%s\n' "$output" | tee -a "$LOG_FILE"
  else
    printf '%s\n' "(sem output)" | tee -a "$LOG_FILE"
  fi

  if [[ $rc -eq 0 ]]; then
    log "STATUS: OK"
  else
    log "STATUS: ERRO (exit $rc)"
  fi

  return $rc
}

run_validation() {
  local cmd="$1"
  log "VALIDACAO: $cmd"
  run_cmd "$cmd"
}

usage() {
  cat <<EOF
Uso:
  $SCRIPT_NAME [opcoes]

Opcoes:
  --auto                       Executa sem perguntas (confirma tudo como S).
  --iso-win10 /caminho.iso     Define ISO do Windows 10.
  --iso-win11 /caminho.iso     Define ISO do Windows 11.
  --iso-search-dirs "d1,d2"    Diretorios para auto-detectar ISOs.
  --skip-smb                   Nao configura SMB na etapa 7.
  --artifact-hint "texto"      Descreve artefato esperado para teste.
  -h, --help                   Mostra esta ajuda.
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --auto)
        AUTO_MODE=1
        shift
        ;;
      --skip-smb)
        SKIP_SMB=1
        shift
        ;;
      --iso-win10)
        if [[ -z "${2:-}" ]]; then
          printf 'ERRO: --iso-win10 exige caminho.\n' >&2
          exit 1
        fi
        ISO_WIN10="$2"
        shift 2
        ;;
      --iso-win11)
        if [[ -z "${2:-}" ]]; then
          printf 'ERRO: --iso-win11 exige caminho.\n' >&2
          exit 1
        fi
        ISO_WIN11="$2"
        shift 2
        ;;
      --iso-search-dirs)
        if [[ -z "${2:-}" ]]; then
          printf 'ERRO: --iso-search-dirs exige valor CSV.\n' >&2
          exit 1
        fi
        ISO_SEARCH_DIRS="$2"
        shift 2
        ;;
      --artifact-hint)
        if [[ -z "${2:-}" ]]; then
          printf 'ERRO: --artifact-hint exige texto.\n' >&2
          exit 1
        fi
        ARTIFACT_HINT="$2"
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        printf 'ERRO: opcao desconhecida: %s\n' "$1" >&2
        usage
        exit 1
        ;;
    esac
  done
}

confirm_step() {
  local plan_text="$1"
  local ans=""

  if [[ "$AUTO_MODE" -eq 1 ]]; then
    log "PLANO DO PASSO: $plan_text"
    log "CONFIRMACAO: S (auto)"
    return 0
  fi

  while true; do
    log "PLANO DO PASSO: $plan_text"
    read -r -p "Posso executar? (S/N): " ans
    ans="$(printf '%s' "$ans" | tr '[:lower:]' '[:upper:]')"
    if [[ "$ans" == "S" ]]; then
      log "CONFIRMACAO: S"
      return 0
    fi
    if [[ "$ans" == "N" ]]; then
      log "CONFIRMACAO: N"
      return 1
    fi
    printf 'Resposta invalida. Digite S ou N.\n'
  done
}

sudo_reason() {
  log "Motivo sudo: $*"
}

pause_flow() {
  log "Execucao pausada por escolha do operador."
  exit 0
}

check_or_warn_failure() {
  local rc="$1"
  local context="$2"
  if [[ "$rc" -ne 0 ]]; then
    log "ATENCAO: falha em: $context"
  fi
}

detect_human_users() {
  getent passwd | awk -F: '$3>=1000 && $1!="nobody" && $6 ~ "^/home/" && $7 !~ "(nologin|false)$" {print $1}'
}

split_csv_dirs() {
  local csv="$1"
  local old_ifs="$IFS"
  local raw=""
  local trimmed=""

  IFS=',' read -r -a parts <<< "$csv"
  IFS="$old_ifs"

  for raw in "${parts[@]:-}"; do
    trimmed="$(printf '%s' "$raw" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
    if [[ -n "$trimmed" ]]; then
      printf '%s\n' "$trimmed"
    fi
  done
}

auto_find_iso() {
  local flavor="$1"
  local dir=""
  local found=""
  local candidate=""

  while IFS= read -r dir; do
    [[ -d "$dir" ]] || continue

    if [[ "$flavor" == "win10" ]]; then
      while IFS= read -r candidate; do
        if is_x64_iso_path "$candidate"; then
          found="$candidate"
          break
        fi
      done < <(find "$dir" -maxdepth 5 -type f \( -iname '*win*10*.iso' -o -iname '*windows*10*.iso' \) 2>/dev/null)
    else
      while IFS= read -r candidate; do
        if is_x64_iso_path "$candidate"; then
          found="$candidate"
          break
        fi
      done < <(find "$dir" -maxdepth 5 -type f \( -iname '*win*11*.iso' -o -iname '*windows*11*.iso' \) 2>/dev/null)
    fi

    if [[ -n "$found" ]]; then
      printf '%s' "$found"
      return 0
    fi
  done < <(split_csv_dirs "$ISO_SEARCH_DIRS")

  return 1
}

is_x64_iso_path() {
  local iso_path="$1"
  local lower_name=""

  lower_name="$(basename "$iso_path" | tr '[:upper:]' '[:lower:]')"
  if [[ "$lower_name" != *.iso ]]; then
    return 1
  fi

  if [[ "$lower_name" =~ (arm64|aarch64|arm) ]]; then
    return 1
  fi

  if [[ "$lower_name" =~ (x64|amd64|x86_64) ]]; then
    return 0
  fi

  return 1
}

resolve_virsh_cmd() {
  if virsh list --all >/dev/null 2>&1; then
    VIRSH_CMD="virsh"
  else
    VIRSH_CMD="sudo virsh"
  fi
  log "Comando virsh selecionado: $VIRSH_CMD"
}

generate_guide_doc() {
  cat > "$GUIDE_FILE" <<'EOF'
# Guia de Teste Windows — Protons

## 1) Abrir as VMs
1. Abra o gerenciador: `virt-manager`.
2. Clique em `win10-lite` ou `win11-lite`.
3. Clique em `Open` e depois `Play`.

## 2) Instalar Windows
1. Confirme boot pela ISO.
2. Quando pedir chave, use `I don't have a product key`.
3. Termine instalacao padrao.

## 3) Criar snapshot clean
1. No host, rode:
   - `virsh snapshot-create-as win10-lite clean "Windows 10 clean"`
   - `virsh snapshot-create-as win11-lite clean "Windows 11 clean"`
2. Validacao:
   - `virsh snapshot-list win10-lite`
   - `virsh snapshot-list win11-lite`

## 4) Transferir instalador para VM
1. Opcao simples: arrastar e soltar pelo SPICE no `virt-manager`.
2. Opcao alternativa: pasta do host `~/vm_shared` (SMB opcional).

## 5) Teste automatizado no Windows
1. Use o template: `windows_installer_test_template.ps1`.
2. Ajuste:
   - `InstallerPath` para `.exe`/`.msi`.
   - `SilentArgs` conforme instalador (`/S`, `/quiet`, `/qn`).
3. O log fica em: `C:\temp\test_log.txt`.

## 6) Configuracao esperada das VMs
1. `win10-lite`: 2 vCPU, 4096 MB, disco 80 GB, SATA, rede e1000e, UEFI.
2. `win11-lite`: 2 vCPU, 4096 MB, disco 80 GB, SATA, rede e1000e, UEFI + Secure Boot + TPM 2.0.
EOF
  log "Arquivo gerado: $GUIDE_FILE"
}

generate_execution_report() {
  local now_utc=""
  local vmx_count=""
  local kvm_dev=""
  local kvm_mods=""
  local libvirtd_state=""
  local sudo_auth_state="OK"
  local human_users=""
  local all_human_sudo="SIM"
  local users_block=""
  local u=""
  local groups_line=""
  local add_extra=""
  local extra_groups=""
  local win10_status="NAO_CRIADA"
  local win11_status="NAO_CRIADA"
  local win11_security="NAO_VALIDADO"

  now_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  vmx_count="$(grep -E -c '(vmx|svm)' /proc/cpuinfo 2>/dev/null || true)"
  if [[ -e /dev/kvm ]]; then
    kvm_dev="PRESENTE"
  else
    kvm_dev="AUSENTE"
  fi
  kvm_mods="$(lsmod | awk '/^kvm/{print $1}' | paste -sd ',' -)"
  [[ -n "$kvm_mods" ]] || kvm_mods="nenhum"
  libvirtd_state="$(systemctl is-active libvirtd 2>/dev/null || true)"
  [[ -n "$libvirtd_state" ]] || libvirtd_state="desconhecido"
  if grep -Eq 'sudo: a terminal is required|sudo: a password is required' "$LOG_FILE" 2>/dev/null; then
    sudo_auth_state="BLOQUEADO_SENHA_INTERATIVA"
  fi

  human_users="$(detect_human_users | tr '\n' ' ' | sed 's/[[:space:]]*$//')"
  for u in $(detect_human_users); do
    groups_line="$(id -nG "$u" 2>/dev/null || true)"
    if ! printf '%s\n' "$groups_line" | grep -Eq '(^|[[:space:]])sudo($|[[:space:]])'; then
      all_human_sudo="NAO"
    fi
    users_block="${users_block}- \`$u\`: \`$groups_line\`\n"
  done
  [[ -n "$users_block" ]] || users_block="- nenhum usuario humano detectado\n"

  add_extra="$(grep -E '^ADD_EXTRA_GROUPS=' /etc/adduser.conf 2>/dev/null | head -n 1 || true)"
  extra_groups="$(grep -E '^EXTRA_GROUPS=' /etc/adduser.conf 2>/dev/null | head -n 1 || true)"
  [[ -n "$add_extra" ]] || add_extra="ADD_EXTRA_GROUPS=<nao encontrado>"
  [[ -n "$extra_groups" ]] || extra_groups="EXTRA_GROUPS=<nao encontrado>"

  if bash -lc "$VIRSH_CMD dominfo win10-lite >/dev/null 2>&1"; then
    win10_status="CRIADA"
  fi
  if bash -lc "$VIRSH_CMD dominfo win11-lite >/dev/null 2>&1"; then
    win11_status="CRIADA"
    if bash -lc "$VIRSH_CMD dumpxml win11-lite | grep -Eq 'loader.secure=.yes|<tpm'"; then
      win11_security="OK (secure boot/tpm presentes no XML)"
    else
      win11_security="ERRO (secure boot/tpm nao confirmados no XML)"
    fi
  fi

  cat > "$REPORT_FILE" <<EOF
# Relatorio de Execucao — Admin + KVM + VMs Windows

- Data UTC: \`$now_utc\`
- Script: \`$SCRIPT_NAME\`
- Log: \`$LOG_FILE\`

## Resultado por etapa
- Etapa 1 (usuarios humanos em sudo): $( [[ "$all_human_sudo" == "SIM" ]] && printf 'OK' || printf 'ERRO' )
- Etapa 2 (/etc/adduser.conf): $( [[ "$add_extra" == "ADD_EXTRA_GROUPS=1" && "$extra_groups" == "EXTRA_GROUPS=\"sudo\"" ]] && printf 'OK' || printf 'ERRO' )
- Etapa 3 (stack KVM/libvirt): $( [[ "$vmx_count" =~ ^[0-9]+$ && "$vmx_count" -gt 0 && "$kvm_dev" == "PRESENTE" && "$libvirtd_state" == "active" ]] && printf 'OK' || printf 'ERRO' )
- Etapa 4 (OVMF/Secure Boot): $( [[ -n "$UEFI_LOADER" && -n "$UEFI_SECBOOT_LOADER" && -n "$UEFI_VARS" ]] && printf 'OK' || printf 'ERRO' )
- Etapa 5 (criacao de VMs): \`$VM_CREATION_STATUS\`
- Mensagem da Etapa 5: $VM_CREATION_MESSAGE

## Evidencias tecnicas
- vmx/svm: \`$vmx_count\`
- /dev/kvm: \`$kvm_dev\`
- modulos kvm: \`$kvm_mods\`
- libvirtd: \`$libvirtd_state\`
- sudo no contexto atual: \`$sudo_auth_state\`
- VIRSH_CMD: \`$VIRSH_CMD\`
- OVMF loader: \`${UEFI_LOADER:-<nao definido>}\`
- OVMF secboot loader: \`${UEFI_SECBOOT_LOADER:-<nao definido>}\`
- OVMF vars: \`${UEFI_VARS:-<nao definido>}\`
- win10-lite: \`$win10_status\`
- win11-lite: \`$win11_status\`
- win11 secure boot + tpm: \`$win11_security\`

## Usuarios humanos e grupos
$(printf '%b' "$users_block")

## Politica de novos usuarios
- \`$add_extra\`
- \`$extra_groups\`

## Prontidao para teste do instalador
- Win10 (2 vCPU + 4096MB + 80GB): $( [[ "$win10_status" == "CRIADA" ]] && printf 'PRONTO' || printf 'PENDENTE' )
- Win11 (2 vCPU + 4096MB + 80GB + UEFI+SB+TPM): $( [[ "$win11_status" == "CRIADA" && "$win11_security" == OK* ]] && printf 'PRONTO' || printf 'PENDENTE' )

## Acao objetiva pendente (se houver)
- Se status da Etapa 5 for \`BLOQUEADO_DEPENDENCIA_ISO\`: adicionar ISOs locais e rerodar:
  \`./setup_vm_windows.sh --auto --skip-smb --iso-win10 /caminho/Win10.iso --iso-win11 /caminho/Win11.iso\`
EOF
  log "Arquivo gerado: $REPORT_FILE"
}

stage_0() {
  log "=== ETAPA 0: COLETAR INFO (SEM MUDAR NADA) ==="

  run_cmd "lsb_release -a || cat /etc/os-release"
  check_or_warn_failure "$?" "detectar distro"
  run_validation "lsb_release -d || grep '^PRETTY_NAME=' /etc/os-release"

  run_cmd "lscpu"
  check_or_warn_failure "$?" "coletar lscpu"
  run_validation "lscpu | rg 'Model name|CPU\\(s\\)|Architecture'"

  run_cmd "free -h"
  check_or_warn_failure "$?" "coletar memoria"
  run_validation "free -h | rg 'Mem:|Swap:'"

  run_cmd "egrep -c '(vmx|svm)' /proc/cpuinfo"
  check_or_warn_failure "$?" "contar vmx/svm"
  run_validation "test -e /dev/kvm && ls -l /dev/kvm || echo '/dev/kvm ausente'"

  run_cmd "lsmod | grep kvm || true"
  check_or_warn_failure "$?" "estado modulo kvm"
  run_validation "lsmod | rg '^kvm' || echo 'kvm modules absent'"

  run_cmd "whoami; groups"
  check_or_warn_failure "$?" "usuario e grupos"
  run_validation "id -nG \"\$(whoami)\""

  run_cmd "$HUMAN_USERS_CMD"
  check_or_warn_failure "$?" "listar usuarios humanos"
  run_validation "$HUMAN_USERS_CMD | wc -l"
}

stage_1() {
  log "=== ETAPA 1: TORNAR TODOS USUARIOS HUMANOS ADMIN ==="

  run_cmd "$HUMAN_USERS_CMD"
  check_or_warn_failure "$?" "listar usuarios humanos"
  run_validation "$HUMAN_USERS_CMD | wc -l"

  local users
  users="$(detect_human_users | tr '\n' ' ' | sed 's/[[:space:]]*$//')"
  log "Usuarios humanos detectados: ${users:-<nenhum>}"

  if ! confirm_step "Adicionar todos os usuarios humanos detectados ao grupo sudo."; then
    pause_flow
  fi

  sudo_reason "alteracao de membership de grupo exige privilegio root"
  run_cmd "for u in \$( $HUMAN_USERS_CMD ); do sudo usermod -aG sudo \"\$u\"; done"
  check_or_warn_failure "$?" "adicionar usuarios ao sudo"
  run_validation "for u in \$( $HUMAN_USERS_CMD ); do id -nG \"\$u\"; done | grep -E '(^|[[:space:]])sudo($|[[:space:]])'"

  run_cmd "groups \"\$(whoami)\""
  check_or_warn_failure "$?" "validar grupos usuario atual"
  run_validation "id -nG \"\$(whoami)\" | grep -E '(^|[[:space:]])sudo($|[[:space:]])'"

  log "AVISO: e necessario logout/login para aplicar grupos na sessao atual."
}

stage_2() {
  log "=== ETAPA 2: NOVOS USUARIOS JA NASCEM ADMIN ==="

  if ! confirm_step "Fazer backup e alterar /etc/adduser.conf para ADD_EXTRA_GROUPS=1 e EXTRA_GROUPS=\"sudo\"."; then
    pause_flow
  fi

  sudo_reason "backup e escrita em /etc exigem privilegio root"
  run_cmd "sudo cp -a /etc/adduser.conf /etc/adduser.conf.bak.\$(date +%F_%H%M%S)"
  check_or_warn_failure "$?" "backup adduser.conf"
  run_validation "ls -1t /etc/adduser.conf.bak.* | head -n 1"

  run_cmd "sudo awk 'BEGIN{a=0;e=0} /^#?[[:space:]]*ADD_EXTRA_GROUPS=/{print \"ADD_EXTRA_GROUPS=1\";a=1;next} /^#?[[:space:]]*EXTRA_GROUPS=/{print \"EXTRA_GROUPS=\\\"sudo\\\"\";e=1;next} {print} END{if(!a)print \"ADD_EXTRA_GROUPS=1\"; if(!e)print \"EXTRA_GROUPS=\\\"sudo\\\"\"}' /etc/adduser.conf | sudo tee /tmp/adduser.conf.new >/dev/null"
  check_or_warn_failure "$?" "gerar candidato adduser.conf"
  run_validation "sudo grep -nE '^(ADD_EXTRA_GROUPS|EXTRA_GROUPS)=' /tmp/adduser.conf.new"

  run_cmd "sudo diff -u /etc/adduser.conf /tmp/adduser.conf.new || true"
  check_or_warn_failure "$?" "mostrar diff adduser.conf"
  run_validation "sudo test -f /tmp/adduser.conf.new && echo '/tmp/adduser.conf.new OK'"

  if ! confirm_step "Aplicar o diff em /etc/adduser.conf."; then
    pause_flow
  fi

  sudo_reason "substituir /etc/adduser.conf exige privilegio root"
  run_cmd "sudo install -m 644 /tmp/adduser.conf.new /etc/adduser.conf"
  check_or_warn_failure "$?" "aplicar novo adduser.conf"
  run_validation "grep -nE 'ADD_EXTRA_GROUPS|EXTRA_GROUPS' /etc/adduser.conf"
  run_validation "grep -q '^ADD_EXTRA_GROUPS=1$' /etc/adduser.conf && grep -q '^EXTRA_GROUPS=\"sudo\"$' /etc/adduser.conf && echo 'Linhas obrigatorias OK'"
}

stage_3_precheck() {
  log "=== ETAPA 3: PRE-CHECK KVM ==="
  run_cmd "egrep -c '(vmx|svm)' /proc/cpuinfo"
  check_or_warn_failure "$?" "pre-check vmx/svm"
  run_validation "test -e /dev/kvm && ls -l /dev/kvm || echo '/dev/kvm ausente'"

  local vmx_count
  vmx_count="$(grep -E -c '(vmx|svm)' /proc/cpuinfo 2>/dev/null | tr -d '[:space:]')"
  if [[ ! "$vmx_count" =~ ^[0-9]+$ ]]; then
    vmx_count="0"
  fi

  if [[ "${vmx_count:-0}" -le 0 || ! -e /dev/kvm ]]; then
    log "BLOQUEIO: virtualizacao KVM indisponivel (vmx/svm=${vmx_count:-0}, /dev/kvm ausente)."
    log "Etapas 3 a 7 foram pausadas sem erro de roteiro."
    log "Acao requerida: habilitar VT-x/AMD-V na BIOS/UEFI e repetir o script."
    return 1
  fi

  log "Pre-check KVM aprovado."
  return 0
}

stage_3_install() {
  log "=== ETAPA 3: INSTALAR KVM/LIBVIRT/TPM ==="

  if ! confirm_step "Instalar pacotes qemu-kvm/libvirt/virt-manager/ovmf/swtpm e configurar libvirtd."; then
    pause_flow
  fi

  sudo_reason "instalacao de pacotes e controle de servico de sistema"
  run_cmd "sudo apt update"
  check_or_warn_failure "$?" "apt update"
  run_validation "apt-cache policy qemu-kvm | head -n 5"

  run_cmd "sudo apt install -y qemu-kvm libvirt-daemon-system libvirt-clients virt-manager ovmf swtpm swtpm-tools"
  check_or_warn_failure "$?" "instalar pacotes virtualizacao"
  run_validation "dpkg -l | rg 'qemu-kvm|libvirt-daemon-system|libvirt-clients|virt-manager|ovmf|swtpm'"

  run_cmd "sudo systemctl enable --now libvirtd"
  check_or_warn_failure "$?" "habilitar libvirtd"
  run_validation "systemctl is-active libvirtd"

  run_cmd "systemctl status libvirtd --no-pager"
  check_or_warn_failure "$?" "status libvirtd"
  run_validation "systemctl is-active libvirtd | grep -x active"

  sudo_reason "adicionar usuario atual aos grupos libvirt,kvm"
  run_cmd "sudo usermod -aG libvirt,kvm \"\$(whoami)\""
  check_or_warn_failure "$?" "adicionar grupos libvirt,kvm"
  run_validation "id -nG \"\$(whoami)\" | grep -E '(^|[[:space:]])libvirt($|[[:space:]])'"
  run_validation "id -nG \"\$(whoami)\" | grep -E '(^|[[:space:]])kvm($|[[:space:]])'"
  log "AVISO: logout/login necessario para aplicar grupos libvirt,kvm na sessao atual."

  resolve_virsh_cmd
  run_cmd "$VIRSH_CMD --version"
  check_or_warn_failure "$?" "virsh --version"
  run_validation "$VIRSH_CMD --version | grep -E '^[0-9]'"

  run_cmd "$VIRSH_CMD list --all || true"
  check_or_warn_failure "$?" "virsh list --all"
  run_validation "$VIRSH_CMD list --all >/dev/null && echo 'virsh OK'"
}

stage_4_detect_ovmf() {
  log "=== ETAPA 4: IDENTIFICAR OVMF (UEFI + SECURE BOOT) ==="

  run_cmd "find /usr/share -maxdepth 5 -type f 2>/dev/null | grep -E '/(OVMF|edk2/ovmf)/.*OVMF_(CODE|VARS).*\\.fd$' | sort -u || true"
  check_or_warn_failure "$?" "listar OVMF"
  run_validation "find /usr/share -maxdepth 5 -type f 2>/dev/null | grep -E '/(OVMF|edk2/ovmf)/.*OVMF_(CODE|VARS).*\\.fd$' | wc -l"

  mapfile -t ovmf_files < <(find /usr/share -maxdepth 5 -type f 2>/dev/null | grep -E '/(OVMF|edk2/ovmf)/.*OVMF_(CODE|VARS).*\.fd$' | sort -u)

  UEFI_SECBOOT_LOADER="$(printf '%s\n' "${ovmf_files[@]:-}" | grep -Ei '/OVMF_CODE.*secboot.*\.fd$' | head -n 1 || true)"
  UEFI_LOADER="$(printf '%s\n' "${ovmf_files[@]:-}" | grep -Ei '/OVMF_CODE.*\.fd$' | grep -Eiv 'secboot' | head -n 1 || true)"
  UEFI_VARS="$(printf '%s\n' "${ovmf_files[@]:-}" | grep -Ei '/OVMF_VARS.*\.fd$' | head -n 1 || true)"

  if [[ -z "$UEFI_LOADER" ]]; then
    UEFI_LOADER="$(printf '%s\n' "${ovmf_files[@]:-}" | grep -Ei '/OVMF_CODE.*\.fd$' | head -n 1 || true)"
  fi

  log "Selecao OVMF:"
  log "UEFI_LOADER=$UEFI_LOADER"
  log "UEFI_SECBOOT_LOADER=$UEFI_SECBOOT_LOADER"
  log "UEFI_VARS=$UEFI_VARS"

  if [[ -z "$UEFI_LOADER" || -z "$UEFI_SECBOOT_LOADER" || -z "$UEFI_VARS" ]]; then
    log "ERRO: nao foi possivel resolver loaders/vars OVMF necessarios."
    VM_CREATION_STATUS="ERRO_OVMF"
    VM_CREATION_MESSAGE="Nao foi possivel resolver arquivos OVMF para UEFI/Secure Boot."
    return 1
  fi

  if [[ "$UEFI_LOADER" != /* || "$UEFI_SECBOOT_LOADER" != /* || "$UEFI_VARS" != /* ]]; then
    log "ERRO: nao foi possivel resolver loaders/vars OVMF necessarios."
    VM_CREATION_STATUS="ERRO_OVMF"
    VM_CREATION_MESSAGE="Arquivos OVMF detectados sem caminho absoluto."
    return 1
  fi

  run_validation "test -f \"$UEFI_LOADER\" && echo 'UEFI_LOADER OK'"
  run_validation "test -f \"$UEFI_SECBOOT_LOADER\" && echo 'UEFI_SECBOOT_LOADER OK'"
  run_validation "test -f \"$UEFI_VARS\" && echo 'UEFI_VARS OK'"

  if ! confirm_step "Prosseguir para criacao de VMs com os caminhos OVMF detectados."; then
    pause_flow
  fi
}

detect_os_variant() {
  local pattern="$1"
  local fallback="$2"
  local result=""

  if command -v osinfo-query >/dev/null 2>&1; then
    result="$(osinfo-query os --fields=short-id,name 2>/dev/null | awk -F'|' -v p="$pattern" '
      tolower($0) ~ tolower(p) {
        gsub(/^[ \t]+|[ \t]+$/, "", $1)
        if ($1 != "") { print $1; exit }
      }'
    )"
  fi

  if [[ -z "$result" ]]; then
    result="$fallback"
  fi

  printf '%s' "$result"
}

stage_5_create_vms() {
  log "=== ETAPA 5: CRIAR VMS ==="

  local win10_variant=""
  local win11_variant=""
  local vm_checks_ok=1

  if [[ -z "$ISO_WIN10" ]]; then
    ISO_WIN10="$(auto_find_iso "win10" || true)"
    if [[ -n "$ISO_WIN10" ]]; then
      log "ISO_WIN10 auto-detectada: $ISO_WIN10"
    fi
  fi
  if [[ -z "$ISO_WIN11" ]]; then
    ISO_WIN11="$(auto_find_iso "win11" || true)"
    if [[ -n "$ISO_WIN11" ]]; then
      log "ISO_WIN11 auto-detectada: $ISO_WIN11"
    fi
  fi

  if [[ -z "$ISO_WIN10" || -z "$ISO_WIN11" ]]; then
    VM_CREATION_STATUS="BLOQUEADO_DEPENDENCIA_ISO"
    VM_CREATION_MESSAGE="ISOs do Windows 10/11 nao localizadas. Use --iso-win10 e --iso-win11 ou mova para diretorios de busca."
    log "BLOQUEIO: $VM_CREATION_MESSAGE"
    run_cmd "printf '%s\n' \"$ISO_SEARCH_DIRS\" | tr ',' '\n'"
    return 0
  fi

  run_cmd "test -f \"$ISO_WIN10\" && echo 'ISO_WIN10 OK' || (echo 'ISO_WIN10 inexistente'; exit 1)"
  if [[ $? -ne 0 ]]; then
    VM_CREATION_STATUS="BLOQUEADO_DEPENDENCIA_ISO"
    VM_CREATION_MESSAGE="ISO_WIN10 informada nao existe: $ISO_WIN10"
    return 0
  fi
  run_cmd "test -f \"$ISO_WIN11\" && echo 'ISO_WIN11 OK' || (echo 'ISO_WIN11 inexistente'; exit 1)"
  if [[ $? -ne 0 ]]; then
    VM_CREATION_STATUS="BLOQUEADO_DEPENDENCIA_ISO"
    VM_CREATION_MESSAGE="ISO_WIN11 informada nao existe: $ISO_WIN11"
    return 0
  fi

  if ! is_x64_iso_path "$ISO_WIN10"; then
    VM_CREATION_STATUS="BLOQUEADO_DEPENDENCIA_ISO"
    VM_CREATION_MESSAGE="ISO_WIN10 nao parece x64 (arquivo ARM/sem x64 no nome): $ISO_WIN10"
    log "BLOQUEIO: $VM_CREATION_MESSAGE"
    return 0
  fi
  if ! is_x64_iso_path "$ISO_WIN11"; then
    VM_CREATION_STATUS="BLOQUEADO_DEPENDENCIA_ISO"
    VM_CREATION_MESSAGE="ISO_WIN11 nao parece x64 (arquivo ARM/sem x64 no nome): $ISO_WIN11"
    log "BLOQUEIO: $VM_CREATION_MESSAGE"
    return 0
  fi

  if ! confirm_step "Criar rede/discos e VMs win10-lite/win11-lite em /var/lib/libvirt/images."; then
    pause_flow
  fi

  sudo_reason "operacoes em recursos libvirt e /var/lib/libvirt/images"
  run_cmd "sudo virsh net-start default || true"
  check_or_warn_failure "$?" "net-start default"
  run_validation "$VIRSH_CMD net-info default"

  run_cmd "sudo virsh net-autostart default || true"
  check_or_warn_failure "$?" "net-autostart default"
  run_validation "$VIRSH_CMD net-info default | rg 'Autostart|Bridge|Active'"

  run_cmd "sudo qemu-img create -f qcow2 /var/lib/libvirt/images/win10-lite.qcow2 80G"
  check_or_warn_failure "$?" "criar disco win10"
  run_validation "sudo qemu-img info /var/lib/libvirt/images/win10-lite.qcow2"

  run_cmd "sudo qemu-img create -f qcow2 /var/lib/libvirt/images/win11-lite.qcow2 80G"
  check_or_warn_failure "$?" "criar disco win11"
  run_validation "sudo qemu-img info /var/lib/libvirt/images/win11-lite.qcow2"

  win10_variant="$(detect_os_variant 'win10|windows 10' 'generic')"
  win11_variant="$(detect_os_variant 'win11|windows 11' 'generic')"
  log "OS Variant selecionado: win10=$win10_variant, win11=$win11_variant"

  log "Uso de --noautoconsole: para manter terminal livre; abra o console no virt-manager."

  run_cmd "sudo virt-install --name win10-lite --memory 4096 --vcpus 2 --cpu host-passthrough --machine q35 --disk path=/var/lib/libvirt/images/win10-lite.qcow2,format=qcow2,bus=sata --cdrom \"$ISO_WIN10\" --network network=default,model=e1000e --graphics spice --os-variant \"$win10_variant\" --boot loader=\"$UEFI_LOADER\",loader.readonly=yes,loader.type=pflash,nvram.template=\"$UEFI_VARS\" --noautoconsole"
  check_or_warn_failure "$?" "criar VM win10-lite"
  run_validation "$VIRSH_CMD dominfo win10-lite"

  run_cmd "sudo virt-install --name win11-lite --memory 4096 --vcpus 2 --cpu host-passthrough --machine q35 --features smm.state=on --disk path=/var/lib/libvirt/images/win11-lite.qcow2,format=qcow2,bus=sata --cdrom \"$ISO_WIN11\" --network network=default,model=e1000e --graphics spice --os-variant \"$win11_variant\" --boot loader=\"$UEFI_SECBOOT_LOADER\",loader.readonly=yes,loader.type=pflash,loader.secure=yes,nvram.template=\"$UEFI_VARS\" --tpm backend.type=emulator,backend.version=2.0,model=tpm-tis --noautoconsole"
  check_or_warn_failure "$?" "criar VM win11-lite"
  run_validation "$VIRSH_CMD dominfo win11-lite"

  run_validation "$VIRSH_CMD dumpxml win11-lite | rg 'loader|nvram|tpm'"

  if ! bash -lc "$VIRSH_CMD dominfo win10-lite >/dev/null 2>&1"; then
    vm_checks_ok=0
  fi
  if ! bash -lc "$VIRSH_CMD dominfo win11-lite >/dev/null 2>&1"; then
    vm_checks_ok=0
  fi
  if ! bash -lc "$VIRSH_CMD dumpxml win11-lite | grep -Eq 'loader.secure=.yes|<tpm'"; then
    vm_checks_ok=0
  fi

  if [[ "$vm_checks_ok" -eq 1 ]]; then
    VM_CREATION_STATUS="OK"
    VM_CREATION_MESSAGE="VMs win10-lite e win11-lite criadas e validadas."
  else
    VM_CREATION_STATUS="ERRO"
    VM_CREATION_MESSAGE="Falha na criacao/validacao das VMs. Verifique o log."
  fi
}

stage_6_gui_instructions() {
  log "=== ETAPA 6: CHECKLIST GUI DE INSTALACAO DO WINDOWS ==="
  log "1) Abrir virt-manager: virt-manager"
  log "2) Iniciar win10-lite/win11-lite e confirmar boot pela ISO."
  log "3) No instalador, usar: I don't have a product key."
  log "4) Depois da instalacao finalizada, criar snapshots clean:"
  log "   virsh snapshot-create-as win10-lite clean 'Windows 10 clean'"
  log "   virsh snapshot-create-as win11-lite clean 'Windows 11 clean'"
  log "5) Validar snapshots:"
  log "   virsh snapshot-list win10-lite"
  log "   virsh snapshot-list win11-lite"
}

generate_powershell_template() {
  cat > ./windows_installer_test_template.ps1 <<'EOF'
param(
  [string]$InstallerPath = "C:\temp\installer.exe",
  [string]$LogPath = "C:\temp\test_log.txt",
  [string]$SilentArgs = "/S"
)

New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
"[$(Get-Date -Format s)] Inicio do teste" | Out-File -FilePath $LogPath -Encoding utf8

if (-not (Test-Path $InstallerPath)) {
  "Installer nao encontrado: $InstallerPath" | Add-Content -Path $LogPath
  exit 1
}

"Executando instalador: $InstallerPath $SilentArgs" | Add-Content -Path $LogPath
$proc = Start-Process -FilePath $InstallerPath -ArgumentList $SilentArgs -Wait -PassThru
"ExitCode instalador: $($proc.ExitCode)" | Add-Content -Path $LogPath

Start-Sleep -Seconds 5

"Checagens basicas (ajuste para seu app):" | Add-Content -Path $LogPath
"- App abre" | Add-Content -Path $LogPath
"- Servico roda" | Add-Content -Path $LogPath
"- Arquivos gerados" | Add-Content -Path $LogPath

"[$(Get-Date -Format s)] Fim do teste" | Add-Content -Path $LogPath
EOF
  log "Arquivo gerado: ./windows_installer_test_template.ps1"
}

stage_7_prepare_project_test() {
  log "=== ETAPA 7: PREPARAR TESTE DO PROJETO ==="

  local enable_smb=""

  log "Artefato alvo para teste: $ARTIFACT_HINT"

  run_cmd "mkdir -p \"\$HOME/vm_shared\""
  check_or_warn_failure "$?" "criar pasta vm_shared"
  run_validation "ls -ld \"\$HOME/vm_shared\""

  log "Opcao simples: usar SPICE (virt-manager) para arrastar e soltar."
  log "Opcao automatica: SMB (somente com aprovacao)."
  if [[ "$AUTO_MODE" -eq 1 ]]; then
    if [[ "$SKIP_SMB" -eq 1 ]]; then
      enable_smb="N"
    else
      enable_smb="S"
    fi
    log "Decisao SMB (auto): $enable_smb"
  else
    read -r -p "Deseja configurar SMB agora? (S/N): " enable_smb
    enable_smb="$(printf '%s' "$enable_smb" | tr '[:lower:]' '[:upper:]')"
  fi

  if [[ "$enable_smb" == "S" ]]; then
    if ! confirm_step "Instalar Samba e configurar share vm_shared."; then
      pause_flow
    fi

    sudo_reason "instalacao e configuracao de servico de compartilhamento SMB"
    run_cmd "sudo apt update"
    check_or_warn_failure "$?" "apt update para samba"
    run_validation "apt-cache policy samba | head -n 5"

    run_cmd "sudo apt install -y samba"
    check_or_warn_failure "$?" "instalar samba"
    run_validation "dpkg -l | rg '^ii\\s+samba\\s'"

    run_cmd "sudo cp -a /etc/samba/smb.conf /etc/samba/smb.conf.bak.\$(date +%F_%H%M%S)"
    check_or_warn_failure "$?" "backup smb.conf"
    run_validation "ls -1t /etc/samba/smb.conf.bak.* | head -n 1"

    run_cmd "sudo grep -q '^\\[vm_shared\\]' /etc/samba/smb.conf || printf '\n[vm_shared]\npath = %s\nbrowseable = yes\nread only = no\nguest ok = yes\ncreate mask = 0644\ndirectory mask = 0755\n' \"\$HOME/vm_shared\" | sudo tee -a /etc/samba/smb.conf >/dev/null"
    check_or_warn_failure "$?" "adicionar share no smb.conf"
    run_validation "sudo testparm -s"

    run_cmd "sudo systemctl restart smbd"
    check_or_warn_failure "$?" "reiniciar smbd"
    run_validation "systemctl is-active smbd"

    log "No Windows, acesse: \\\\IP_DO_HOST\\vm_shared"
  fi

  generate_powershell_template
  log "Script PowerShell foi gerado em texto e nao sera executado automaticamente."
}

main() {
  parse_args "$@"
  touch "$LOG_FILE"
  log "Inicio da execucao: $SCRIPT_NAME"
  log "Log: $LOG_FILE"
  log "Modo auto: $AUTO_MODE"
  log "Skip SMB: $SKIP_SMB"
  log "ISO search dirs: $ISO_SEARCH_DIRS"
  if [[ -n "$ISO_WIN10" ]]; then
    log "ISO_WIN10 informado via parametro: $ISO_WIN10"
  fi
  if [[ -n "$ISO_WIN11" ]]; then
    log "ISO_WIN11 informado via parametro: $ISO_WIN11"
  fi
  if sudo -n true >/dev/null 2>&1; then
    log "Pre-check sudo: OK (sem prompt)."
  else
    log "Pre-check sudo: BLOQUEADO (senha interativa requerida)."
  fi

  stage_0

  if ! confirm_step "Iniciar Etapa 1 (usuarios humanos no sudo)?"; then
    pause_flow
  fi
  stage_1

  stage_2

  if ! stage_3_precheck; then
    VM_CREATION_STATUS="BLOQUEADO_KVM"
    VM_CREATION_MESSAGE="Virtualizacao indisponivel no host."
    generate_guide_doc
    generate_execution_report
    log "Fim: Etapas 1 e 2 executadas; Etapas 3 a 7 bloqueadas por virtualizacao indisponivel."
    exit 0
  fi

  stage_3_install
  if stage_4_detect_ovmf; then
    stage_5_create_vms
  fi
  stage_6_gui_instructions
  stage_7_prepare_project_test
  generate_guide_doc
  generate_execution_report

  log "Execucao concluida."
}

main "$@"
