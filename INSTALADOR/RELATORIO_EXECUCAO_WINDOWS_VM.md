# Relatorio de Execucao - Admin + KVM + VMs Windows

- Data UTC: `2026-02-15T00:35:00Z`
- Script base: `setup_vm_windows.sh`
- Log base: `./setup_vm_windows_log.txt`
- Rodada factual mais recente: `saida/validacao-windows-20260215T000453Z/`

## Resultado por etapa
- Etapa 1 (usuarios humanos em sudo): OK
- Etapa 2 (/etc/adduser.conf): OK
- Etapa 3 (stack KVM/libvirt): OK
- Etapa 4 (OVMF/Secure Boot): OK
- Etapa 5 (criacao de VMs): OK
- Etapa 6 (snapshots `clean`): OK
- Etapa 7 (automacao de validacao funcional in-guest): BLOQUEADO

## Evidencias tecnicas atuais
- vmx/svm: `16`
- /dev/kvm: `PRESENTE`
- modulos kvm: `kvm_intel,kvm`
- libvirtd: `active`
- rede `default`: `active` + `autostart`
- win10-lite: `CRIADA` (2 vCPU, 4096MB, 80GB, UEFI, SATA, e1000e)
- win11-lite: `CRIADA` (2 vCPU, 4096MB, 80GB, UEFI Secure Boot, TPM 2.0, SATA, e1000e)
- snapshots: `clean` em ambas
- ISOs: Win10 e Win11 anexadas nas VMs
- desktops Windows: `FUNCIONAIS` (capturas de 2026-02-12)

## Evidencias da rodada 2026-02-15 (run id `20260215T000453Z`)
- baseline pre-mudanca: `saida/validacao-windows-20260214T233745Z/`
- resumo: `saida/validacao-windows-20260215T000453Z/windows-round-summary.md`
- CSV: `saida/validacao-windows-20260215T000453Z/resumo.csv`
- gates: `saida/validacao-windows-20260215T000453Z/gates-summary.md`
- bootstrap QGA Win10 (canal): `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_qga_channel.log`
- preflight CD-ROM Win10: `saida/validacao-windows-20260215T000453Z/win10-lite_sanitize_cdrom.log`
- attach de ISO helper Win10: `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_change_media.log`
- cleanup de ISO helper Win10: `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_cleanup_media.log`
- timeout de `guest-ping` Win10: `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_qga_wait.log`
- fallback manual gerado: `saida/validacao-windows-20260215T000453Z/manual-actions-win10-lite.md`
- suite obrigatoria: `saida/validacao-windows-20260215T000453Z/mandatory-suite.csv`
- estado final das VMs apos suite: `saida/validacao-windows-20260215T000453Z/final_vm_status_after_suite.log`
- host audit da rodada: `saida/host-audit/20260215T004642Z.md`
- resumo simples: `saida/validacao-windows-20260215T000453Z/resumo-simples.md`

## Melhorias reais obtidas nesta rodada
- `sanitize_cdrom_source` executado em `win10-lite` antes do bootstrap.
- Troca de midia (`change-media`) passou em `win10-lite` com ISO helper em caminho estavel legivel pelo QEMU.
- Limpeza da midia helper no fim do bootstrap foi executada com sucesso.
- Orquestrador sequencial pausou corretamente no fallback manual quando o gate tecnico falhou.

## Bloqueio atual para fechar validacao funcional Windows
- `virsh qemu-agent-command` ainda retorna `QEMU guest agent is not connected` no `win10-lite`.
- Sem `guest-ping` em PASS no Win10, a sequencia foi pausada antes da etapa equivalente do Win11 nesta rodada.
- Sem conexao ativa do agente no SO Windows, a regressao `run-regressao.ps1` nao iniciou automaticamente.

## Prontidao para teste do instalador
- Infra de maquina fraca: PRONTA
- Hardening de bootstrap (canal/CD-ROM/midia): PRONTO
- Regressao funcional E2E dentro do Windows: PENDENTE (depende do servico `qemu-ga` conectar no SO Windows)
