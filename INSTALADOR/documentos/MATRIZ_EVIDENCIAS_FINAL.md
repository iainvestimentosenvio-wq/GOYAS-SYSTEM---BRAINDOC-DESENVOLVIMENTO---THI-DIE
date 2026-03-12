# MATRIZ EVIDENCIAS FINAL

DataBaselineUTC: 2026-02-15T00:35:00Z
DocumentoMestre: saida/RELATORIO-EXECUTIVO-FINAL.txt
RegraDeConflito: LOG BRUTO VENCE
FonteFactualPrincipal: saida/test-logs/e2e-results-20260207T174707.txt
FonteFactualComplementarLinux: saida/validacao-linux-20260211T212023Z/10.log
FonteFactualComplementarWindows: saida/validacao-windows-20260215T000453Z/windows-round-summary.md

## Criterios de rastreabilidade
1. Sem evidencia verificavel, nao ha PASS.
2. Em divergencia entre texto e log, o log bruto prevalece.
3. Itens sem execucao valida nesta baseline sao marcados como BLOQUEADO.
4. `PRE-REQUISITO` e tratado como gate de elegibilidade (fora da base pontuada da nota oficial).

## Matriz unica (Windows + Linux)

| Item | Plataforma | Resultado | Evidencia | Observacao |
| --- | --- | --- | --- | --- |
| PRE-REQUISITO | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:1` | Execucao com privilegio administrativo registrada. |
| MSI-01-EXE | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:3` | Executavel instalado em Program Files. |
| MSI-02-DESKTOP | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:5` | Atalho de Desktop criado na instalacao MSI. |
| MSI-03-STARTMENU | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:7` | Atalho de Menu Iniciar criado na instalacao MSI. |
| MSI-04-REGISTRY | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:9` | Chave e valores de registro criados. |
| MSI-05-APPDATA | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:11` | Presenca de AppData apos instalacao MSI. |
| MSI-06-LOCALAPPDATA | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:13` | Ausencia em LocalAppData registrada como comportamento correto. |
| MSI-07-APP-OPEN | Windows | MANUAL | `saida/test-logs/e2e-results-20260207T174707.txt:15` | Validacao manual pendente. |
| MSI-UNINST-01-PROGRAMFILES | Windows | FAIL | `saida/test-logs/e2e-results-20260207T174707.txt:17` | Remocao de Program Files falhou no E2E. |
| MSI-UNINST-02-DESKTOP | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:19` | Remocao de atalho Desktop confirmada. |
| MSI-UNINST-03-REGISTRY | Windows | FAIL | `saida/test-logs/e2e-results-20260207T174707.txt:21` | Limpeza de registro falhou no E2E. |
| MSI-UNINST-04-APPDATA-PRESERVED | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:23` | Preservacao de AppData confirmada. |
| MSI-UNINST-05-SENTINEL-PRESERVED | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:25` | Preservacao do sentinela confirmada. |
| INNO-01-EXE | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:27` | Executavel instalado em Program Files. |
| INNO-02-DESKTOP | Windows | FAIL | `saida/test-logs/e2e-results-20260207T174707.txt:29` | Atalho de Desktop nao validado no E2E. |
| INNO-03-STARTMENU | Windows | FAIL | `saida/test-logs/e2e-results-20260207T174707.txt:31` | Atalho de Menu Iniciar nao validado no E2E. |
| INNO-04-REGISTRY | Windows | FAIL | `saida/test-logs/e2e-results-20260207T174707.txt:33` | Registro nao validado no E2E. |
| INNO-05-APPDATA | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:35` | Presenca de AppData apos instalacao Inno. |
| INNO-06-APP-OPEN | Windows | MANUAL | `saida/test-logs/e2e-results-20260207T174707.txt:37` | Validacao manual pendente. |
| INNO-UNINST-01-PROGRAMFILES | Windows | FAIL | `saida/test-logs/e2e-results-20260207T174707.txt:39` | Remocao de Program Files falhou no E2E. |
| INNO-UNINST-02-APPDATA-PRESERVED | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:41` | Preservacao de AppData confirmada. |
| UPGRADE-01-SENTINEL-PRESERVED | Windows | PASS | `saida/test-logs/e2e-results-20260207T174707.txt:43` | Upgrade/reinstalacao preservou sentinela. |
| MSI-SILENT-INSTALL-CMD | Windows | PASS | `saida/entrega-final/EVIDENCIAS-TESTE.md:63` | Comando silencioso registrado; resultado funcional refletido nos itens MSI. |
| MSI-SILENT-UNINSTALL-CMD | Windows | PASS | `saida/entrega-final/EVIDENCIAS-TESTE.md:69` | Comando silencioso registrado; limpeza funcional validada pelos itens MSI-UNINST-*. |
| INNO-SILENT-INSTALL-CMD | Windows | PASS | `saida/entrega-final/EVIDENCIAS-INNO-SETUP.md:26` | Execucao silenciosa registrada; comportamento funcional validado pelos itens INNO-*. |
| INNO-SILENT-UNINSTALL-CMD | Windows | PASS | `saida/entrega-final/EVIDENCIAS-INNO-SETUP.md:140` | Execucao silenciosa registrada; limpeza funcional validada pelos itens INNO-UNINST-*. |
| LNX-DEB-INSTALL | Linux | BLOQUEADO | `saida/test-logs/20260210T222123Z/protons-linux-deb-20260210T222123Z.log:36` | Execucao valida registrada, mas install bloqueado por `sudo -n` indisponivel nesta sessao. |
| LNX-DEB-UNINSTALL | Linux | BLOQUEADO | `saida/test-logs/20260210T222123Z/protons-linux-deb-20260210T222123Z.log:36` | Uninstall nao executado na rodada por bloqueio de privilegio `sudo`. |
| LNX-DEB-SILENT-NONINTERACTIVE | Linux | BLOQUEADO | `saida/test-logs/20260209T234222Z/linux-round-summary-20260209T234222Z.md` | Nao houve execucao dedicada de modo silencioso DEB. |
| LNX-APPIMAGE-EXEC | Linux | PASS | `saida/test-logs/20260210T222123Z/protons-linux-appimage-20260210T222123Z.log:22` | AppImage oficial executavel e validado com 5/5 testes PASS. |
| LNX-UPGRADE-ROLLBACK | Linux | BLOQUEADO | `saida/test-logs/20260209T234222Z/linux-round-summary-20260209T234222Z.md` | Nao houve cenario de upgrade/rollback Linux nesta rodada. |
| LNX-CLEANUP-POST-UNINSTALL | Linux | BLOQUEADO | `saida/test-logs/20260209T234222Z/linux-round-summary-20260209T234222Z.md` | Sem evidencia de uninstall completo Linux para validar limpeza. |
| LNX-VAL-01-PROHIBITED-TERMS | Linux | PASS | `saida/validacao-linux-20260211T212023Z/01.log` | Validador de termos proibidos executado sem ocorrencias na rodada. |
| LNX-VAL-02-LINT-BUILD | Linux | PASS | `saida/validacao-linux-20260211T212023Z/02.log` | Lint de build executado com sucesso. |
| LNX-VAL-03-VERSION | Linux | PASS | `saida/validacao-linux-20260211T212023Z/03.log` | Consistencia de versao validada. |
| LNX-VAL-04-SCRIPT-COMMENTS | Linux | PASS | `saida/validacao-linux-20260211T212023Z/04.log` | Sem TODO/FIXME obsoleto nos scripts alvo. |
| LNX-VAL-05-SBOM | Linux | PASS | `saida/validacao-linux-20260211T212023Z/05.log` | SBOM CycloneDX valido (39 componentes, versao 1.0.0). |
| LNX-VAL-06-MANIFEST-STRICT | Linux | PASS | `saida/validacao-linux-20260211T212023Z/06.log` | `validate-update-manifest --strict-signature` em PASS. |
| LNX-VAL-07-MANIFEST-TESTS | Linux | PASS | `saida/validacao-linux-20260211T212023Z/07.log` | Suite de update-manifest em PASS (com cenarios negativos). |
| LNX-VAL-08-HOST-AUDIT | Linux | PASS | `saida/host-audit/20260211T212025Z.md` | Host audit confirma grupos, KVM, libvirt e ISOs presentes. |
| LNX-VAL-09-COLLECT-EVIDENCE | Linux | PASS | `saida/validacao-linux-20260211T212023Z/09.log` | Coleta de evidencias Linux concluida em `saida/test-logs/20260211T212025Z`. |
| LNX-VAL-10-APPIMAGE-TEST | Linux | PASS | `saida/validacao-linux-20260211T212023Z/10.log` | Teste AppImage 5/5 PASS na rodada atual. |
| LNX-VAL-11-DEB-TEST | Linux | BLOQUEADO | `saida/validacao-linux-20260211T212023Z/11.log` | Execucao parcial: testes 3-6 SKIP por ausencia de `sudo -n` na sessao. |
| LNX-VAL-12-CONTRACT-STATIC | Windows | PASS | `saida/validacao-linux-20260211T212023Z/12.log` | Contrato estatico de instalador validado em PASS. |
| LNX-VAL-13-CHECKLIST-GRAFICOS | Meta | PASS | `saida/validacao-linux-20260211T212023Z/13.log` | Consistencia estrutural do checklist validada em PASS. |

## Totais consolidados da matriz (bruto, inclui `PRE-REQUISITO`)

- PASS: 31
- FAIL: 6
- MANUAL: 2
- BLOQUEADO: 6
- TOTAL: 45

## Totais oficiais para nota (base pontuada, exclui `PRE-REQUISITO`)

- PASS: 30
- FAIL: 6
- MANUAL: 2
- BLOQUEADO: 6
- TOTAL: 44
- Nota oficial: 70.5 (arredondado para 71/100)

## Totais do E2E Windows (nucleo funcional)

- PASS: 13
- FAIL: 6
- MANUAL: 2
- PENDENTE: 0

## Evidencias complementares da rodada Windows 2026-02-15 (nao pontuadas)

- Escopo: validacao de infraestrutura de VM fraca + bootstrap QGA + tentativa de regressao in-guest sequencial.
- Resultado: infraestrutura `PASS`; hardening tecnico (preflight CD-ROM + attach/cleanup de midia helper) `PASS` em `win10-lite`; automacao in-guest `BLOQUEADO` por `QEMU guest agent is not connected`; sequencia pausada antes do ciclo `win11-lite`.
- Evidencias:
  - `saida/validacao-windows-20260214T233745Z/` (baseline pre-mudanca)
  - `saida/validacao-windows-20260214T233745Z/win10-guest-ping.err`
  - `saida/validacao-windows-20260214T233745Z/win11-guest-ping.err`
  - `saida/validacao-windows-20260215T000453Z/windows-round-summary.md`
  - `saida/validacao-windows-20260215T000453Z/resumo.csv`
  - `saida/validacao-windows-20260215T000453Z/gates-summary.md`
  - `saida/validacao-windows-20260215T000453Z/final_vm_status_after_suite.log`
  - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_qga_channel.log`
  - `saida/validacao-windows-20260215T000453Z/win10-lite_sanitize_cdrom.log`
  - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_change_media.log`
  - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_cleanup_media.log`
  - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_qga_precheck.log`
  - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_qga_wait.log`
  - `saida/validacao-windows-20260215T000453Z/manual-actions-win10-lite.md`
  - `saida/validacao-windows-20260215T000453Z/mandatory-suite.csv`
  - `saida/validacao-windows-20260215T000453Z/resumo-simples.md`

