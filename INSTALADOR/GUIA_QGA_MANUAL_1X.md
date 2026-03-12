# GUIA_QGA_MANUAL_1X

Objetivo: habilitar o `qemu-ga` uma unica vez em cada VM (`win10-lite` e `win11-lite`).
Depois disso, a regressao roda no host em modo automatico (`manual-ready`) sem send-keys.

## 1. Preparar (host Linux)
```bash
bash comum/scripts/windows-vm-control.sh start win10-lite
virt-viewer -c qemu:///system win10-lite
```

## 2. Instalar o agente dentro da VM
1. Abra `Este Computador`.
2. Abra `Unidade de CD (D:) GUESTTOOLS` (ou outra letra de CD).
3. Execute o aplicativo `A` como Administrador.
4. Conclua o instalador.

## 3. Garantir servico ativo
1. Pressione `Win + R`.
2. Digite `services.msc` e confirme.
3. Encontre `QEMU Guest Agent` (ou `qemu-ga`).
4. Configure:
- Tipo de inicializacao: `Automatico`
- Estado: `Em execucao`

Checagem rapida no PowerShell (Administrador):
```powershell
Get-Service qemu-ga | Format-Table Name,Status,StartType -Auto
Set-Service qemu-ga -StartupType Automatic
Start-Service qemu-ga
Get-Service qemu-ga | Format-Table Name,Status,StartType -Auto
```

## 4. Validar no host
```bash
sg libvirt -c "virsh qemu-agent-command win10-lite '{\"execute\":\"guest-ping\"}'"
```
Esperado: retorno JSON sem erro.

## 5. Repetir para Win11
```bash
bash comum/scripts/windows-vm-control.sh stop win10-lite
bash comum/scripts/windows-vm-control.sh start win11-lite
virt-viewer -c qemu:///system win11-lite
```
Repita os passos 2, 3 e valide:
```bash
sg libvirt -c "virsh qemu-agent-command win11-lite '{\"execute\":\"guest-ping\"}'"
```

## 6. Encerrar
```bash
bash comum/scripts/windows-vm-control.sh stop win11-lite
```

## 7. Rodada automatica (host)
Com as duas VMs ja preparadas:
```bash
RUN_ID_PROD=$(date -u +%Y%m%dT%H%M%SZ)
bash comum/scripts/windows-e2e-sequencial.sh \
  --run-id "$RUN_ID_PROD" \
  --vm-order win10-lite,win11-lite \
  --cooldown-sec 20 \
  --strict-signature \
  --bootstrap-mode manual-ready
```

## 8. Fallback rapido por VM
Use quando o bootstrap automatico falhar em apenas uma VM.

### Win10
```bash
bash comum/scripts/windows-vm-control.sh start win10-lite
virt-viewer -c qemu:///system win10-lite
```
Dentro da VM, repetir Secao 2 e 3. Depois validar no host:
```bash
sg libvirt -c "virsh qemu-agent-command win10-lite '{\"execute\":\"guest-ping\"}'"
```

### Win11
```bash
bash comum/scripts/windows-vm-control.sh start win11-lite
virt-viewer -c qemu:///system win11-lite
```
Dentro da VM, repetir Secao 2 e 3. Depois validar no host:
```bash
sg libvirt -c "virsh qemu-agent-command win11-lite '{\"execute\":\"guest-ping\"}'"
```
