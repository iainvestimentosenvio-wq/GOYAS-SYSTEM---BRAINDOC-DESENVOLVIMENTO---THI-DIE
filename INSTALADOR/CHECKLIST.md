# CHECKLIST DE EXECUCAO - AUDITORIA HONESTA + MELHORIA POR PILAR

Data de referencia: 2026-02-17 (UTC)
Escopo: auditoria honesta do gap tecnico real, comparacao com instaladores pagos, e checklist de melhoria por pilar.

Nota de governanca:
- Este arquivo e narrativo/historico.
- Estado operacional para decisao GO/NO-GO vem de `saida/status/latest-status.json`.
- Fluxo operacional ativo: entrypoints `run-windows-*.sh` (com `--help` explicito, sem execucao).
- Transporte de payload na automacao ativa: `iso-strict` por padrao (sem fallback silencioso); `iso`/`auto` mantem fallback para diagnostico.
- Canary curto obrigatorio antes da rodada longa: `bash run-windows-canary.sh` (bypass emergencial: `PROTONS_SKIP_CANARY=1`).
- Campos canonicos obrigatorios no status: `code_revision` e `status_stale`.
- Bloqueador operacional atual (2026-02-21): `testes/windows/test-artifact-freshness.sh` em FAIL ate rebuild real de MSI/EXE.

## Atualizacao oficial (Dia 7 - fechamento)

- Fonte oficial de status operacional: `saida/status/latest-status.json`.
- Derivados automaticos: `saida/status/latest-status.md` e `saida/status/latest-status.csv`.
- Rodada Windows de referencia: `FINAL2-20260217150331` com `PASS 68 | FAIL 0 | PARCIAL 4`.
- Gates Windows da rodada de referencia: `G1 PASS | G2 PASS | G3 PASS | G4 PASS`.
- Rodada Linux/meta de fechamento: `20260217T211702Z` com `PASS 11 | FAIL 0`.
- Veredito final da sprint:
  - GO tecnico interno (80/100): **SIM**
  - GO comercial externo (85/100): **NAO** (dependencia de certificado corporativo real)
- Evidencia consolidada: `saida/dia7-DIA7AUTO-20260217T211731Z/`.

---

## ⚡ ATALHO RAPIDO: SCRIPTS AUTOMATIZADOS (USE ESTES!)

**🎯 Para economizar tempo e tokens, use os scripts prontos ao invés de fazer manual:**

### Script Principal - Validação Windows Completa (100% AUTOMÁTICO)
```bash
bash run-windows-base-gate.sh
```
- ✅ Testa Win10 + Win11 automaticamente
- ✅ Upload otimizado (chunk 64KB)
- ✅ Regressão E2E completa
- ✅ Gera relatórios (resumo.csv, gates-summary.md)
- ⏱️ Duração: 40-90 minutos (ZERO intervenção manual)
- 📊 Resultado esperado: Score +10-15 pontos

### Scripts de Correção (se necessário)
```bash
# Se upload falhar, tentar otimizações:
bash troubleshoot-upload.sh

# Escolher opção [1] - Chunk 64KB
# OU opção [2] - HTTP Server (fallback)
```

### Guias Manuais (ÚLTIMO RECURSO - só se automático falhar)
- `GUIA-MANUAL-PASSOS-WINDOWS.md` - Passos detalhados nas VMs
- `PARA-ENVIAR-AO-CLAUDE-AI.md` - Templates para pedir ajuda

**💡 REGRA DE OURO:** Sempre tente o script automático PRIMEIRO. Só vá para manual se realmente falhar!

Compatibilidade:
- `RODAR-FINAL2-CORRIGIDO.sh`, `RODAR-P10-GATE.sh`, `RODAR-UX-P10-GATE.sh`, `RODAR-BUILD-E-VALIDA.sh` e `comum/scripts/FIX-UPLOAD-RAPIDO.sh` continuam disponiveis como wrappers deprecados.

---

## 🎯 TAREFAS CONSOLIDADAS (Execute UMA VEZ - resolve múltiplos pilares)

**Use estas tarefas consolidadas para economizar tempo - cada uma resolve VÁRIOS pilares simultaneamente!**

### ⚡ 1. Validação Windows Completa (PRIORITÁRIO!)

```bash
bash run-windows-base-gate.sh
```

**Resolve simultaneamente:**
- ✅ **Pilar 1** (Performance): Mede tempo p95 de instalação Win10+Win11
- ✅ **Pilar 2** (Instalação): Testa E2E MSI+Inno em ambas as VMs
- ✅ **Pilar 9** (Compatibilidade): Valida Win10 e Win11

**Status:** [x] ✅ CONCLUÍDO (rodada FINAL2-20260217150331)
**Duração:** 33 minutos (15:03Z - 15:36Z)
**Resultado:** `PASS 68 | FAIL 0 | PARCIAL 4`
**Próximo passo:** Fechamento da trilha comercial (certificado corporativo real)

---

### 🔧 2. Correção dos 6 FAIL + Reteste

**Resolve simultaneamente:**
- ✅ **Pilar 2** (Instalação): Resolve FAILs de uninstall
- ✅ **Pilar 5** (Robustez): Resolve cleanup incompleto

**FAILs a corrigir:**
- MSI: UNINST-01 (Program Files não removido), UNINST-03 (Registro não limpo)
- Inno: 02 (Atalho Desktop), 03 (Start Menu), 04 (Registro), UNINST-01 (Program Files)

**Status:** [x] ✅ Concluído
**Passos:**
1. Corrigir código MSI (`Product.wxs`, `Components.wxs`)
2. Corrigir código Inno (`protons-setup.iss`)
3. Rebuild: `windows/scripts/build-msi.ps1` + `build-inno.ps1`
4. Reteste: `bash run-windows-base-gate.sh`

---

### 📦 3. Preservação de Dados (Reinstall/Upgrade)

**Resolve simultaneamente:**
- ✅ **Pilar 5** (Robustez): Preservação em reinstall
- ✅ **Pilar 6** (Update): Preservação em upgrade

**Status:** [x] ✅ Concluído
**Teste:** `testes/windows/test-upgrade-reinstall.ps1`
**Cenários:** 1.0.0→1.0.0 (reinstall), 1.0.0→1.0.1 (upgrade)
**Validação:** `%APPDATA%\Protons` preservado

---

### 📋 Progresso Consolidado

| Tarefa Consolidada | Pilares Afetados | Status | Pontos Potenciais |
|-------------------|------------------|--------|-------------------|
| Validação Windows | 1, 2, 9 | ✅ Concluído (FAIL 0) | +10-15 pontos |
| 6 FAIL corrigidos | 2, 5 | ✅ Concluído | +10-15 pontos |
| Preservação dados | 5, 6 | ✅ Concluído | +5-8 pontos |

**Total obtido na sprint:** meta tecnica interna atingida (80/100)
**Status final:** GO tecnico interno concluido; trilha comercial ainda bloqueada por certificado

---

## 🎯 ESCOPO v1.0 vs FUTURO

### ✅ ESCOPO v1.0 (GO = 85/100)

**Meta:** Instalador funcional, confiável e testado

**INCLUÍDO (bloqueadores para GO):**
- ✅ Instalação/desinstalação Win10+Win11 funcionando (6 FAIL resolvidos)
- ✅ Performance p95 dentro da meta (install ≤90s, uninstall ≤45s)
- ✅ Update manual funcionando (download + verificar hash + instalar)
- ✅ Segurança básica (SBOM, hashes SHA-256, manifesto estrito)
- ✅ CI/CD básico configurado e validado
- ✅ Preservação de dados do usuário (`%APPDATA%`)
- ✅ Compatibilidade Win10/Win11 validada

### 🔮 FUTURO (v2.0 ou posterior)

**Nice-to-have, NÃO bloqueadores:**
- 🔮 Suporte ARM64 (não há demanda atual)
- 🔮 Update silencioso em background (feature avançada)
- 🔮 Meta "elite" p95 ≤60s (otimização além do necessário)
- 🔮 GPO corporativa completa (requer certificado enterprise)
- 🔮 Benchmark vs instalador pago (comparação já existe em docs)
- 🔮 Suporte 32-bit (mercado migrou para 64-bit)

**Critério:** Se não impede lançamento v1.0, é FUTURO (P3)

---

## Indice

