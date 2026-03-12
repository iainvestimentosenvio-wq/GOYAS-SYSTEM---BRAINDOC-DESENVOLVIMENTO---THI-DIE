# Guia Operacional das VMs Windows (IDE/Agente)

Este e o documento oficial para operar `win10-lite` e `win11-lite` pelo terminal do host Linux.

## Estado atual real (confirmado em 2026-02-11)
- VM `win10-lite`: Windows instalado, desktop abre, snapshot `clean` criado.
- VM `win11-lite`: Windows instalado, desktop abre, snapshot `clean` criado.
- Ambas as VMs: `shut off` no momento e `autostart: enable`.
- ISO Win10 x64: `/home/u/Downloads/Win10_22H2_BrazilianPortuguese_x64v1.iso`
- ISO Win11 x64: `/home/u/Downloads/Win11_25H2_BrazilianPortuguese_x64.iso`
- Snapshot `clean` Win10: `2026-02-11 17:32:58 -0300`
- Snapshot `clean` Win11: `2026-02-11 19:43:48 -0300`
- Auditoria de host mais recente: `saida/host-audit/20260211T224413Z.md`

## O que a IDE precisa saber
- Diretorio de execucao: `INSTALADOR/`
- Script principal: `comum/scripts/windows-vm-control.sh`
- Nomes oficiais das VMs: `win10-lite` e `win11-lite`
- Operacao recomendada: sempre restaurar snapshot `clean` antes de rodada de teste
- Abrir interface grafica quando necessario: `virt-manager --connect qemu:///system`

## Comandos essenciais
```bash
bash comum/scripts/windows-vm-control.sh status
bash comum/scripts/windows-vm-control.sh start win10-lite
bash comum/scripts/windows-vm-control.sh start win11-lite
bash comum/scripts/windows-vm-control.sh display win10-lite
bash comum/scripts/windows-vm-control.sh display win11-lite
bash comum/scripts/windows-vm-control.sh screenshot win10-lite
bash comum/scripts/windows-vm-control.sh screenshot win11-lite
bash comum/scripts/windows-vm-control.sh snapshot-list win10-lite
bash comum/scripts/windows-vm-control.sh snapshot-list win11-lite
bash comum/scripts/windows-vm-control.sh restore-clean win10-lite
bash comum/scripts/windows-vm-control.sh restore-clean win11-lite
bash comum/scripts/windows-vm-control.sh stop win10-lite
bash comum/scripts/windows-vm-control.sh stop win11-lite
bash comum/scripts/windows-vm-control.sh audit
```

## Fluxo padrao para cada rodada de teste
1. Restaurar baseline:
```bash
bash comum/scripts/windows-vm-control.sh restore-clean win10-lite
# ou
bash comum/scripts/windows-vm-control.sh restore-clean win11-lite
```
2. Abrir console da VM:
```bash
bash comum/scripts/windows-vm-control.sh display win10-lite
```
3. Rodar teste manual/automatizado dentro do Windows.
4. Coletar screenshot de evidencia:
```bash
bash comum/scripts/windows-vm-control.sh screenshot win10-lite
```
5. Desligar VM no fim:
```bash
bash comum/scripts/windows-vm-control.sh stop win10-lite
```

## Quando precisar reinstalar Windows (somente excecao)
Use apenas se snapshot `clean` ficar inutilizavel.
1. Iniciar VM com ISO.
2. `Avancar` -> `Instalar agora` -> `Nao tenho chave`.
3. Escolher edicao.
4. `Personalizada`.
5. Selecionar disco de 80GB.
6. Depois da primeira copia de arquivos, nao pressionar tecla no reboot.
7. Ao chegar no desktop, criar snapshot limpo:
```bash
bash comum/scripts/windows-vm-control.sh snapshot-clean win10-lite
bash comum/scripts/windows-vm-control.sh snapshot-clean win11-lite
```

## Troubleshooting rapido

### 1) Erro no virt-manager (libvirt)
```bash
newgrp libvirt
virt-manager --connect qemu:///system
```

### 2) Caiu na tela UEFI/Shell
```bash
bash comum/scripts/windows-vm-control.sh boot-iso win10-lite
bash comum/scripts/windows-vm-control.sh boot-iso win11-lite
```

### 3) VM liga mas screenshot mostra "Guest has not initialized the display (yet)"
Isso e fase inicial de boot. Aguarde 30-90s e tente novo screenshot.

### 4) OOBE sem internet ou preso em conta Microsoft
Se precisar bypass em Windows 11 OOBE:
1. `Shift + F10`
2. Comando correto:
```cmd
oobe\bypassnro
```
3. A VM reinicia e libera modo offline/local.

## Validacao tecnica do host
```bash
sg libvirt -c 'virsh dominfo win10-lite'
sg libvirt -c 'virsh dominfo win11-lite'
sg libvirt -c 'virsh snapshot-list win10-lite'
sg libvirt -c 'virsh snapshot-list win11-lite'
sg libvirt -c "virsh dumpxml win11-lite | rg 'loader|nvram|tpm'"
bash comum/scripts/audit-host-state.sh
```

## Configuracao alvo (baseline)
- `win10-lite`: 2 vCPU, 4096MB RAM, disco 80GB, UEFI.
- `win11-lite`: 2 vCPU, 4096MB RAM, disco 80GB, UEFI + Secure Boot + TPM 2.0.
