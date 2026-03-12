# PLANO DE 7 DIAS - UX 100 + RESILIENCIA/ROLLBACK (ORDEM TECNICA OTIMIZADA)

Data de inicio: 2026-02-18
Ultima revisao: 2026-02-19
Status: em execucao (trilha base FINAL2 concluida; pendencias no gate UX/P10 estendido)
Escopo: elevar UX e Resiliencia sem quebrar os gates tecnicos atuais (G1-G4)

Fonte oficial de status operacional:
- `saida/status/latest-status.json` (plano e checklist sao narrativos/historicos).
- Execucao operacional ativa: `run-windows-base-gate.sh`, `run-windows-p10-gate.sh`, `run-windows-ux-p10-gate.sh` (`--help` nao executa fluxo).
- Transporte de payload na automacao ativa: `iso-strict` por padrao; `iso`/`auto` com fallback para diagnostico.
- Canary curto obrigatorio antes da rodada longa: `run-windows-canary.sh` (bypass emergencial: `PROTONS_SKIP_CANARY=1`).
- Status canonico exige `code_revision` e `status_stale` antes de liberar teste pesado.
- Perfil padrao para pre-teste tecnico: `technical`.
- Bloqueador operacional atual (2026-02-21): freshness de artefatos em FAIL ate rebuild real (`build-msi.ps1` + `build-inno.ps1`).

## Status rapido (2026-02-19)

- Run base validado: `FINAL2-20260219195627`.
- Resultado da rodada base: `PASS 70 | FAIL 0 | PARCIAL 4`.
- Gates base: `G1 PASS | G2 PASS | G3 PASS | G4 PASS`.
- Run estendido UX/P10 mais recente: `UXP10-GATE-20260219010135`.
- Resultado estendido: `PASS 96 | FAIL 7 | PARCIAL 6`.
- Gates estendidos: `G1 PASS | G2 NAO | G3 PASS | G4 PASS`.
- Pacote UX PT-BR dinamico do Inno implementado em codigo (pipeline dual-theme + estilo Windows 11 + checks UX 13..17).
- Conclusao: base de producao tecnica esta estavel; trilha UX/P10 ainda exige correcoes para `MSI-ROLLBACK`, `INNO-ROLLBACK`, `TX-INSTALL`, `CANCEL-INSTALL` (win10/win11) e `POWER-RECOVERY` no win10.

## 1) Diagnostico tecnico (codigo real de hoje)

Base observada:
- `testes/windows/run-regressao.ps1` cobre base E2E e suites extras de resiliencia/UX via flags.
- Existem suites dedicadas de rollback/resiliencia em `testes/windows/` (MSI-ROLLBACK, INNO-ROLLBACK, TX-INSTALL, POWER-RECOVERY, MSI-REPAIR).
- MSI ja tem base forte para upgrade/cleanup (WiX `MajorUpgrade` + `RemoveFolderEx`).
- Inno ja possui cleanup custom para falha/cancelamento, porem ainda com falhas nos testes negativos.
- UX do Inno evoluiu (Wizard moderno + progresso + ETA), mas ainda com pendencia no fluxo de cancelamento limpo.

Conclusao tecnica:
- O maior risco nao e "fazer UX bonita"; e fazer UX + resiliencia sem perder previsibilidade de install/uninstall.
- A ordem mais rapida e segura e: primeiro testes/resiliencia base, depois UX, depois gate final completo.

## 2) Objetivos do ciclo

Metas:
- UX: 25 -> 85+ validado.
- Resiliencia/Rollback: 15 -> 55+ validado (stretch 70+).

Regras:
1. Nenhuma regressao em install/uninstall/upgrade.
2. Toda mudanca de resiliencia precisa de teste negativo com evidencia.
3. Toda mudanca visual precisa de smoke em Win10 e Win11.
4. Logo oficial da empresa sera usada nas telas e icones.

Decisao de escopo (fechada para este ciclo):
- ENTRA AGORA:
  - Progress bar customizada (instalacao).
  - Tempo estimado de instalacao (ao menos faixa aproximada + fallback).
  - Cancelamento gracioso com limpeza segura.
  - Rollback em falha (MSI + cleanup equivalente no Inno).
  - Compatibilidade com antivirus via teste real com Windows Defender ativo.
- FICA PARA DEPOIS:
  - Deteccao inteligente/whitelisting automatico de antivirus.
  - Compatibilidade ARM64 (novo build, novo pipeline e nova matriz de testes).

## 3) Ordem otimizada para velocidade

Sequencia escolhida:
1. Fase A (Dias 1-3): blindar resiliencia e testes.
2. Fase B (Dias 4-5): executar UX forte com base tecnica estavel.
3. Fase C (Dias 6-7): hardening final + gates.