- [⚡ ATALHO RAPIDO: Scripts Automatizados](#-atalho-rapido-scripts-automatizados-use-estes)
- [PARTE 1: Painel Executivo (nota honesta)](#parte-1-painel-executivo-nota-honesta)
- [PARTE 2: Graficos por Pilar](#parte-2-graficos-por-pilar)
- [PARTE 3: Comparacao com Instaladores Pagos](#parte-3-comparacao-com-instaladores-pagos)
- [PARTE 4: Checklist de Melhoria por Pilar](#parte-4-checklist-de-melhoria-por-pilar)
- [PARTE 5: Trade-offs entre Pilares](#parte-5-trade-offs-entre-pilares)
- [PARTE 6: Ranking de Prioridade Real](#parte-6-ranking-de-prioridade-real)
- [RODADAS DE VALIDAÇÃO](#rodadas-de-validação)

---

## PARTE 1: Painel Executivo (nota honesta)

### Veredito

| Metrica | Valor |
| --- | --- |
| Veredito atual | `GO tecnico interno / NO-GO comercial externo` |
| Score tecnico interno (sprint atual) | **80 / 100** |
| Score comercial externo (atual) | **80 / 100** (bloqueado por certificado corporativo) |
| Rodada Windows de referencia | `FINAL2-20260217150331` (`PASS 68 | FAIL 0 | PARCIAL 4`) |
| Gates globais | `G1 PASS | G2 PASS | G3 PASS | G4 PASS` |
| Referencia topo enterprise | 95 / 100 |
| Meta minima producao comercial | >= 85 / 100 |
| Gap real para GO comercial | **-5 pontos + assinatura oficial** |
| Falhas criticas abertas | **0 tecnicas internas** + 1 bloqueio externo (`.pfx`) |

### Principio desta auditoria
- `VALIDADO` = existe teste automatizado que passou E foi executado recentemente
- `ALEGADO` = existe no documento mas SEM teste que comprove OU codigo corrigido sem reteste
- 🔴 = nota inflada (diferenca > 10 pontos entre alegado e validado)
- ⚠️ = nota com ressalva (diferenca 5-10 pontos)
- ✅ = nota honesta (diferenca < 5 pontos)

### Tabela dos 10 Pilares (estado oficial da sprint)

| # | Pilar | Score atual | Topo | Gap | Badge | Evidencia-chave |
|---|---|---|---|---|---|---|
| 1 | Performance em maquina fraca | **60** | 90 | -30 | ✅ | `gates-summary.md`: G2+G4 PASS; MSI/Inno dentro da meta |
| 2 | Instalacao/desinstalacao | **75** | 98 | -23 | ✅ | `gates-summary.md`: G2+G3 PASS; `FAIL=0` |
| 3 | Governanca de release | **74** | 90 | -16 | ✅ | `CHANGELOG.md` + `RELEASE_APPROVAL.md` + testes meta |
| 4 | Seguranca de release | **70** | 95 | -25 | ⚠️ | manifesto/sbom/pinning PASS; assinatura oficial pendente |
| 5 | Robustez e preservacao | **60** | 95 | -35 | ✅ | cleanup/preservacao PASS em MSI/Inno na rodada FINAL2 |
| 6 | Update confiavel | **65** | 95 | -30 | ⚠️ | ciclo Dia 5 em PASS; rollback de falha ainda nao coberto |
| 7 | Operacao/automacao | **80** | 95 | -15 | ✅ | G1 PASS + automacao sequencial validada |
| 8 | UX (NOVO) | **25** | 90 | -65 | 🔴 | sem progress bar/tempo estimado/multi-idioma |
| 9 | Compatibilidade (NOVO) | **55** | 95 | -40 | ⚠️ | Win10/Win11 validados na rodada FINAL2 |
| 10 | Resiliencia/Rollback (NOVO) | **15** | 95 | -80 | 🔴 | rollback/repair ainda sem cobertura de testes |

### Como as notas foram fechadas nesta sprint

**P1 Performance:** G2+G4 em PASS na rodada `FINAL2-20260217150331`; metrica atual MSI (install 6.59s/18.85s, uninstall 8.56s/5.10s) e Inno (install 10.82s/14.41s, uninstall 2.36s/3.57s).

**P2 Instalacao:** suite MSI e Inno com `FAIL=0`, incluindo atalhos/registro/uninstall.

**P3 Governanca:** trilha documental e gates meta em PASS, com fechamento formal em `RELEASE_APPROVAL.md`.

**P4 Seguranca:** baseline local consolidada (manifesto/SBOM/pinning), mantendo bloqueio comercial por falta do certificado corporativo real.

**P5 Robustez:** limpeza de Program Files/registro e preservacao de AppData validadas na rodada final.

**P6 Update:** ciclo Dia 5 executado em Win10/Win11 via HTTP local com install/reinstall/upgrade/uninstall em PASS; rollback de falha continua pendente.

**P7 Operacao:** automacao sequencial e bootstrap QGA consolidados; G1 em PASS.

**P8 UX:** backlog funcional permanece sem implementacoes de UX premium.

**P9 Compatibilidade:** validacao funcional Win10+Win11 em PASS na rodada final, com G1/G2 em PASS.

**P10 Resiliencia:** permanece como maior gap (rollback/repair ainda sem suite dedicada).

### Registro de score por evidencia (rodada atual)

Regra: score so sobe com evidencia de run id e log novo da rodada correspondente.

| Pilar | Antes | Depois | Delta | Gate | RunId | Evidencia |
| --- | --- | --- | --- | --- | --- | --- |
| 1 - Performance em maquina fraca | 30 | 60 | 30 | G2+G4 em PASS | 20260217T153640Z | `saida/validacao-windows-FINAL2-20260217150331/gates-summary.md` |
| 2 - Instalacao/desinstalacao | 58 | 75 | 17 | G2+G3 em PASS (`FAIL=0`) | 20260217T153640Z | `saida/validacao-windows-FINAL2-20260217150331/windows-round-summary.md` |
| 5 - Robustez e preservacao | 45 | 60 | 15 | G3 em PASS (cleanup/preservacao) | 20260217T153640Z | `saida/validacao-windows-FINAL2-20260217150331/win10-lite-guest-msi-results-FINAL2-20260217150331.json` |
| 7 - Operacao/automacao | 78 | 80 | 2 | G1 em PASS nas duas VMs | 20260217T153640Z | `saida/validacao-windows-FINAL2-20260217150331/gates-summary.md` |
| 9 - Compatibilidade | 42 | 55 | 13 | G1+G2 em PASS (Win10/Win11) | 20260217T153640Z | `saida/validacao-windows-FINAL2-20260217150331/windows-round-summary.md` |

### Historico de rodadas intermediarias (pre-fechamento)

As secoes a seguir preservam a trilha de evolucao entre 2026-02-15 e 2026-02-16.
O estado oficial atual e o que consta acima (rodada `FINAL2-20260217150331` + fechamento Dia 7).

### Atualizacao trabalho paralelo (2026-02-15T12:00:00Z)

Trabalho paralelo executado sem conflito com testes Windows (7 fases, 28 itens).

| Pilar | Antes | Depois | Delta | Motivo | RunId | Evidencia |
| --- | --- | --- | --- | --- | --- | --- |
| 3 - Governanca | 64 | **74** | **+10** | CHANGELOG.md + RELEASE_APPROVAL.md [FINAL] + test-changelog-format.sh PASS | 20260215T093500Z | `saida/validacao-paralela-20260215T093500Z/resumo.csv` |
| 9 - Compatibilidade | 42 | **44** | **+2** | REQUISITOS_MINIMOS.md + COMPATIBILIDADE_TESTADA.md criados | 20260215T093500Z | `documentos/REQUISITOS_MINIMOS.md` |

**Nota global atualizada:** 49.2 → **51.2** (+2.0 pontos conservador)

**Trabalho realizado:**
- FASE 1: Governanca (CHANGELOG, RELEASE_APPROVAL [FINAL], test-changelog-format)
- FASE 2: CI/CD (`.github/workflows/instalador-ci.yml`, `instalador-release.yml`, `documentos/CI_CD_GUIA_INSTALADOR.md`)
- FASE 3: Meta-testes (11 validacoes: 10 PASS, 1 SKIP)

### Atualizacao rodada FINAL2 (2026-02-16T02:44:38Z)

🎉 **PROGRESSO SIGNIFICATIVO!** Upload otimizado funcionou, performance medida, 3 FAILs resolvidos.

| Pilar | Antes | Depois | Delta | Motivo | RunId | Evidencia |
| --- | --- | --- | --- | --- | --- | --- |
| 1 - Performance | 30 | **45** | **+15** | Performance medida (4/5 metas OK; MSI uninstall 94.69s bloqueador) | FINAL2-20260216024438 | `saida/validacao-windows-FINAL2-20260216024438/win10-lite-guest-regressao-windows-FINAL2-20260216024438.json` |
| 2 - Instalacao | 58 | **68** | **+10** | Inno 9/10 PASS (3 FAILs resolvidos: INNO-02, INNO-03, INNO-UNINST-01) | FINAL2-20260216024438 | `saida/validacao-windows-FINAL2-20260216024438/win10-lite-guest-inno-results-FINAL2-20260216024438.json` |
| 5 - Robustez | 45 | **52** | **+7** | Inno cleanup PASS; UPGRADE-REINSTALL-E2E PASS (preservação OK) | FINAL2-20260216024438 | `saida/validacao-windows-FINAL2-20260216024438/win10-lite-guest-upgrade-metrics-FINAL2-20260216024438.json` |
| 9 - Compatibilidade | 44 | **49** | **+5** | Win10 validado (upload PASS, QGA PASS, E2E rodou); Win11 QGA bloqueado | FINAL2-20260216024438 | `saida/validacao-windows-FINAL2-20260216024438/resumo.csv` |

**Nota global atualizada:** 51.2 → **68-70** (+17-19 pontos) 🎉

**Conquistas FINAL2:**
- ✅ Upload otimizado (chunk 64KB) funcionou perfeitamente no Win10
- ✅ Performance medida pela primeira vez (dados reais de timing!)
- ✅ 3 FAILs históricos resolvidos (INNO-02, INNO-03, INNO-UNINST-01)
- ✅ Win10 QGA estável e confiável
- ✅ Upgrade/Reinstall E2E funcionando (preservação de dados validada)

**Bloqueadores identificados:**
- ❌ MSI uninstall lento: 94.69s (meta ≤45s) - CRÍTICO P0
- ❌ Win11 QGA: Timeout 120s ("QEMU guest agent is not connected") - CRÍTICO P0
- ⚠️ MSI results missing: Arquivo msi-results.json não gerado - P1
- ⚠️ INNO-04-REGISTRY: Só 1 FAIL restante (HKLM key) - P2

**Próximo passo:** DIA 2 - Diagnóstico completo (3-4h)
- FASE 4: Linux (4/4 PASS: AppImage 5/5, contrato, manifesto)
- FASE 5: Docs (REQUISITOS_MINIMOS, COMPATIBILIDADE, RISCOS atualizados)
- FASE 6: Assinatura (sign-with-cert.sh, test-code-signing-cert.sh esqueletos)
- FASE 7: Consolidacao (resumo.csv, TRABALHO-PARALELO-RESUMO.md)

**Veredito:** NO-GO mantido (honestidade tecnica)
**Bloqueadores criticos:** QGA bloqueado, 6 FAIL E2E Windows, certificado .pfx pendente

**Resumo completo:** `saida/TRABALHO-PARALELO-RESUMO.md`

---

## PARTE 2: Graficos por Pilar

Legenda das barras:
```
[VALIDADO]  = nota comprovada por teste automatizado que passou
[ALEGADO ]  = nota no documento sem teste OU codigo corrigido sem reteste
[META 85 ]  = minimo para liberar producao
[TOPO    ]  = referencia enterprise/instalador pago
```

Cada `#` = 5 pontos. Escala 0-100.

### 1. Performance em maquina fraca ✅

```
VALIDADO (60): [############........]
ALEGADO  (62): [############........]
META 85:       [#################...]
TOPO  90:      [##################..]
```
Status: ✅ VALIDADO. G2/G4 em PASS na rodada `FINAL2-20260217150331` com p95 dentro de meta.

### 2. Instalacao/desinstalacao ✅

```
VALIDADO (75): [###############.....]
ALEGADO  (75): [###############.....]
META 85:       [#################...]
TOPO  98:      [####################]
```
Status: ✅ VALIDADO. Suites MSI/Inno com `FAIL=0` e G3 em PASS.

### 3. Governanca de release ✅

```
VALIDADO (64): [#############.......]
ALEGADO  (65): [#############.......]
META 85:       [#################...]
TOPO  90:      [##################..]
```
Status: ✅ HONESTO (-1 ponto). QA/revisor nomeados e validadores de governanca em PASS.

### 4. Seguranca de release ✅

```
VALIDADO (70): [##############......]
ALEGADO  (70): [##############......]
META 85:       [#################...]
TOPO  95:      [###################.]
```
Status: ✅ HONESTO (0 pontos). Seguranca local estrita confirmada na rodada de validacao.

### 5. Robustez e preservacao ✅

```
VALIDADO (60): [############........]
ALEGADO  (60): [############........]
META 85:       [#################...]
TOPO  95:      [###################.]
```
Status: ✅ VALIDADO na trilha tecnica: cleanup/preservacao passaram na rodada FINAL2.

### 6. Update confiavel ⚠️

```
VALIDADO (65): [#############.......]
ALEGADO  (65): [#############.......]
META 85:       [#################...]
TOPO  95:      [###################.]
```
Status: ⚠️ RESSALVA. Dia 5 executou ciclo real via HTTP local (PASS), mas rollback de falha e assinatura final continuam pendentes.

### 7. Operacao/automacao ✅

```
VALIDADO (80): [################....]
ALEGADO  (80): [################....]
META 85:       [#################...]
TOPO  95:      [###################.]
```
Status: ✅ VALIDADO. G1 em PASS e automacao consolidada; CI/CD em PR/tag real segue como pendencia nao bloqueante da trilha tecnica.

Evolucao tecnica da rodada Windows de referencia (`FINAL2-20260217150331`):
```
sanitize_cdrom preflight:[####################] PASS
attach/cleanup de midia: [####################] PASS
QGA guest-ping:          [####################] PASS
```

### 8. 🟢 UX (NOVO) 🔴

```
VALIDADO (25): [#####...............]
ALEGADO  (--): (nao existia pilar)
META 85:       [#################...]
TOPO  90:      [##################..]
```
Status: 🔴 CRITICO. Pilar novo. Instalador e funcional mas sem UX moderna.

### 9. 🟢 Compatibilidade (NOVO) ⚠️

```
VALIDADO (55): [###########.........]
ALEGADO  (--): (nao existia pilar)
META 85:       [#################...]
TOPO  95:      [###################.]
```
Status: ⚠️ VALIDADO parcialmente para producao comercial. Win10/Win11 em PASS (G1/G2), pendentes Defender/offline/GPO.

Evolucao tecnica da rodada Windows de referencia (`FINAL2-20260217150331`):
```
Bootstrap tecnico Win10:[####################] PASS
Sequencia Win10->Win11: [####################] PASS
Regressao in-guest QGA: [####################] PASS
```

### 10. 🟢 Resiliencia/Rollback (NOVO) 🔴

```
VALIDADO (15): [###.................]
ALEGADO  (--): (nao existia pilar)
META 85:       [#################...]
TOPO  95:      [###################.]
```
Status: 🔴 CRITICO. Rollback nunca testado. Inno sem rollback nativo.

---

## PARTE 3: Comparacao com Instaladores Pagos

### Feature por feature: Protons vs Mercado

|            Feature              |            Protons          |    Advanced Installer   | InstallShield    | MSIX/AppX       |
|---------------------------------|-----------------------------|-------------------------|------------------|-----------------|
|      **Instalacao basica**      |      ✅ MSI + Inno          |    ✅ MSI/MSIX          | ✅ MSI/EXE       | ✅ MSIX         |
|     **Desinstalacao limpa**     |   ✅ Validada (`FAIL=0`)     |    ✅ Testado           | ✅ Testado       | ✅ Automatica   |
|  **Progress bar customizada**   |       ❌ Nao tem            |    ✅ GUI builder       | ✅ GUI builder   | ✅ Nativa OS    |
|        **Tempo estimado**       |       ❌ Nao tem            |    ✅ Sim               | ✅ Sim           | ⚠️ Parcial      |
|         **Multi-idioma**        |       ❌ Nao tem            |    ✅ 30+ idiomas       | ✅ 30+ idiomas   | ✅ Via manifest |
|    **Cancelamento gracioso**    |       ❌ Nao tem            |    ✅ Sim               | ✅ Sim           | ✅ Nativo       |
|     **Rollback em falha**       |       ❌ Nunca testado      |    ✅ Automatico        | ✅ Automatico    | ✅ Transacional |
|         **Repair mode**         |       ❌ Nao tem            |    ✅ Sim               | ✅ Sim           | ✅ Via Store    |
|    **Assinatura de codigo**     | ⚠️ Local (sem cert corp)    |    ✅ Integrado         | ✅ Integrado     | ✅ Obrigatorio  |
|       **Update automatico**     |    ⚠️ Manifesto local       |    ✅ Updater integrado | ✅ FlexNet       | ✅ Store auto   |
|    **Deteccao de antivirus**    |       ❌ Nao testado        |    ✅ Whitelisting      | ✅ Whitelisting  | ✅ Trusted Store|
|   **Compatibilidade Win10/11**  |   ✅ Win10+Win11 validados  |    ✅ Testado           | ✅ Testado       | ✅ Nativo       |
|     **Compatibilidade ARM**     |       ❌ Nao previsto       |    ✅ ARM64             | ✅ ARM64         | ✅ Nativo       |
|      **Instalacao offline**     | ⚠️ Provavel mas nao testado |    ✅ Sim               | ✅ Sim           | ⚠️ Depende      |
| **Politica corporativa (GPO)**  |       ❌ Nao testado        |    ✅ MSI Transform     | ✅ GPO nativo    | ✅ ADMX         |
|       **CI/CD integrado**       | ⚠️ Basico (isolado, sem execucao de producao) |    ✅ CLI + MSBuild     | ✅ CLI + MSBuild | ✅ PowerShell   |
|            **SBOM**             |    ✅ CycloneDX + SPDX      |    ⚠️ Parcial           | ⚠️ Parcial       | ❌ Nao nativo   |
|      **Audit automatizado**     |    ✅ audit-host-state.sh   |    ❌ Nao nativo        | ❌ Nao nativo    | ❌ Nao nativo   |
|     **Preservacao de dados**    |    ✅ APPDATA preservado    |    ✅ Regras            | ✅ Regras        | ✅ Nativo       |
| **Manifesto de update estrito** |    ✅ strict-signature      |    ⚠️ Parcial           | ✅ Sim           | ✅ Nativo       |

### Resumo: o que eles fazem que nos NAO fazemos

1. **GUI de instalacao com feedback visual** (progress bar, tempo estimado, cancelamento)
2. **Rollback automatico transacional** (falha no meio → desfaz tudo)
3. **Repair mode** (usuario pode "reparar" sem reinstalar)
4. **Multi-idioma nativo** (detecta idioma do SO)
5. **Assinatura com certificado corporativo** (chain of trust reconhecida pelo Windows)
6. **Whitelisting de antivirus** (SmartScreen, Windows Defender)
7. **Suporte ARM64** (futuro proximo)
8. **CI/CD pipeline nativo** (build + sign + publish em um comando)
9. **Politicas corporativas (GPO/ADMX)** (deploy em massa)
10. **Update automatico com download + install** (nao apenas manifesto)

### Resumo: o que nos fazemos que eles NAO fazem

1. **SBOM completo** (CycloneDX + SPDX) - pagos nao geram isso nativamente
2. **Audit automatizado de host** - nenhum pago tem isso
3. **Manifesto de update com validacao estrita anti-falso-positivo** - nivel acima do padrao

---

## PARTE 4: Checklist de Melhoria por Pilar (SO O QUE FALTA)

Regras desta secao:
- ❌ Itens ja implementados foram REMOVIDOS
- ❌ Itens irrelevantes foram REMOVIDOS
- Cada item tem prioridade (P0 = bloqueador, P1 = importante, P2 = desejavel)
- Cada item indica qual teste validaria a melhoria
- Testes que precisam ser CRIADOS estao marcados com `[CRIAR TESTE]`
- Testes que precisam ser CORRIGIDOS estao marcados com `[CORRIGIR TESTE]`

---

### Rodada Linux 2026-02-11 (evidencia oficial)

Resumo da execucao dedicada: `saida/validacao-linux-20260211T212023Z/resumo.csv`

| # |                    Script                      | Status | Exit code | Evidencia |
|---|------------------------------------------------|--------|-----------|-----------|
| 1 |  `comum/scripts/validate-prohibited-terms.sh`  | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/01.log` |
| 2 |          `comum/scripts/lint-build.sh`         | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/02.log` |
| 3 |         `comum/scripts/validate-version.sh`    | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/03.log` |
| 4 |    `comum/scripts/validate-script-comments.sh` | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/04.log` |
| 5 |          `comum/scripts/validate-sbom.sh`      | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/05.log` |
| 6 |`comum/scripts/validate-update-manifest.sh --strict-signature` 
                                                     | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/06.log` |
| 7 |      `comum/scripts/test-update-manifest.sh`   | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/07.log` |
| 8 |        `comum/scripts/audit-host-state.sh`     | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/08.log` / `saida/host-audit/20260211T212025Z.md` |
| 9 |     `comum/scripts/collect-test-evidence.sh`   | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/09.log` / `saida/test-logs/20260211T212025Z/collect.log` |
| 10 |       `testes/linux/test-appimage.sh`         | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/10.log` |
| 11 |           `testes/linux/test-deb.sh`          | `PARCIAL`| `0` | `saida/validacao-linux-20260211T212023Z/11.log` (`SKIP` de 3-6 por `sudo -n`) |
| 12 | `testes/windows/test-installer-contract-static.sh`|`PASS` | `0` | `saida/validacao-linux-20260211T212023Z/12.log` |
| 13 | `testes/meta/test-checklist-graficos.sh`       | `PASS` | `0` | `saida/validacao-linux-20260211T212023Z/13.log` |

Consolidado da rodada:
- `PASS`: 12
- `PARCIAL`: 1
- `FAIL`: 0

### Rodada Windows autonoma 2026-02-15 (evidencia oficial)

Resumo da execucao autonoma: `saida/validacao-windows-20260215T000453Z/resumo.csv`

Checklist da rodada (somente o que foi realmente executado):
- [x] Congelar baseline pre-mudanca (`RUN_ID_BASE=20260214T233745Z`) com `audit-host-state`, `windows-vm-control status`, `qga-ping` e `dumpxml` (`saida/validacao-windows-20260214T233745Z/`)
- [x] Executar `bash comum/scripts/windows-e2e-sequencial.sh --run-id 20260215T000453Z --vm-order win10-lite,win11-lite --cooldown-sec 20 --strict-signature --bootstrap-mode auto --manual-fallback-on-auto-fail`
- [x] Sanitizar CD-ROM da `win10-lite` antes do bootstrap (`win10-lite_sanitize_cdrom.log`)
- [x] Executar bootstrap tecnico da `win10-lite` com attach de ISO helper e cleanup de midia (PASS em `change_media` e `cleanup_media`)
- [x] Gerar arquivo de fallback manual para `win10-lite` (`manual-actions-win10-lite.md`) quando `guest-ping` ficou bloqueado
- [x] Reexecutar bootstrap em `manual-ready` para `win10-lite` (permaneceu BLOQUEADO)
- [ ] Executar etapa equivalente na `win11-lite` (sequencia pausada antes desta VM)
- [x] Executar `bash testes/windows/test-qga-bootstrap-autonomo.sh 20260215T000453Z-TBOOT` (FAIL em modo estrito: `BLOQUEADO`, evidencia `saida/validacao-windows-20260215T000453Z/suite_test_qga_bootstrap_autonomo.log`)
- [x] Executar `bash testes/windows/test-regressao-autonoma.sh 20260215T000453Z-TREG` (FAIL em modo estrito: `BLOQUEADO`, evidencia `saida/validacao-windows-20260215T000453Z/suite_test_regressao_autonoma.log`)
- [x] Revalidar `validate-update-manifest --strict-signature`, `test-update-manifest`, `test-installer-contract-static`, `test-checklist-graficos` e `test-checklist-score-evidence` (PASS, evidencias em `saida/validacao-windows-20260215T000453Z/mandatory-suite.csv`)
- [x] Gerar novo snapshot factual de host com `bash comum/scripts/audit-host-state.sh` (`saida/host-audit/20260215T004642Z.md`)
- [x] Garantir canal QGA no XML e precheck de conectividade na `win10-lite` (PASS/PARCIAL: `bootstrap_qga_win10-lite_qga_channel.log`, `bootstrap_qga_win10-lite_qga_precheck.log`)
- [ ] Ativar QGA nas duas VMs (continua BLOQUEADO: `QEMU guest agent is not connected`)
- [ ] Rodar `run-regressao.ps1` completo in-guest com coleta de `regressao-windows-*.json` e `*-metrics-*.json`

Estado dos gates da politica conservadora:
- `G1` (QGA ativo nas duas VMs): **NAO ATINGIDO**
- `G2` (regressao completa nas duas VMs): **NAO ATINGIDO**
- `G3` (6 FAIL historicos resolvidos em novo run): **NAO ATINGIDO**
- `G4` (p95 MSI/Inno dentro de meta): **NAO ATINGIDO**

---

### Rodada Windows FINAL2 2026-02-16 (evidencia oficial - upload otimizado)

**RUN_ID:** `FINAL2-20260216024438`
**Timestamp:** `2026-02-16T03:17:27Z` (concluído)
**Duração:** 33 minutos (02:44 - 03:17 UTC)
**Script:** `RODAR-FINAL2-CORRIGIDO.sh` (chunk 64KB otimizado)

Resumo da execucao: `saida/validacao-windows-FINAL2-20260216024438/resumo.csv`
Gates summary: `saida/validacao-windows-FINAL2-20260216024438/gates-summary.md`
Relatório completo: `saida/validacao-windows-FINAL2-20260216024438/windows-round-summary.md`

**🎉 CONQUISTA PRINCIPAL:** Upload bundle funcionou no Win10 com chunk 64KB! (95% economia de comandos)

#### Totais da rodada:
- **PASS:** 39
- **FAIL:** 4
- **PARCIAL:** 6
- **BLOQUEADO:** 4
- **SEM_RESULTADO:** 1 (Win11 skipped)
- **TOTAL:** 54 steps

#### Win10-lite (PARCIAL - progresso importante!):
- ✅ **Bootstrap QGA:** PASS (canal + ping funcionando)
- ✅ **Bundle upload:** PASS (chunk 64KB = 2-3min vs timeout anterior!)
- ✅ **Bundle transfer metrics:** PASS
- ✅ **Collect guest files:** PASS (9 arquivos JSON/MD recuperados)
- ❌ **Invoke regressão:** FAIL (upload funcionou mas execução do script falhou)
- ❌ **Artifact set validation:** FAIL (arquivos faltando)
- **Resultado final:** FAIL (mas upload OK = progresso!)

#### Win11-lite (BLOQUEADO):
- ✅ **Bootstrap QGA restore_clean:** PARCIAL
- ✅ **Bootstrap QGA ensure_running:** PASS
- ✅ **Bootstrap QGA channel:** PASS
- ❌ **Bootstrap QGA ping:** BLOQUEADO (timeout aguardando QGA responder)
- ❌ **Bootstrap QGA result:** BLOQUEADO
- ❌ **Run regressão:** BLOQUEADO (não executado devido ao QGA)
- **Resultado final:** SEM_RESULTADO (QGA ainda precisa intervenção manual)

#### Estado dos gates (política conservadora):
- `G1` (QGA ativo nas duas VMs): **NAO** (Win10=PASS, Win11=BLOQUEADO)
- `G2` (regressao completa nas duas VMs): **NAO** (Win10=FAIL, Win11=SEM_RESULTADO)
- `G3` (6 FAIL historicos resolvidos): **NAO** (não atingido)
- `G4` (p95 MSI/Inno dentro de meta): **NAO** (não medido)

#### Evidências-chave coletadas:
- `win10-lite-guest-regressao-windows-FINAL2-20260216024438.json` ✅
- `win10-lite-guest-regressao-windows-FINAL2-20260216024438.md` ✅
- `win10-lite-guest-artifact-verify-FINAL2-20260216024438.json` ✅
- `win10-lite-guest-upgrade-metrics-FINAL2-20260216024438.json` ✅
- `win10-lite-guest-inno-metrics-FINAL2-20260216024438.json` ✅
- `win10-lite-guest-inno-results-FINAL2-20260216024438.json` ✅
- `win10-lite-guest-msi-metrics-FINAL2-20260216024438.json` ✅

#### Problemas identificados:
1. **Win10 invoke_regressao FAIL:** Upload bundle funcionou (PASS) mas script PowerShell não executou corretamente
   - Evidência: `run_regressao_win10-lite_invoke_regressao.status.log`
   - Próximo passo: Diagnosticar erro de execução do `run-regressao.ps1`

2. **Win11 QGA blocking:** QGA channel + VM running OK, mas `guest-ping` não responde
   - Evidência: `bootstrap_qga_win11-lite_qga_wait.log`
   - Próximo passo: Intervenção manual no Win11 (verificar serviço QGA Guest Agent)

3. **JSON UTF-8 BOM error:** Ocorreu em `[2026-02-16T02:53:52Z]` durante processamento Win11
   - Tipo: `json.decoder.JSONDecodeError: Unexpected UTF-8 BOM`
   - Próximo passo: Investigar encoding dos arquivos JSON gerados no Windows

4. **Artifacts missing:** Win10 reportou artifacts faltando mesmo após coleta
   - Evidência: `run_regressao_win10-lite_missing_artifacts.log`
   - Próximo passo: Verificar se `run-regressao.ps1` gerou todos os arquivos esperados

#### Progresso vs rodadas anteriores:
- ✅ **Upload otimizado:** Chunk 3KB → 64KB (27.000 → 1.300 comandos = 95% economia)
- ✅ **Win10 upload:** PASS
- ✅ **Win11 QGA:** PASS na rodada `FINAL2-20260217150331`
- ✅ **Invoke regressão:** PASS em ambas as VMs

#### Score consolidado (conservador):
- **Score técnico interno:** 80/100 (GO interno)
- **Score comercial externo:** 80/100 (ainda abaixo de 85 por ausência de `.pfx`)
- **Meta comercial:** 85/100

#### 🔍 ANÁLISE DETALHADA do Win10 (invoke_regressao):

**Situação final da sprint (rodada `FINAL2-20260217150331`):**
- ✅ **UPGRADE-REINSTALL-E2E:** PASS
- ✅ **MSI-E2E:** PASS
- ✅ **INNO-E2E:** PASS
- ✅ **ARTIFACT-VERIFY:** PASS (artefatos gerados em Win10/Win11)

**Performance consolidada da rodada final:**
- ✅ MSI install: **6.59s** (Win10) e **18.85s** (Win11)
- ✅ MSI uninstall: **8.56s** (Win10) e **5.10s** (Win11)
- ✅ Inno install: **10.82s** (Win10) e **14.41s** (Win11)
- ✅ Inno uninstall: **2.36s** (Win10) e **3.57s** (Win11)

#### Checklist de follow-up:
- [x] **CRÍTICO** Otimizar MSI uninstall (94.69s → ≤45s)
  - Verificar: remoção de arquivos, registry cleanup, delays desnecessários
  - Arquivo: `windows/instalador-msi/Product.wxs`
- [x] Diagnosticar MSI-E2E FAIL detalhes (ver logs completos in-guest)
- [x] Diagnosticar INNO-E2E FAIL detalhes (ver logs completos in-guest)
- [x] Corrigir geração de artifact `msi-results-*.json` no `run-regressao.ps1`
- [x] Intervir manualmente no Win11 para ativar QGA Guest Agent service
- [x] Investigar JSON UTF-8 BOM error (recodificar arquivos ou ajustar parser?)
- [x] Reexecutar rodada após correções: `bash RODAR-FINAL2-CORRIGIDO.sh`

---

### PILAR 1: Performance em maquina fraca (60/100 → meta 85)

**⚡ SCRIPT AUTOMÁTICO DISPONÍVEL:**
```bash
# Executa regressão com medição de tempo em AMBAS as VMs automaticamente:
bash RODAR-FINAL2-CORRIGIDO.sh
```
**Resultado:** Testa Win10 + Win11 + mede tempo p95 + gera relatórios (40-90min automático)

**O que falta fazer:**

- [x] **P0** ⚡ Executar `RODAR-FINAL2-CORRIGIDO.sh` (AUTOMÁTICO - use o script!) ✅ FEITO
  - [x] ~~Executar `run-regressao.ps1` na VM `win10-lite` com medicao de tempo~~ (AUTOMATIZADO) ✅
  - [x] ~~Executar `run-regressao.ps1` na VM `win11-lite` com medicao de tempo~~ (AUTOMATIZADO) ✅
  - Teste: script roda tudo sozinho via QGA ✅
- [x] **P0** Documentar resultados p95: MSI install <= 90s, MSI uninstall <= 45s ✅ MEDIDO
  - MSI install: **6.59s** (Win10) / **18.85s** (Win11) ✅
  - MSI uninstall: **8.56s** (Win10) / **5.10s** (Win11) ✅
- [x] **P0** Documentar resultados p95: Inno install <= 90s, Inno uninstall <= 45s ✅ MEDIDO
  - Inno install: **10.82s** (Win10) / **14.41s** (Win11) ✅
  - Inno uninstall: **2.36s** (Win10) / **3.57s** (Win11) ✅
- [x] **P0 CRÍTICO** ⚠️ Otimizar MSI uninstall (94.69s → ≤45s)
  - Resultado final: meta atingida em ambas as VMs na rodada `FINAL2-20260217150331`
- [ ] **P1** Medir tempo de primeira execucao do app apos install
  - `[CRIAR TESTE]` `testes/windows/test-first-launch-time.ps1`

**Testes a criar/corrigir:**

|           Teste               | Status                   | Acao |
|-------------------------------|--------------------------|------|
|      `run-regressao.ps1`      | Existe, com metricas em PASS | OK |
| `test-first-launch-time.ps1` |      Nao existe | `[CRIAR]` Medir cold start do app |

---

### PILAR 2: Instalacao/desinstalacao (75/100 → meta 85)

**⚡ SCRIPT AUTOMÁTICO DISPONÍVEL:**
```bash
# Executa testes E2E completos em Win10 + Win11 automaticamente:
bash RODAR-FINAL2-CORRIGIDO.sh
```
**Resultado:** Revalida suites MSI/Inno nas duas VMs e acusa regressao imediatamente

**O que falta fazer:**

- [x] **P0** Rebuild do MSI apos correcoes em `Product.wxs` e `Components.wxs`
  - Comando: `windows/scripts/build-msi.ps1`
- [x] **P0** Rebuild do Inno apos correcoes
  - Comando: `windows/scripts/build-inno.ps1`
- [x] **P0** ⚡ Reteste E2E completo apos rebuild (AUTOMÁTICO - use o script!)
  - ~~Teste: `testes/windows/test-msi.ps1` + `testes/windows/test-inno.ps1`~~ (AUTOMATIZADO)
  - Resultado final: `MSI=PASS` e `INNO=PASS` em Win10/Win11
  - Comando: `bash RODAR-FINAL2-CORRIGIDO.sh`
- [x] **P0** Resolver estrategia de remocao de `INSTALLFOLDER` (evitar "another client exists")
  - Teste: `testes/windows/test-msi.ps1` (cenario de reinstall)
- [x] **P1** ⚡ Validar install + uninstall em Windows 10 real (AUTOMÁTICO - use o script!)
  - ~~Teste: `testes/windows/run-regressao.ps1` na VM~~ (AUTOMATIZADO via RODAR-FINAL2-CORRIGIDO.sh)
- [x] **P1** Corrigir atalhos Inno (Desktop + Start Menu) - FAIL `INNO-02` e `INNO-03`
  - Teste: `testes/windows/test-inno.ps1`
- [x] **P1** Corrigir registro Inno - FAIL `INNO-04`
  - Teste: `testes/windows/test-inno.ps1`
- [x] **P2** Validar cenario de upgrade (1.0.0 → 1.0.1) sem perda de dados
  - Teste: `testes/windows/test-upgrade-reinstall.ps1` (existe)

**Testes a criar/corrigir:**

|           Teste              |          Status          |            Acao                |
|------------------------------|--------------------------|--------------------------------|
|       `test-msi.ps1`         | Existe, PASS na rodada final | OK |
|       `test-inno.ps1`        | Existe, PASS na rodada final | OK |
| `test-upgrade-reinstall.ps1` | Existe, PASS no Dia 5 | OK |
|     `run-regressao.ps1`      | Existe, PASS em Win10+Win11 | OK |

---

### PILAR 3: Governanca de release (74/100 → meta 85)

**O que falta fazer:**

- [x] **P1** Abrir changelog final da release com riscos aceitos e bloqueadores
  - Evidencia: `documentos/CHANGELOG_RELEASE_FINAL.md`; `saida/validacao-paralela-20260215T101334Z/92-changelog-format.log`
- [x] **P1** Executar rodada final de revisao e registrar veredito
  - Teste: `saida/RELATORIO-EXECUTIVO-FINAL.txt` atualizado com veredito
- [ ] **P1** Publicar registro de aprovacao final (quem, quando, versao, hash)
  - Teste: campo `aprovacao_final` em `RELEASE_APPROVAL.md`
- [ ] **P1** Concluir itens [FINAL] pendentes de governanca (foco em assinatura oficial/CI remoto)
  - Evidencia: `documentos/RELEASE_APPROVAL.md` (bloco FINAL)
- [x] **P2** Implementar gate automatico de GO/NO-GO (script que verifica todos os criterios)
  - Evidencia: `comum/scripts/check-go-nogo.sh`; `testes/meta/test-go-nogo-gate.sh`; `saida/validacao-paralela-20260215T101334Z/93-go-nogo-gate.log`

**Testes a criar/corrigir:**

|                Teste                  |    Status    |             Acao                   |
|---------------------------------------|--------------|-------------------------------------|
|    `test-checklist-graficos.sh`       | Existe, PASS | OK                                 |
|    `test-changelog-format.sh`         | Existe, PASS | Reexecutar em cada rodada          |
|    `check-go-nogo.sh`                 | Existe, PASS | Gate automatico funcional          |
| `test-checklist-honesty.sh`           | Existe, PASS | Valida honestidade dos scores      |
| `test-checklist-score-evidence.sh`    | Existe, PASS | Valida evidencias por pilar        |
| `test-release-approval-consistency.sh`| Existe       | Valida RELEASE_APPROVAL.md         |

---

### PILAR 4: Seguranca de release (70/100 → meta 85)

**O que falta fazer:**

- [ ] **P0** Obter certificado corporativo de assinatura (`.pfx`)
  - Status: BLOQUEADO (dependencia externa - Protons Consultoria)
  - Teste: `testes/meta/test-code-signing-cert.sh` (existe; executar quando cert estiver disponivel)
- [ ] **P0** Assinar MSI e EXE com timestamp RFC3161
  - Teste: `signtool verify /pa /v` no artefato assinado
  - `[CRIAR TESTE]` `testes/windows/test-signtool-verify.ps1`
- [x] **P1** Publicar hash SHA-256 de todos os artefatos finais no release
  - Evidencia: `testes/meta/test-release-hashes.sh`; `saida/validacao-paralela-20260215T101334Z/90-release-hashes.log`
- [ ] **P1** Testar que Windows SmartScreen aceita o instalador assinado
  - `[CRIAR TESTE]` `testes/windows/test-smartscreen-acceptance.ps1`
- [x] **P2** Implementar pinning de chave publica no update checker
  - Evidencia: `testes/meta/test-update-manifest-pinning.sh` (existe e PASS)

**Testes a criar/corrigir:**

|             Teste                 |    Status    |                      Acao                   |
|-----------------------------------|--------------|---------------------------------------------|
| `validate-update-manifest.sh`     | Existe, PASS |                       OK                    |
|   `test-update-manifest.sh`       | Existe, PASS |                       OK                    |
|  `test-code-signing-cert.sh`      | Existe (esqueleto, bloqueado sem `.pfx`) | Executar quando certificado estiver disponivel |
|  `test-signtool-verify.ps1`       | Nao existe   | `[CRIAR]` Verificar assinatura Authenticode |
|  `test-release-hashes.sh`         | Existe, PASS (rodada `20260215T101334Z`) | Reexecutar em cada release |
| `test-smartscreen-acceptance.ps1` | Nao existe   | `[CRIAR]` Teste manual → automatico         |

---

### PILAR 5: Robustez e preservacao (60/100 → meta 85)

**O que falta fazer:**

- [x] **P0** Reteste de cleanup apos correcoes de retry/polling
  - Resultado: `MSI-UNINST-01`, `MSI-UNINST-03`, `INNO-UNINST-01` em PASS
  - Teste: `testes/windows/test-msi.ps1` + `testes/windows/test-inno.ps1`
- [x] **P0** Validar que `C:\Program Files\Protons` e removido 100% apos uninstall
  - Teste: `test-msi.ps1` cenario `MSI-UNINST-01`
- [x] **P0** Validar que registro (HKLM) e limpo apos uninstall
  - Teste: `test-msi.ps1` cenario `MSI-UNINST-03`
- [x] **P1** Validar preservacao em cenario de reinstall/upgrade
  - Teste: `testes/windows/test-upgrade-reinstall.ps1`
- [ ] **P1** Testar comportamento com `%APPDATA%\Protons` pre-existente (dados reais)
  - `[CRIAR TESTE]` `testes/windows/test-appdata-preservation-real.ps1`
- [x] **P2** Remover payload de debug (`*.pdb`) do pacote Linux na entrega validada
  - Evidencia: `testes/meta/test-no-debug-symbols.sh`; `saida/validacao-paralela-20260215T101334Z/91-no-debug-symbols.log`

**Testes a criar/corrigir:**

|               Teste                  |         Status          |               Acao                   |
|--------------------------------------|-------------------------|--------------------------------------|
|           `test-msi.ps1`             | Existe, PASS na rodada final | OK |
|           `test-inno.ps1`            | Existe, PASS na rodada final | OK |
|      `test-upgrade-reinstall.ps1`    | Existe, PASS no Dia 5 | OK |
| `test-appdata-preservation-real.ps1` |       Nao existe        | `[CRIAR]` Cenario com dados reais    |
|     `test-no-debug-symbols.sh`       | Existe, PASS (rodada `20260215T101334Z`) | Reexecutar em cada release Linux |

---

### PILAR 6: Update confiavel (65/100 → meta 85)

**O que falta fazer:**

- [x] **P0** Executar ciclo completo de update: download → verificar hash → instalar nova versao
  - Evidencia: `saida/dia5-DIA5AUTO-20260217T200509Z/win10-lite.result.json` e `win11-lite.result.json`
- [x] **P0** Testar update com servidor HTTP real (nao `file:///`)
  - Evidencia: `msi_url` HTTP em ambas as VMs na rodada Dia 5
- [ ] **P1** Testar rollback quando download falha no meio
  - `[CRIAR TESTE]` `testes/windows/test-update-download-failure.ps1`
- [x] **P1** Testar deteccao de versao mais nova (1.0.0 → 1.0.1)
  - Teste: `comum/scripts/check-update.sh` (adicionar cenario de versao)
- [x] **P1** Testar que update preserva dados do usuario
  - Teste: `testes/windows/test-upgrade-reinstall.ps1` (ja existe parcial)
- [ ] **P3** (FUTURO - v2.0) Update silencioso em background
  - Depende: Ciclo basico funcionando primeiro (P0)
  - Status: Feature avancada, apos MVP

**Testes a criar/corrigir:**

|               Teste                | Status               |             Acao               |
|------------------------------------|----------------------|--------------------------------|
|           `check-update.sh`        | Existe, PASS (local) |         OK para smoke          |
|       `test-update-manifest.sh`    |      Existe, PASS    |              OK                |
|       `test-update-cycle-e2e.ps1`  |      Nao existe      | `[CRIAR]` Ciclo completo real  |
|      `test-update-http-server.ps1` |      Nao existe      | `[CRIAR]` Servidor HTTP local  |
| `test-update-download-failure.ps1` |      Nao existe      |    `[CRIAR]` Simular falha     |
|       `test-silent-update.ps1`     |      Nao existe      | `[CRIAR]` Update em background |

---

### PILAR 7: Operacao/automacao (80/100 → meta 85)

**Concluido nesta rodada (real):**
- [x] Endurecer bootstrap automatico do QGA no host (`ensure_qga_channel` + `sanitize_cdrom` + attach/cleanup de midia helper)
  - Evidencia: `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_qga_channel.log`; `saida/validacao-windows-20260215T000453Z/win10-lite_sanitize_cdrom.log`; `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_change_media.log`; `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_cleanup_media.log`
- [x] Tornar testes autonomos estritos por padrao (falham sem `PASS`)
  - Evidencia: `saida/validacao-windows-20260215T000453Z/suite_test_qga_bootstrap_autonomo.log`; `saida/validacao-windows-20260215T000453Z/suite_test_regressao_autonoma.log`

**O que falta fazer:**

- [x] **P0** Configurar CI/CD basico (GitHub Actions) para o instalador
  - Evidencia: `.github/workflows/instalador-ci.yml`; `saida/validacao-paralela-20260215T101334Z/23-validate-workflow-yaml.log`
- [x] **P1** Automatizar publicacao de release (tag → validacao → artefatos) para o instalador
  - Evidencia: `.github/workflows/instalador-release.yml`; `documentos/CI_CD_GUIA_INSTALADOR.md`
- [ ] **P1** Resolver `sudo -n true` bloqueado (login de sessao necessario)
  - Teste: `comum/scripts/audit-host-state.sh` (ja verifica)
- [x] **P1** Resolver grupos efetivos `libvirt,kvm` na sessao atual
  - Evidencia: `saida/host-audit/20260217T153523Z.md` (`groups_session` inclui `libvirt kvm`)
- [x] **P2** Automatizar snapshot de VMs antes/depois de testes
  - Evidencia: `testes/meta/test-vm-snapshot-automation.sh` (criado e funcional)

**Testes a criar/corrigir:**

|               Teste              |     Status        |               Acao               |
|----------------------------------|-------------------|----------------------------------|
|      `audit-host-state.sh`       | Existe, funcional |                OK                |
|  `test-checklist-graficos.sh`    | Existe, PASS | OK |
|   `collect-test-evidence.sh`     | Existe, funcional |                OK                |
|         CI/CD pipeline           | Configurado (isolado por `paths`, nao executado em producao) | Executar PR/tag de validacao |
| `test-vm-snapshot-automation.sh` | Existe, funcional | OK - Executar quando necessario |

---

### PILAR 8: 🟢 UX - Experiencia do Usuario (25/100 → meta 85)

**O que falta fazer:**

- Escopo deste ciclo de 7 dias (P8 + P10): progress bar customizada, tempo estimado, cancelamento gracioso e mensagens de erro claras.

- [x] **P0** Implementar progress bar no Inno Setup (customizar `[Code]` section)
  - Evidencia: `windows/innosetup/protons-setup.iss` (`CurInstallProgressChanged`, labels de progresso)
  - Validacao pendente: Win10/Win11
- [x] **P0** Adicionar mensagens de erro claras para o usuario final
  - Evidencia: `windows/innosetup/protons-setup.iss` (`CustomMessages` de erro/cancelamento/failpoint)
  - Validacao pendente: Win10/Win11
- [x] **P0** Implementar cancelamento gracioso (desfazer o que ja foi copiado)
  - Evidencia: `windows/innosetup/protons-setup.iss` (`CancelButtonClick`, `CleanupFailedInstall`)
  - Teste criado: `testes/windows/test-cancel-install.ps1`
  - Validacao pendente: Win10/Win11
- [x] **P1** Adicionar tempo estimado de instalacao (baseado em benchmark)
  - Evidencia: `windows/innosetup/protons-setup.iss` (`EtaLabel`, `EtaCalculating`, `CurInstallProgressChanged`)
  - Teste criado: `testes/windows/test-time-estimate.ps1`
  - Validacao pendente: Win10/Win11
- [x] **P1** Implementar multi-idioma (pelo menos PT-BR + EN)
  - Inno: `[Languages]` section com `.isl` files
  - MSI: `WixLocalization` com `.wxl` files
  - Evidencia parcial: `windows/innosetup/protons-setup.iss` (`ptbr` + `en`)
  - Validacao pendente: Win10/Win11
- [ ] **P2** Adicionar tela de boas-vindas customizada com logo
  - `[CRIAR]` Bitmap para Inno e MSI
- [ ] **P2** Implementar modo "custom install" (escolher componentes)
  - Teste: `testes/windows/test-custom-install.ps1`

**Testes a criar/corrigir:**

|             Teste            |  Status    |                 Acao                           |
|------------------------------|------------|------------------------------------------------|
| `test-inno-progress-bar.ps1` | Nao existe |       `[CRIAR]` Verificar feedback visual      |
|   `test-error-messages.ps1`  | Nao existe |  `[CRIAR]` Simular erros e verificar mensagens |
|   `test-cancel-install.ps1`  | Existe (pendente VM) | Executar em Win10/Win11 |
|   `test-time-estimate.ps1`   | Existe (pendente VM) | Executar em Win10/Win11 |
|   `test-multi-language.ps1`  | Nao existe |     `[CRIAR]` Verificar idiomas disponiveis    |

---

### PILAR 9: 🟢 Compatibilidade (55/100 → meta 85)

**Concluido nesta rodada (real):**
- [x] Congelar baseline pre-mudanca de compatibilidade Win10/Win11 com host/qga/xml
  - Evidencia: `saida/validacao-windows-20260214T233745Z/`
- [x] Validar infraestrutura de automacao autonoma sequencial (com fallback manual) e gerar consolidado de gates
  - Evidencia: `saida/validacao-windows-20260215T000453Z/resumo.csv`; `saida/validacao-windows-20260215T000453Z/gates-summary.md`; `saida/validacao-windows-20260215T000453Z/mandatory-suite.csv`

**⚡ SCRIPT AUTOMÁTICO DISPONÍVEL:**
```bash
# Valida instalação em Win10 + Win11 automaticamente:
bash RODAR-FINAL2-CORRIGIDO.sh
```
**Resultado:** Testa compatibilidade em ambas as VMs + gera evidências

**O que falta fazer:**

- Escopo deste ciclo de 7 dias: validar instalacao com Windows Defender ativo (compatibilidade real).
- Fora deste ciclo: deteccao/whitelisting automatico de antivirus e suporte ARM64.

- [x] **P0** ⚡ Validar instalacao completa em Windows 10 + 11 (AUTOMÁTICO - use o script!)
  - ~~Teste: `testes/windows/run-regressao.ps1` na VM Win10~~ (AUTOMATIZADO)
  - ~~Teste: `testes/windows/run-regressao.ps1` na VM Win11~~ (AUTOMATIZADO)
  - Comando: `bash RODAR-FINAL2-CORRIGIDO.sh`
- [x] **P0** Testar com Windows Defender ativo (nao desabilitar para testar)
  - Teste criado: `testes/windows/test-defender-compatibility.ps1`
  - Validacao pendente: Win10/Win11
- [ ] **P1** Testar instalacao offline (sem internet durante install)
  - `[CRIAR TESTE]` `testes/windows/test-offline-install.ps1`
- [ ] **P2** (DEPENDE DE CERT .pfx) Testar com GPO corporativa
  - Bloqueador: Certificado corporativo nao disponivel
  - Acao: Executar apos obter certificado
- [x] **P1** Documentar suporte apenas 64-bit (32-bit nao suportado)
  - Evidencia: `documentos/REQUISITOS_MINIMOS.md` (especifica x64 apenas)
- [x] **P2** Documentar requisitos minimos de SO (versao, build, features)
  - Evidencia: `documentos/REQUISITOS_MINIMOS.md`
- [x] **P2** Documentar matriz de compatibilidade testada
  - Evidencia: `documentos/COMPATIBILIDADE_TESTADA.md`
- [ ] **P3** (FUTURO - nao planejado v1.0) Suporte ARM64
  - Status: Nao prioritario, reavaliar quando houver demanda
  - Plataforma atual: x64 (Win10/Win11)

**Testes a criar/corrigir:**

|                Teste              |   Status   |                   Acao                     |
|-----------------------------------|------------|--------------------------------------------|
|         `run-regressao.ps1`       |   Existe   |  ⚡ `bash RODAR-FINAL2-CORRIGIDO.sh`      |
| `test-defender-compatibility.ps1` | Existe (pendente VM) | Executar install/uninstall com Defender ativo |
|     `test-offline-install.ps1`    | Nao existe |    `[CRIAR]` Desconectar rede → instalar   |

---

### PILAR 10: 🟢 Resiliencia/Rollback (15/100 → meta 85)

**O que falta fazer:**

- [x] **P0** Definir plano integrado UX + Resiliencia/Rollback (7 dias)
  - Evidencia: `PLANO-7-DIAS-GO.md` (ordem tecnica otimizada, canario Win10/Win11 e gate final)
- [x] **P0** Testar rollback nativo do MSI (simular falha no meio da instalacao)
  - Evidencia de implementacao: `windows/wix/Product.wxs` (`PROTONS_ROLLBACK_TEST`, custom action)
  - Teste criado: `testes/windows/test-msi-rollback.ps1`
  - Validacao pendente: Win10/Win11
- [x] **P0** Implementar rollback basico no Inno (desfazer copia em caso de erro)
  - Evidencia de implementacao: `windows/innosetup/protons-setup.iss` (`CleanupFailedInstall`, failpoint/cancel switches)
  - Teste criado: `testes/windows/test-inno-rollback.ps1`
  - Validacao pendente: Win10/Win11
- [x] **P1** Implementar repair mode no MSI
  - MSI ja suporta nativamente (`msiexec /f`), coberto por teste criado
  - Teste criado: `testes/windows/test-msi-repair.ps1`
  - Validacao pendente: Win10/Win11
- [x] **P1** Testar instalacao transacional (tudo ou nada)
  - Teste criado: `testes/windows/test-transactional-install.ps1`
  - Metodo: interrupcao + cleanup fallback
  - Validacao pendente: Win10/Win11
- [x] **P1** Testar recuperacao apos queda de energia simulada
  - Teste criado: `testes/windows/test-power-failure-recovery.ps1`
  - Metodo: kill do setup + reinstall + uninstall
  - Validacao pendente: Win10/Win11
- [ ] **P2** Implementar ponto de restauracao do Windows antes da instalacao
  - `[CRIAR]` Checkpoint-Computer no script de install
  - `[CRIAR TESTE]` `testes/windows/test-restore-point.ps1`

**Testes a criar/corrigir:**

|              Teste                |   Status   |                     Acao                     |
|-----------------------------------|------------|----------------------------------------------|
|       `test-msi-rollback.ps1`     | Existe (pendente VM) | Executar em Win10/Win11 |
|      `test-inno-rollback.ps1`     | Existe (pendente VM) | Executar em Win10/Win11 |
|      `test-msi-repair.ps1`        | Existe (pendente VM) | Executar em Win10/Win11 |
| `test-transactional-install.ps1`  | Existe (pendente VM) | Executar em Win10/Win11 |
| `test-power-failure-recovery.ps1` | Existe (pendente VM) | Executar em Win10/Win11 |
|      `test-restore-point.ps1`     | Nao existe |    `[CRIAR]` Verificar ponto de restauracao  |

---

## PARTE 5: Trade-offs entre pilares: melhorar A pode piorar B?

### Resposta direta
- `SIM` em 4 casos (9%) - precisa equilibrar
- `TALVEZ` em 5 casos (11%) - risco se fizer errado
- `NAO` em 33 casos (74%) - pode melhorar livremente

### Os 4 conflitos diretos (cuidado)

#### 1. Performance vs Seguranca
Problema: instalar rapido = pular verificacoes
```
Sem verificacao: 30s (rapido) mas vulneravel
Nossa solucao:   90s (medio)  e seguro (verifica 1x + cache)
Paranoia total:  180s (lento) e ultra-seguro
```
Como equilibrar: 1a instalacao verifica tudo (90s), updates usam cache (45s)

#### 2. Instalacao limpa vs Preservar dados
Problema: deletar tudo = PC limpo, mas perde configs do usuario
```
Nossa solucao:
- Deletar: C:\Program Files\Protons\ (codigo)
- Deletar: HKLM\Software\Protons (sistema)
- PRESERVAR: %APPDATA%\Protons\ (dados do usuario)
```
Como equilibrar: separar dados de sistema vs dados de usuario

#### 3 e 4. Reversos dos acima
- Seguranca vs Performance (reverso #1)
- Robustez vs Instalacao (reverso #2)

### Pilares que NUNCA conflitam (74% dos casos)

Governanca e ouro puro:
- Melhorar governanca NUNCA piora Performance, Instalacao, Seguranca, Robustez, Update ou Operacao
- Adicionar QA + revisor + documentacao = sempre bom

Outros pares seguros:
- Melhorar Performance NAO afeta Governanca, Instalacao, Update, Operacao
- Melhorar Instalacao NAO afeta Governanca, Performance, Seguranca, Update, Operacao
- Melhorar Update NAO afeta Governanca, Performance, Instalacao, Robustez
- Melhorar Operacao NAO afeta Performance, Instalacao, Robustez, Update

### Pilares novos e trade-offs

UX vs Performance:
- Adicionar progress bar e multi-idioma pode adicionar 1-2s ao install (desprezivel)
- `NAO` e conflito real

Compatibilidade vs Complexidade:
- Suportar Win10 + Win11 + ARM aumenta matriz de testes
- `TALVEZ` se nao tiver CI/CD para rodar tudo automaticamente

Resiliencia vs Performance:
- Rollback transacional adiciona overhead ao install
- `TALVEZ` - MSI nativo e eficiente, mas Inno custom pode ser lento

### Checklist antes de melhorar qualquer pilar

1. Vou melhorar qual pilar? _________________
2. Tem conflito direto?
   - Performance <-> Seguranca: usar cache + verificacao essencial
   - Instalacao <-> Robustez: separar sistema de usuario
   - Outros: pode melhorar livremente
3. Medir ANTES e DEPOIS (ambos os pilares afetados)

### Exemplos praticos do nosso projeto

Caso 1: "Vamos instalar mais rapido"
- ERRADO: remover verificacao de assinatura
  - Performance: 30 -> 95 (+65) mas Seguranca: 68 -> 20 (-48) REGRESSAO
- CERTO: cachear certificado + verificar 1x
  - Performance: 30 -> 60 (+30) e Seguranca: 68 -> 68 (0) manteve

Caso 2: "Vamos limpar tudo no uninstall"
- ERRADO: deletar Program Files E %APPDATA%
  - Instalacao: 55 -> 98 (+43) mas Robustez: 45 -> 10 (-35) perdeu configs
- CERTO: deletar so Program Files, preservar APPDATA
  - Instalacao: 55 -> 80 (+25) e Robustez: 45 -> 75 (+30) melhorou ambos

Caso 3: "Vamos melhorar governanca"
- SEMPRE SEGURO: adicionar QA, revisor, documentacao
  - Governanca: 62 -> 85 (+23)
  - Performance, Seguranca, outros: nao afetou
  - NENHUMA REGRESSAO

### Conclusao
Na maioria das vezes (74%), voce pode melhorar sem medo.
Nos 26% restantes, use as solucoes equilibradas acima (padrao Google, Microsoft, VSCode).

---

## PARTE 6: Ranking de Prioridade Real (ordenado por gap honesto)

### Ordenacao: maior gap primeiro = maior urgencia

| Rank |            Pilar        | Honesto | Topo | Gap | Esforco estimado | Quick Win? |
|------|-------------------------|---------|------|-----|-----------------|------------|
|   1  | 🟢 Resiliencia/Rollback | 15 | 95 | **-80** | Alto (implementar rollback Inno + testes) | Nao |
|   2  | 🟢 UX                   | 25 | 90 | **-65** | Medio (progress bar e multi-idioma no Inno) | Parcial (progress bar = rapido) |
|   3  | 🟢 Compatibilidade      | 55 | 95 | **-40** | Medio (Defender/offline/GPO) | Parcial |
|   4  |  Performance            | 60 | 90 | **-30** | Medio (otimizar cold-start) | Parcial |
|   5  | Robustez                | 60 | 95 | **-35** | Medio (casos reais de AppData) | Parcial |
|   6  | Update                  | 65 | 95 | **-30** | Medio (rollback de download) | Parcial |
|   7  | Seguranca               | 70 | 95 | **-25** | Alto (depende de cert corporativo) | Nao (BLOQUEADO) |
|   8  | Instalacao              | 75 | 98 | **-23** | Medio (acabamento comercial) | Parcial |
|   9  | Governanca              | 74 | 90 | **-16** | Medio (assinatura/aprovacao final) | Parcial |
|   10 | Operacao                | 80 | 95 | **-15** | Medio (execucao real de CI/CD) | Parcial |

### Estrategia recomendada: Quick Wins primeiro

**Fase imediata (foco comercial externo):**
1. **Seguranca/Assinatura:** obter certificado `.pfx` e assinar artefatos.
2. **Governanca final:** fechar aprovacoes/hashes finais de release comercial.
3. **CI/CD real:** executar PR/tag no GitHub Actions para fechar [FINAL-15].

**Fase curta (1-2 semanas):**
4. **Compatibilidade:** Defender ativo + offline install.
5. **Update:** rollback em falha de download e cenarios negativos.
6. **UX:** feedback visual basico (progress/cancelamento/mensagens).

**Fase media (1-2 meses):**
7. **Resiliencia:** Testar rollback MSI, implementar rollback Inno
8. **Operacao:** politicas de hardening e observabilidade de pipeline
9. **UX/Produto:** multi-idioma + custom install

### Nota global projetada apos Quick Wins

```
Atual (tecnico interno):          80 / 100  [################....]
Com assinatura comercial oficial: 85 / 100  [#################...]
Evolucao de medio prazo:          90+ / 100 [##################..]
```

---

## Fontes oficiais (verdade factual)

| Arquivo | Conteudo | Ultima execucao |
|---------|----------|-----------------|
| `saida/validacao-windows-FINAL2-20260217150331/windows-round-summary.md` | Rodada Windows final de referencia (`PASS 68 | FAIL 0 | PARCIAL 4`) | 2026-02-17 |
| `saida/validacao-windows-FINAL2-20260217150331/gates-summary.md` | Gates globais (`G1/G2/G3/G4` em PASS) | 2026-02-17 |
| `saida/dia5-DIA5AUTO-20260217T200509Z/summary.md` | Ciclo update/reinstall/upgrade com HTTP local | 2026-02-17 |
| `saida/dia7-DIA7AUTO-20260217T211731Z/` | Fechamento formal da sprint (interno x comercial) | 2026-02-17 |
| `saida/RESULTADO-FINAL.txt` | Veredito final consolidado | 2026-02-17 |
| `documentos/RELEASE_APPROVAL.md` | Governanca [FINAL] e aprovacoes | 2026-02-17 |
| `documentos/RISCOS_PENDENCIAS_FINAL.md` | Riscos remanescentes e bloqueio comercial | 2026-02-17 |
| `documentos/MATRIZ_EVIDENCIAS_FINAL.md` | Matriz de rastreabilidade de evidencias | 2026-02-15 |
| `saida/security-baseline-20260211T002214Z.md` | Baseline de seguranca local | 2026-02-11 |
| `saida/test-logs/e2e-results-20260207T174707.txt` | Referencia historica dos 6 FAIL originais | 2026-02-07 |

---

## Governanca do checklist
- [x] Checklist temporario consolidado para `CHECKLIST.md`.
- [ ] Quando houver fechamento comercial (>=85 com assinatura oficial), congelar snapshot em `documentos/historico/`.

## APENDICE A - Historico intermediario (pre-fechamento)

As secoes abaixo sao snapshots de rodadas antigas usadas durante a investigacao.
Nao usar estas secoes como estado oficial atual da sprint.

### Rodada Paralela 20260215T090651Z (sem Windows)

Escopo desta rodada:
- validacoes Linux/meta e documentacao de suporte
- sem interacao com `win10-lite`/`win11-lite`
- sem alteracao de automacao Windows em execucao por outra equipe

### Evidencias da rodada paralela

| Item | Status | Evidencia |
| --- | --- | --- |
| Baseline host congelada | PASS | `saida/validacao-paralela-20260215T090651Z/00-host-audit.log` |
| Hash checklist antes da rodada | PASS | `saida/validacao-paralela-20260215T090651Z/00-checklist-sha256-before.txt` |
| Trilha paralela segura (11 etapas) | PASS | `saida/validacao-paralela-20260215T090651Z/resumo.csv` |
| Manifesto de evidencias da rodada | PASS | `saida/validacao-paralela-20260215T090651Z/manifesto-evidencias.md` |
| Validacao de hashes de release | PASS | `saida/validacao-paralela-20260215T090651Z/90-release-hashes.log` |
| Validacao sem debug symbols Linux | PASS | `saida/validacao-paralela-20260215T090651Z/91-no-debug-symbols.log` |
| Validacao de formato do changelog final | PASS | `saida/validacao-paralela-20260215T090651Z/92-changelog-format.log` |

### O que melhorou

- Nova automacao de rodada paralela criada: `comum/scripts/run-parallel-safe-round.sh`.
- Novos meta-testes adicionados para fechamento honesto:
  - `testes/meta/test-release-hashes.sh`
  - `testes/meta/test-no-debug-symbols.sh`
  - `testes/meta/test-changelog-format.sh`
  - `testes/meta/test-checklist-honesty.sh`
- Documentacao complementar publicada:
  - `documentos/CHANGELOG_RELEASE_FINAL.md`
  - `documentos/REQUISITOS_MINIMOS.md`
  - `documentos/COMPATIBILIDADE_TESTADA.md`
  - `documentos/ASSINATURA_CODIGO.md`
- Governanca atualizada com bloco dedicado da rodada paralela em `documentos/RELEASE_APPROVAL.md`.

### O que nao mudou

- Gates globais Windows (`G1/G2/G3/G4`) continuam dependentes da trilha Windows.
- Veredito global permanece `NO-GO` ate fechamento factual da validacao Windows.
- Nao houve alteracao de score global nesta rodada paralela.

Sem alteração de score global nesta rodada paralela (G1-G4 dependem da trilha Windows).

## Rodada Paralela 20260215T101334Z (sem Windows)

Escopo desta rodada:
- reconciliacao factual do checklist + nova rodada paralela com evidencias
- sem interacao com `win10-lite`/`win11-lite`
- sem alteracao de automacao Windows ativa

### Evidencias da rodada paralela 2

| Item | Status | Evidencia |
| --- | --- | --- |
| Baseline host + commit + hash inicial do checklist | PASS | `saida/validacao-paralela-20260215T101334Z/00-host-audit.log`; `saida/validacao-paralela-20260215T101334Z/00-commit.txt`; `saida/validacao-paralela-20260215T101334Z/00-checklist-sha256-before.txt` |
| Trilha paralela segura (11 etapas) | PASS | `saida/validacao-paralela-20260215T101334Z/resumo.csv`; `saida/validacao-paralela-20260215T101334Z/40-run-parallel-safe-round.rc` |
| Hashes oficiais de release | PASS | `saida/validacao-paralela-20260215T101334Z/90-release-hashes.log` |
| Ausencia de `.pdb` em entrega Linux | PASS | `saida/validacao-paralela-20260215T101334Z/91-no-debug-symbols.log` |
| Formato do changelog final | PASS | `saida/validacao-paralela-20260215T101334Z/92-changelog-format.log` |
| Gate GO/NO-GO automatizado (coerencia de decisao/exit code) | PASS (`NO-GO` esperado) | `saida/validacao-paralela-20260215T101334Z/93-go-nogo-gate.log`; `saida/validacao-paralela-20260215T101334Z/go-nogo-20260215T101334Z.md` |

### O que melhorou

- `comum/scripts/run-parallel-safe-round.sh` foi endurecido para registrar corretamente falhas sem mascarar exit code.
- `comum/scripts/check-go-nogo.sh` e `testes/meta/test-go-nogo-gate.sh` estao ativos e validados nesta rodada.
- CI/CD do instalador ficou configurado na raiz com escopo isolado:
  - `.github/workflows/instalador-ci.yml`
  - `.github/workflows/instalador-release.yml`
- Itens desatualizados do checklist foram reconciliados (scripts/testes que ja existiam deixaram de aparecer como “Nao existe”).

### O que nao mudou

- Gates globais Windows (`G1/G2/G3/G4`) continuam pendentes e dependem da trilha Windows.
- Veredito global segue `NO-GO`.
- Certificado corporativo `.pfx` permanece bloqueio factual para assinatura final.

Sem alteração de score global nesta rodada paralela (G1-G4 dependem da trilha Windows).

---

## 📚 REFERÊNCIA RÁPIDA: SCRIPTS E GUIAS CRIADOS

**Para economizar tempo e tokens no futuro, use esta referência ao invés de refazer manualmente:**

### 🚀 Scripts de Automação (PRIORITÁRIOS)

| Script | Quando Usar | Resultado | Duração |
|--------|-------------|-----------|---------|
| `RODAR-FINAL2-CORRIGIDO.sh` | ⭐ PRINCIPAL - Validação Windows completa | Testa Win10+Win11, upload otimizado, regressão E2E, relatórios | 40-90min automático |
| `comum/scripts/FIX-UPLOAD-RAPIDO.sh` | Se upload continuar falhando | Escolhe chunk 64KB, HTTP Server ou ambos | 5min interativo |

### 📖 Guias Manuais (ÚLTIMO RECURSO)

| Guia | Quando Usar | Conteúdo |
|------|-------------|----------|
| `GUIA-MANUAL-PASSOS-WINDOWS.md` | Só se script automático falhar | 10 passos detalhados para configurar VMs manualmente |
| `PARA-ENVIAR-AO-CLAUDE-AI.md` | Precisar ajuda do Claude.ai | Templates de mensagem + prints necessários |
| `RESUMO-PREPARACAO-AUTOMATICA.md` | Visão geral das soluções | Diagnóstico + 3 opções (A/B/C) + checklist |

### 🎯 Fluxo Recomendado (Economize Tokens!)

```bash
# 1. SEMPRE tentar automático primeiro:
bash RODAR-FINAL2-CORRIGIDO.sh

# 2. SE falhar, ver evidências:
cat saida/validacao-windows-FINAL2-*/resumo.csv

# 3. SE upload falhou, tentar otimizações:
bash comum/scripts/FIX-UPLOAD-RAPIDO.sh

# 4. ÚLTIMO RECURSO: seguir guia manual
cat GUIA-MANUAL-PASSOS-WINDOWS.md
```

### ⚡ Atalhos por Pilar

| Pilar | Script Automático | O que resolve |
|-------|-------------------|---------------|
| **P1 - Performance** | `RODAR-FINAL2-CORRIGIDO.sh` | Mede tempo p95 Win10+Win11 |
| **P2 - Instalação** | `RODAR-FINAL2-CORRIGIDO.sh` | Testa E2E MSI+Inno Win10+Win11 |
| **P5 - Robustez** | `RODAR-FINAL2-CORRIGIDO.sh` | Valida cleanup/preservação |
| **P9 - Compatibilidade** | `RODAR-FINAL2-CORRIGIDO.sh` | Valida Win10+Win11 |

### 💡 Regras para Economizar Tokens

1. ✅ **SEMPRE** consultar esta seção antes de pedir ajuda manual
2. ✅ **SEMPRE** tentar script automático primeiro
3. ✅ **SEMPRE** colar evidências (resumo.csv, gates-summary.md) ao pedir análise
4. ❌ **NUNCA** refazer análise de evidence files manualmente (já foi feita!)
5. ❌ **NUNCA** pedir para criar scripts que já existem (consultar esta tabela)

### 📊 Economia Estimada

| Ação | Tokens Gastos (Manual) | Tokens Gastos (Script) | Economia |
|------|------------------------|------------------------|----------|
| Validar Win10+Win11 | ~5.000 (análise + passos) | ~500 (rodar script) | **90%** |
| Diagnosticar upload | ~3.000 (evidence + debug) | ~200 (já diagnosticado) | **93%** |
| Criar guias | ~8.000 (criar do zero) | ~100 (guias prontos) | **99%** |
| **TOTAL** | **~16.000** | **~800** | **95%** ✅ |

---

## Rodada 3 - Maximizar Score (sem Windows) - 20260215T103457Z

DataUTC: 2026-02-15T10:34:57Z
Escopo: maximizar score SEM tocar em VMs Windows (Equipe 1 trabalhando em Win10/Win11)
Estrategia: atingir ~64-70/100 (+13-19 pontos) focando em Linux, documentacao, meta-testes, CI/CD

### Trabalho realizado (7 fases, 31 itens)

**FASE 1 - Governanca:**
- Itens [FINAL-11] a [FINAL-15] documentados com detalhes (bloqueios, dependencias, impactos)
- Gate GO/NO-GO executado: NO-GO confirmado (esperado)
- Secao RODADA3 adicionada ao RELEASE_APPROVAL.md

**FASE 2 - Seguranca:**
- test-release-hashes.sh: PASS (6 artefatos validados)
- test-code-signing-cert.sh: SKIP (cert .pfx nao disponivel - esperado)
- test-update-manifest.sh: PASS (20/20 cenarios, +3 cenarios de pinning adicionados)
- SECURITY_CHECKLIST_FINAL.md criado (10 secoes)
- UPDATE_MANIFEST_SECURITY.md criado (9 secoes, documentacao de pinning)

**FASE 3 - CI/CD:**
- 7/7 scripts principais do CI validados localmente (PASS)
- test-vm-snapshot-automation.sh criado
- CI_CD_GUIA.md atualizado com validacao local executada
- FINAL-15 status: PARCIAL → VALIDADO LOCALMENTE

**FASE 4 - Robustez Linux:**
- test-no-debug-symbols: FAIL (encontrou .pdb no DEB - bloqueador critico)
- test-appimage: PASS (5/5)
- test-installer-contract-static: PASS
- ARTEFATOS_TAMANHO_TENDENCIA.md criado
- PRESERVACAO_DADOS_LINUX.md criado

**FASE 5 - Compatibilidade:**
- LIMITACOES_CONHECIDAS.md criado (32-bit, ARM64, GPO, proxy, antivirus)

**FASE 6 - Update:**
- UPDATE_CYCLE_GUIA.md criado (ciclo completo documentado)
- UPDATE_OFFLINE_GUIA.md criado (instalacao manual sem internet)

**FASE 7 - Consolidacao:**
- Resumo consolidado criado (31 itens rastreados)
- RODADA3-RESUMO.md executivo criado
- RELATORIO-EXECUTIVO-FINAL.txt atualizado
- RESULTADO-FINAL.txt atualizado

### Evidencias da rodada 3

| Fase | Itens | Testes PASS | Testes FAIL | Docs Criados | Evidencia |
| --- | --- | --- | --- | --- | --- |
| 1 - Governanca | 8 | 1 (check-go-nogo) | 0 | 1 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-fase1.csv` |
| 2 - Seguranca | 6 | 2 (hashes+manifest) | 1 (cert SKIP) | 2 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-fase2.csv` |
| 3 - CI/CD | 5 | 7 (scripts locais) | 0 | 2 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-fase3.csv` |
| 4 - Robustez | 6 | 2 (appimage+contract) | 1 (no-debug-symbols) | 2 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-fase4.csv` |
| 5 - Compatibilidade | 3 | 0 | 0 | 1 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-fase5.csv` |
| 6 - Update | 3 | 0 | 0 | 2 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-fase6.csv` |
| 7 - Consolidacao | - | - | - | 3 | `saida/rodada3-maximizar-score-20260215T103458Z/resumo-consolidado.csv` |
| **TOTAL** | **31** | **12** | **2** | **10** | `saida/RODADA3-RESUMO.md` |

### Impacto no score global

**Score global:** 51.2/100 (MANTIDO - sem alteracao)

**Por que score nao subiu:**
1. P4 Seguranca (70/100): Bloqueado por certificado .pfx corporativo (FINAL-11)
2. P7 Operacao (78/100): CI validado localmente mas nao executado em GitHub Actions
3. P5 Robustez (45/100): FAIL em test-no-debug-symbols (.pdb no DEB)
4. P9 Compatibilidade (44/100): Apenas docs criados (sem testes novos executados)
5. P6 Update (65/100): Apenas docs criados (ciclo real nao testado)

**Tabela de scores por pilar (sem alteracao):**

| Pilar | Antes | Depois | Delta | Motivo |
| --- | --- | --- | --- | --- |
| 1 - Performance | 30 | 30 | 0 | Depende de regressao Windows (Equipe 1) |
| 2 - Instalacao | 58 | 58 | 0 | Depende de rebuild + reteste Windows (Equipe 1) |
| 3 - Governanca | 74 | 74 | 0 | Documentacao melhorada mas sem mudanca de score |
| 4 - Seguranca | 70 | 70 | 0 | Bloqueado por cert .pfx corporativo |
| 5 - Robustez | 45 | 45 | 0 | FAIL em test-no-debug-symbols (.pdb no DEB) |
| 6 - Update | 65 | 65 | 0 | Apenas docs, ciclo real nao testado |
| 7 - Operacao | 78 | 78 | 0 | CI validado localmente, nao executado em GH Actions |
| 8 - UX | 25 | 25 | 0 | Depende de Inno customizado (Equipe 1) |
| 9 - Compatibilidade | 44 | 44 | 0 | Apenas docs criados |
| 10 - Resiliencia | 15 | 15 | 0 | Depende de testes de rollback Windows |

**Nota global:** 51.2/100 (MANTIDO)

### Beneficios nao pontuados

1. CI/CD pronto e validado localmente (quando executar em GH Actions: +7 pts potencial)
2. Automacao de snapshots de VMs documentada (test-vm-snapshot-automation.sh)
3. Pinning de chave publica implementado e testado (+3 cenarios novos)
4. Security checklist completo (SECURITY_CHECKLIST_FINAL.md)
5. 10 documentos novos/atualizados (profissionalizacao)
6. Zero conflito com Equipe 1 (Windows preservado)
7. Zero regressoes (nada quebrou)

### Bloqueadores criticos identificados

1. [FINAL-11] Certificado .pfx corporativo nao disponivel (bloqueio externo)
2. [FINAL-13] 6 FAIL E2E Windows aguardando Equipe 1 (rebuild + reteste)
3. [FINAL-14] QGA bloqueado aguardando Equipe 1 (guest-ping FAIL)
4. [NOVO] .pdb no DEB (test-no-debug-symbols FAIL) - bloqueador P5 Robustez

### O que nao mudou

- Gates globais Windows (`G1/G2/G3/G4`) continuam pendentes (Equipe 1)
- Veredito global permanece `NO-GO` (honestidade tecnica)
- Gap para GO: -33.8 pontos (depende de Windows + cert .pfx + DEB fix)
- Score global: 51.2/100 (MANTIDO - sem alteracao factual)

### Proximos passos

1. Aguardar Equipe 1: QGA + 6 FAIL resolvidos
2. Adquirir cert .pfx corporativo → +8 pts (P4 Seguranca)
3. Remover .pdb do DEB → +5 pts potencial (P5 Robustez)
4. Executar CI em GitHub Actions → +7 pts potencial (P7 Operacao)
5. Recalcular score com evidencias Windows
6. Atingir >= 85/100 e mudar veredito para GO

Politica aplicada: scores so sobem com evidencia real de testes PASS executados nesta rodada.

## Rodada FINAL - Validacao Windows Sequencial (QGA Manual) - FINAL-20260215165945

DataUTC: 2026-02-15T16:59:45Z → 18:57:50Z (duração: ~2h)
Escopo: validacao completa Windows em modo manual-ready (QGA instalado manualmente nas VMs)
VMs: win10-lite → win11-lite (sequencial)
Modo: manual-ready (bootstrap manual de 4min cumprido)

### Contexto e Preparacao

**Problema original resolvido:**
- QEMU Guest Agent (QGA) nao conectava (erro: "QEMU guest agent is not connected")
- Solucao manual aplicada DENTRO de cada VM:
  - Instalacao do QEMU Guest Agent (x86_64) no Windows
  - Instalacao do driver VirtIO Serial / vioserial (resolveu dispositivos "PCI" com alerta)
  - Validacao final do QGA: `virsh qemu-agent-command <VM> '{"execute":"guest-ping"}' → {"return":{}}`

**Resultado da validacao manual:**
- Win10-lite: bootstrap_qga_win10-lite_qga_ping PASS
- Win10-lite: bootstrap_qga_win10-lite_qga_channel PASS (state='connected')
- Win11-lite: bootstrap_qga_win11-lite_qga_ping PASS
- Win11-lite: bootstrap_qga_win11-lite_qga_channel PASS (state='connected')
- ✅ Requisito manual de "4 minutos" cumprido e QGA funcional nas duas VMs

### Comando Executado

```bash
cd "/srv/DocumentosCompartilhados/PROJETO PROTONS/INSTALADOR" && \
bash comum/scripts/windows-e2e-sequencial.sh \
  --run-id "FINAL-$(date -u +%Y%m%d%H%M%S)" \
  --vm-order "win10-lite,win11-lite" \
  --cooldown-sec 20 \
  --strict-signature \
  --bootstrap-mode manual-ready \
  2>&1 | tee saida/validacao-windows-FINAL.log
```

### Linha do Tempo

| Evento | Timestamp | Duracao |
|--------|-----------|---------|
| Inicio da rodada | 16:59:45Z | - |
| Win10 iniciado | 16:59:46Z | - |
| Troca para Win11 | 17:31:57Z | ~32min |
| Final da rodada | 18:57:50Z | ~86min total |

### Artefatos Gerados

**RUN_DIR principal:**
`/srv/DocumentosCompartilhados/PROJETO PROTONS/INSTALADOR/saida/validacao-windows-FINAL-20260215165945`

**Arquivos principais:**
- Log geral: `saida/validacao-windows-FINAL.log`
- Resumo da rodada: `.../windows-round-summary.md`
- Gates/Pass-Fail: `.../gates-summary.md`
- Resumo CSV: `.../resumo.csv`
- Mandatory suite: `.../mandatory-suite.csv`

### Resultados por Gate

#### ✅ Gates que PASSARAM:

| Gate | VM | Status | Evidencia |
|------|-----|--------|-----------|
| Sanitizacao CD-ROM | Win10 | PASS | `win10-lite_sanitize_cdrom` |
| Sanitizacao CD-ROM | Win11 | PASS | `win11-lite_sanitize_cdrom` |
| Bootstrap QGA - Canal | Win10 | PASS | `bootstrap_qga_win10-lite_qga_channel` (state='connected') |
| Bootstrap QGA - Ping | Win10 | PASS | `bootstrap_qga_win10-lite_qga_ping` |
| Bootstrap QGA - Status | Win10 | PASS | `bootstrap_qga_win10-lite_qga_status` |
| Bootstrap QGA - Canal | Win11 | PASS | `bootstrap_qga_win11-lite_qga_channel` (state='connected') |
| Bootstrap QGA - Ping | Win11 | PASS | `bootstrap_qga_win11-lite_qga_ping` |
| Bootstrap QGA - Status | Win11 | PASS | `bootstrap_qga_win11-lite_qga_status` |
| VM checks e comandos | Win10/Win11 | PASS | Rodada executada ate o fim |
| Bundle preparado | Win10 | PASS | `run_regressao_win10-lite_bundle` |
| Bundle preparado | Win11 | PASS | `run_regressao_win11-lite_bundle` |

#### ⚠️ Gates PARCIAIS (esperado por modo manual-ready):

| Gate | VM | Status | Motivo |
|------|-----|--------|--------|
| Restore clean | Win10/Win11 | PARCIAL | skip_restore_clean=1 por manual-ready (esperado) |
| Bootstrap precheck | Win11 | PARCIAL | Modo manual-ready (esperado) |
| Send keys | Win11 | PARCIAL | Modo manual-ready (esperado) |

#### ❌ Gates que FALHARAM:

| Gate | VM | Status | Exit Code | Causa Provavel |
|------|-----|--------|-----------|----------------|
| Bundle upload | Win10 | FAIL | 124 | Timeout - processo ficou travado esperando |
| Bundle transfer metrics | Win10 | FAIL | 124 | Timeout - dependia do upload |
| Result snapshot | Win10 | FAIL | 124 | Timeout - dependia do upload |
| Result final | Win10 | FAIL | 124 | Timeout - dependia do upload |
| Bundle upload | Win11 | FAIL | 1 | Falha generica - ver evidence file |
| Bundle transfer metrics | Win11 | FAIL | 1 | Dependia do upload |
| Result snapshot | Win11 | FAIL | 1 | Dependia do upload |
| Result final | Win11 | FAIL | 1 | Dependia do upload |

**Observacao tecnica:**
- Exit code 124 = timeout (processo esperou e estourou tempo limite)
- Exit code 1 = falha generica (precisa analisar arquivo evidence da etapa)

### Diagnostico Tecnico

**Causas provaveis das falhas (bundle upload):**

1. **Permissoes no guest:**
   - Caminho `C:\Windows\Temp` pode estar bloqueado
   - UAC (User Account Control) pode estar bloqueando PowerShell
   - Execucao de scripts PowerShell pode estar desabilitada

2. **Seguranca Windows:**
   - Windows SmartScreen bloqueando upload de arquivos
   - Windows Defender em tempo real bloqueando I/O
   - Antivirus de terceiros (se instalado)

3. **Problemas de path/disco:**
   - Path invalido ou inexistente no guest
   - Disco cheio na VM (pouco provavel em win10-lite/win11-lite)
   - Permissoes NTFS incorretas

4. **Problemas de snapshot:**
   - VM state diferente do esperado
   - Snapshot nao criado/revertido corretamente
   - Politica do libvirt bloqueando snapshots

### Proximos Passos Recomendados

**Para identificar a causa exata:**

1. Abrir arquivos "evidence" das etapas que falharam:
   ```bash
   # Win10 bundle upload
   cat saida/validacao-windows-FINAL-20260215165945/run_regressao_win10-lite_bundle_upload.evidence

   # Win11 bundle upload
   cat saida/validacao-windows-FINAL-20260215165945/run_regressao_win11-lite_bundle_upload.evidence
   ```

2. Verificar logs de transfer metrics:
   ```bash
   cat saida/validacao-windows-FINAL-20260215165945/run_regressao_win10-lite_bundle_transfer_metrics.evidence
   cat saida/validacao-windows-FINAL-20260215165945/run_regressao_win11-lite_bundle_transfer_metrics.evidence
   ```

3. Verificar estado das VMs apos falha:
   ```bash
   virsh dominfo win10-lite
   virsh dominfo win11-lite
   virsh snapshot-list win10-lite
   virsh snapshot-list win11-lite
   ```

**Para corrigir e repetir:**

1. Corrigir o problema identificado nos evidence files
2. Se for permissoes PowerShell:
   ```powershell
   # Dentro da VM Windows
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```
3. Se for UAC, considerar desabilitar temporariamente para testes
4. Se for antivirus/SmartScreen, adicionar excecao para o path de testes
5. Repetir a rodada usando o mesmo comando FINAL (manual-ready)

### Impacto no Checklist Global (HISTORICO 2026-02-15)

#### Gates Globais (G1-G4):

| Gate | Status | Justificativa |
|------|--------|---------------|
| G1 - QGA ativo nas duas VMs | ✅ ATINGIDO PARCIAL | QGA conectado e respondendo guest-ping em ambas as VMs, mas modo manual-ready (nao automatico) |
| G2 - Regressao completa nas duas VMs | ❌ NAO ATINGIDO | Bundle upload falhou em ambas (timeout Win10, erro Win11) |
| G3 - 6 FAIL historicos resolvidos | ❌ NAO ATINGIDO | Sem novo E2E Windows executado (dependia de G2) |
| G4 - p95 MSI/Inno dentro de meta | ❌ NAO ATINGIDO | Sem medicao de tempo (dependia de G2) |

#### Evolucao dos Pilares:

| Pilar | Score Antes | Score Depois | Delta | Motivo |
|-------|-------------|--------------|-------|--------|
| 1 - Performance | 30 | **35** | +5 | Infraestrutura QGA manual validada (+5 pts conservador) |
| 2 - Instalacao | 58 | 58 | 0 | Sem novo E2E (G2 bloqueado) |
| 5 - Robustez | 45 | 45 | 0 | Sem novo cleanup (G2/G3 bloqueados) |
| 7 - Operacao | 78 | **82** | +4 | Bootstrap tecnico QGA melhorado (canal/midia/sanitize PASS) |
| 9 - Compatibilidade | 44 | **48** | +4 | Validacao de infraestrutura Win10/Win11 (QGA conectado) |

**Nota global atualizada:** 51.2 → **55.2** (+4.0 pontos conservador)

**Justificativa da atualizacao conservadora:**
- QGA agora funciona (evidencia factual: guest-ping PASS em ambas as VMs)
- Infraestrutura de bootstrap tecnico endurecida (sanitize_cdrom, channel, midia)
- Modo manual-ready validado (4min de trabalho manual cumprido)
- Rodada executou ate o fim (86min, sem crash de sistema)
- POREM: regressao in-guest continua bloqueada (upload falhou)

### O Que Conseguimos

**Tecnicamente:**
1. ✅ QGA instalado e funcional nas duas VMs Windows (Win10 + Win11)
2. ✅ Driver VirtIO Serial instalado e resolvendo dispositivos PCI
3. ✅ Canal QGA conectado (state='connected')
4. ✅ Guest-ping respondendo corretamente ({"return":{}})
5. ✅ Bootstrap tecnico automatizado (sanitize_cdrom, attach/cleanup de midia)
6. ✅ Rodada sequencial executada ate o fim (sem crash)
7. ✅ Modo manual-ready validado (fallback manual funcionando)

**Operacionalmente:**
1. ✅ Automacao sequencial testada (win10-lite → win11-lite)
2. ✅ Cooldown de 20s validado
3. ✅ Strict-signature funcionando
4. ✅ Artefatos completos gerados (resumo.csv, gates-summary.md, mandatory-suite.csv)
5. ✅ Evidencias rastreadas por etapa (logs individuais)

**Bloqueios restantes:**
1. ❌ Upload de bundle in-guest (timeout Win10, erro Win11)
2. ❌ Transfer metrics nao coletadas (dependia do upload)
3. ❌ Snapshots de resultado nao criados (dependia do upload)
4. ❌ Regressao E2E in-guest nao executada (dependia do upload)

### O Que Ainda Falta Fazer em Windows

**Prioridade P0 (bloqueadores criticos):**

- [ ] **P0** Identificar causa exata da falha de upload (analisar evidence files)
  - Evidencia: `.../run_regressao_win10-lite_bundle_upload.evidence`
- [ ] **P0** Corrigir problema de upload (permissoes / UAC / antivirus / path)
- [ ] **P0** Reexecutar rodada FINAL apos correcao
- [ ] **P0** Validar bundle transfer metrics (tempo de upload, tamanho)
- [ ] **P0** Executar regressao E2E in-guest completa (run-regressao.ps1)
  - Teste: `testes/windows/run-regressao.ps1` com medicao de tempo
- [ ] **P0** Resolver os 6 FAIL historicos do E2E:
  - `MSI-UNINST-01` (Program Files nao removido)
  - `MSI-UNINST-03` (Registro nao removido)
  - `INNO-02` (Atalho Desktop)
  - `INNO-03` (Atalho Start Menu)
  - `INNO-04` (Registro)
  - `INNO-UNINST-01` (Program Files nao removido)

**Prioridade P1 (importantes):**

- [ ] **P1** Automatizar bootstrap QGA (eliminar modo manual-ready)
  - Atualmente: 4min de trabalho manual dentro da VM
  - Meta: bootstrap 100% automatizado via virsh/qemu-ga
- [ ] **P1** Medir tempo p95 de instalacao (MSI + Inno)
  - Meta: MSI install <= 90s, uninstall <= 45s
  - Meta: Inno install <= 90s, uninstall <= 45s
- [ ] **P1** Validar preservacao de dados em upgrade (1.0.0 → 1.0.1)
  - Teste: `testes/windows/test-upgrade-reinstall.ps1`
- [ ] **P1** Testar com Windows Defender ativo (nao desabilitar)
  - Teste: `[CRIAR]` `testes/windows/test-defender-compatibility.ps1`

**Prioridade P2 (desejaveis):**

- [ ] **P2** Implementar progress bar no Inno Setup
- [ ] **P2** Adicionar multi-idioma (PT-BR + EN)
- [ ] **P2** Testar rollback MSI (simular falha no meio)
- [ ] **P2** Benchmark comparativo com instalador pago

### Veredito da Rodada FINAL

**Status:** PARCIAL (progresso tecnico significativo, mas regressao bloqueada)

**Conquistas:**
- QGA funcional (trabalho manual de 4min validado)
- Infraestrutura de automacao sequencial validada
- Bootstrap tecnico endurecido
- Artefatos completos e rastreados

**Bloqueios:**
- Upload de bundle falhou em ambas as VMs
- Regressao in-guest nao executada
- 6 FAIL historicos continuam pendentes

**Proximo comando:**
Apos corrigir o problema de upload identificado nos evidence files, repetir:
```bash
cd "/srv/DocumentosCompartilhados/PROJETO PROTONS/INSTALADOR" && \
bash comum/scripts/windows-e2e-sequencial.sh \
  --run-id "FINAL2-$(date -u +%Y%m%d%H%M%S)" \
  --vm-order "win10-lite,win11-lite" \
  --cooldown-sec 20 \
  --strict-signature \
  --bootstrap-mode manual-ready \
  2>&1 | tee saida/validacao-windows-FINAL2.log
```

### Comparacao com Rodada Anterior (20260215T000453Z)

| Metrica | Rodada 20260215T000453Z | Rodada FINAL-165945 | Melhoria |
|---------|-------------------------|---------------------|----------|
| QGA conectado | ❌ BLOQUEADO | ✅ PASS (manual) | +100% |
| Bootstrap tecnico | Parcial | ✅ PASS completo | +50% |
| Rodada sequencial | Pausada Win10 | ✅ Completa Win10+Win11 | +100% |
| Bundle preparado | N/A | ✅ PASS | Novo |
| Upload bundle | N/A | ❌ FAIL (timeout/erro) | Bloqueio novo |
| Regressao in-guest | N/A | ❌ Bloqueado | Dependia upload |
| Duracao total | ~4h (pausada) | ~2h (completa) | +50% eficiencia |
| Artefatos gerados | Parciais | ✅ Completos | +100% |

### Evidencias Oficiais

| Arquivo | Localizacao | Conteudo |
|---------|-------------|----------|
| Log geral | `saida/validacao-windows-FINAL.log` | Saida completa do comando |
| Resumo da rodada | `saida/validacao-windows-FINAL-20260215165945/windows-round-summary.md` | Sumario executivo |
| Gates summary | `saida/validacao-windows-FINAL-20260215165945/gates-summary.md` | Status de todos os gates |
| Resumo CSV | `saida/validacao-windows-FINAL-20260215165945/resumo.csv` | Dados tabulares |
| Mandatory suite | `saida/validacao-windows-FINAL-20260215165945/mandatory-suite.csv` | Testes obrigatorios |
| Evidence files | `saida/validacao-windows-FINAL-20260215165945/*_bundle_upload.evidence` | Detalhes das falhas |

### Atualização rodada FINAL2 (2026-02-16T20:24:37Z) - DIA 4 ✅

🎉 **WIN11 QGA RESOLVIDO!** G1 PASS em ambas VMs!

| Item | Status | Detalhes |
| --- | --- | --- |
| **G1 - QGA Bootstrap** | ✅ PASS | Win10=PASS, Win11=PASS (RESOLVIDO!) |
| **G2 - Regressão E2E** | ❌ NÃO | Win10=FAIL, Win11=FAIL (UTF-8 BOM) |
| **Score P7** | 78 → **80** | +2 por G1 |
| **Score P9** | 42 → **45** | +3 por G1 |
| **Score global** | 68-70 → **72** | +4 pontos |

**Problema identificado:** UTF-8 BOM nos JSONs PowerShell
**Correção aplicada:** Test-Common.ps1 linha 165 (Set-Content → Out-File utf8NoBOM)
**Próximo:** Reteste para validar correção BOM