## Evidencias complementares da rodada paralela 2 2026-02-15 (nao pontuadas)

- Escopo: validacoes Linux/meta + reconciliacao de checklist sem tocar VMs Windows.
- Resultado: trilha paralela 11/11 PASS; meta-validacoes adicionais PASS; decisao automatica de gate permaneceu `NO-GO` (coerente com gates Windows pendentes).
- Evidencias:
  - `saida/validacao-paralela-20260215T101334Z/resumo.csv`
  - `saida/validacao-paralela-20260215T101334Z/manifesto-evidencias.md`
  - `saida/validacao-paralela-20260215T101334Z/90-release-hashes.log`
  - `saida/validacao-paralela-20260215T101334Z/91-no-debug-symbols.log`
  - `saida/validacao-paralela-20260215T101334Z/92-changelog-format.log`
  - `saida/validacao-paralela-20260215T101334Z/93-go-nogo-gate.log`
  - `saida/validacao-paralela-20260215T101334Z/go-nogo-20260215T101334Z.md`

## Referencias cruzadas

- `saida/RELATORIO-EXECUTIVO-FINAL.txt`
- `saida/RELATORIO-EXECUTIVO-FINAL.txt`
- `saida/entrega-final/README.md`
- `documentos/RELEASE_APPROVAL.md`
- `saida/validacao-linux-20260211T212023Z/resumo.csv`
- `saida/validacao-linux-20260211T212023Z/`
- `saida/validacao-windows-20260215T000453Z/windows-round-summary.md`
- `saida/validacao-windows-20260215T000453Z/resumo.csv`
- `saida/validacao-windows-20260215T000453Z/gates-summary.md`
- `saida/host-audit/20260215T004642Z.md`
- `saida/test-logs/20260211T212025Z/collect.log`
- `saida/test-logs/20260210T223147Z/collect.log`