Por que esta ordem e mais rapida:
- Evita refazer UX por causa de falha estrutural descoberta tarde.
- Traz falha de rollback para o inicio (quando custo de correcao e menor).
- Usa loop canario (1 VM) antes de gastar rodada completa (2 VMs).

## 4) Loop de execucao rapido (obrigatorio)

A cada bloco de alteracoes:
1. Canary Win10 (rapido):
```bash
bash comum/scripts/windows-e2e-sequencial.sh --vm-order win10-lite --cooldown-sec 0 --bootstrap-mode auto
```
2. Confirmacao Win11:
```bash
bash comum/scripts/windows-e2e-sequencial.sh --vm-order win11-lite --cooldown-sec 0 --bootstrap-mode auto
```
3. Gate completo (apenas quando 1 e 2 passarem):
```bash
bash run-windows-ux-p10-gate.sh
```

Regra de eficiencia:
- Nao rodar rodada completa se canario falhar.
- Corrigir no canario e repetir.

## 4.1) Regra obrigatoria apos mudanca no painel (UX visual)

Qualquer alteracao no painel do instalador (imagem de fundo, copy, layout, icones, fluxo de botoes) exige revalidacao antes de liberar:

1. Validacao minima:
```bash
bash run-windows-base-gate.sh
```
2. Validacao de fechamento UX/P10 (obrigatoria antes de publicar):
```bash
bash run-windows-ux-p10-gate.sh
```

Regra de release:
- Sem rodada nova apos mudanca de painel = NAO liberar.
- `Exit code 0` sozinho nao basta; precisa `gates-summary.md` conforme alvo do ciclo.

## 5) Entregaveis tecnicos obrigatorios (faltavam no plano antigo)

Scripts novos em `testes/windows/`:
- `test-msi-rollback.ps1`
- `test-inno-rollback.ps1`
- `test-transactional-install.ps1`
- `test-power-failure-recovery.ps1`
- `test-msi-repair.ps1` (P1)

Docs novas em `documentos/`:
- `RESILIENCIA_TEST_STRATEGY.md`
- `UX_STYLE_GUIDE_INSTALADOR.md`
- `UX_COPY_INSTALADOR.md`
- `UX_ERROR_MAP.md`
- `UX_I18N_CHECKLIST.md`

Padrao de evidencia:
- `saida/ux-diaX/`
- `saida/ux-diaX/rollback/`
- sempre com `run_id`, VM e veredito PASS/FAIL no nome do arquivo.

## 6) Plano de 7 dias (reordenado)

## DIA 1 - Baseline tecnico + estrategia de falha

Objetivo:
- Travar baseline e preparar trilha de resiliencia com criterio objetivo.

Checklist:
- [x] Congelar baseline atual (G1-G4 + tempos p95 de referencia).
- [x] Criar `documentos/RESILIENCIA_TEST_STRATEGY.md` com matriz de falhas.
- [x] Definir criterios de PASS para rollback MSI e cleanup Inno.
- [x] Criar estrutura de evidencias por dia (`saida/ux-dia1/...`).

Saida minima do dia:
- Baseline documentada e rastreavel.

## DIA 2 - MSI rollback real (primeiro pilar critico)

Objetivo:
- Ter rollback MSI validado por teste reproduzivel.

Checklist:
- [x] Criar `testes/windows/test-msi-rollback.ps1`.
- [x] Implementar metodo de falha injetada controlada para validar rollback.
- [ ] Validar ausencia de lixo pos-falha (arquivos, atalhos, registro).
- [x] Rodar canario/rodada em Win10 e Win11 para este cenario.

Saida minima do dia:
- Pelo menos 1 teste negativo MSI com PASS em Win10 e Win11.

## DIA 3 - Inno resiliencia (cleanup + interrupcao)

Objetivo:
- Cobrir principal gap de Inno: falha/cancelamento/interrupcao.

Checklist:
- [x] Criar `testes/windows/test-inno-rollback.ps1`.
- [x] Criar `testes/windows/test-transactional-install.ps1`.
- [x] Implementar cleanup custom em `windows/innosetup/protons-setup.iss` para falha/cancelamento.
- [x] Testar kill de processo/cancelamento em ponto medio.

Saida minima do dia:
- Inno com cleanup validado em ao menos 2 cenarios negativos.

## DIA 4 - UX fundacao (branding + fluxo + microcopy)

Objetivo:
- Subir qualidade visual sem tocar no core tecnico ja estabilizado.

Checklist:
- [x] Aplicar guia visual (logo, contraste, hierarquia, consistencia).
- [x] Aplicar fundo oficial do login no wizard Inno sem distorcao.
- [x] Aplicar tema dinamico light/dark no wizard Inno.
- [x] Forcar painel Inno para PT-BR only.
- [x] Revisar textos criticos (boas-vindas, confirmacao, sucesso, erro).
- [x] Criar `documentos/UX_STYLE_GUIDE_INSTALADOR.md`.
- [x] Criar `documentos/UX_COPY_INSTALADOR.md`.
- [ ] Revalidar no Win10 e Win11 apos cada alteracao visual relevante.

Saida minima do dia:
- Fluxo visual coerente e textos claros.

## DIA 5 - UX operacional (progresso, erros, idiomas)

Objetivo:
- Melhorar percepcao de controle e reduzir friccao real do usuario.

Checklist:
- [x] Implementar progress bar customizada com etapa atual.
- [x] Exibir tempo estimado de instalacao (faixa aproximada + fallback "calculando...").
- [ ] Implementar cancelamento gracioso com confirmacao e cleanup minimo seguro.
- [x] Implementar mapa de erros com acao sugerida.
- [ ] Validar PT-BR (somente) sem truncamento.
- [x] Criar `documentos/UX_ERROR_MAP.md` e `documentos/UX_I18N_CHECKLIST.md`.

Saida minima do dia:
- UX funcional em PT-BR (somente) com mensagens consistentes.

## DIA 6 - Integracao final (UX + resiliencia no runner)

Objetivo:
- Integrar e endurecer a execucao para reduzir risco de regressao.

Checklist:
- [x] Criar `testes/windows/test-power-failure-recovery.ps1`.
- [x] Criar `testes/windows/test-msi-repair.ps1` (P1).
- [x] Criar `testes/windows/test-defender-compatibility.ps1` e validar com Windows Defender ativo.
- [x] Integrar chamada dos novos testes no fluxo de regressao (direto ou via runner unificado).
- [x] Executar rodada dupla Win10+Win11 com evidencias completas.

Saida minima do dia:
- Suite integrada, sem quebra de G1-G4.

## DIA 7 - Gate final e fechamento oficial

Objetivo:
- Fechar o ciclo com prova tecnica e prova de experiencia.

Checklist:
- [x] Rodar gate base `bash run-windows-base-gate.sh` (`FINAL2-20260219195627`) com `G1-G4 PASS`.
- [x] Rodar gate estendido `bash run-windows-ux-p10-gate.sh` (`UXP10-GATE-20260219010135`).
- [ ] Rodar bateria final de resiliencia (MSI + Inno) nas 2 VMs com PASS (execucao manual final pendente).
- [ ] Atualizar score do Pilar 8 (UX) e Pilar 10 (Resiliencia/Rollback) com base em `saida/status/latest-status.json`.
- [ ] Publicar relatorios finais em `saida/ux-dia7/` gerados automaticamente a partir de `saida/status/latest-status.json`.

Saida minima do dia:
- Veredito GO/NO-GO do ciclo UX + Resiliencia.

## 7) Matriz minima de validacao diaria

| Teste | Win10 | Win11 | Resultado esperado |
| --- | --- | --- | --- |
| Install + abrir app + uninstall | obrigatorio | obrigatorio | PASS |
| Rollback MSI (falha injetada) | obrigatorio (dias 2+) | obrigatorio (dias 2+) | PASS |
| Cleanup Inno apos falha/cancel | obrigatorio (dias 3+) | obrigatorio (dias 3+) | PASS |
| Interrupcao de install (kill/cancel) | obrigatorio (dias 3+) | obrigatorio (dias 3+) | estado consistente |
| UX visual + microcopy | obrigatorio (dias 4+) | obrigatorio (dias 4+) | OK |
| PT-BR (somente) | obrigatorio (dias 5+) | obrigatorio (dias 5+) | OK |
| Defender ativo durante install/uninstall | obrigatorio (dia 6+) | obrigatorio (dia 6+) | PASS |

## 8) Riscos e mitigacao

1. Risco: rollback falso positivo (teste fraco).
Mitigacao: falha injetada reproduzivel + verificacao de arquivos/registro/atalhos.

2. Risco: Inno sem limpeza completa apos interrupcao.
Mitigacao: teste de kill de processo e teste de cancelamento tardio todo dia ate estabilizar.

3. Risco: UX bonita com regressao funcional.
Mitigacao: canario obrigatorio apos cada bloco e gate completo no final.

4. Risco: ciclo atrasar por rodada pesada em duas VMs.
Mitigacao: usar canario 1 VM como filtro antes da rodada completa.

## 9) Criterio de fechamento

GO do ciclo somente se:
1. UX >= 85.
2. Resiliencia/Rollback >= 55 com evidencia negativa real (ideal 70+).
3. Win10 e Win11 validados.
4. G1-G4 mantidos.
5. Evidencias publicadas e rastreaveis.

Responsavel: time do instalador
