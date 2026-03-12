# Changelog

Todas as mudanças notáveis neste projeto serão documentadas neste arquivo.

O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/),
e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [1.0.1] - 2026-02-17

### Status de Release

**DataUTC:** 2026-02-17T21:17:31Z
**Status técnico interno:** GO (80/100)
**Status comercial externo:** NO-GO (depende de certificado corporativo `.pfx`)

### Atualizado

- Regressao Windows consolidada em `FINAL2-20260217150331` com `PASS 68 | FAIL 0 | PARCIAL 4`.
- Gates `G1/G2/G3/G4` em PASS na rodada de referencia (`saida/validacao-windows-FINAL2-20260217150331/gates-summary.md`).
- Fechamento Dia 7 consolidado com veredito duplo (interno x comercial) em `saida/dia7-DIA7AUTO-20260217T211731Z/`.
- Checklist oficial consolidado em `CHECKLIST.md`.

### Mantido como Bloqueio Comercial

- Certificado corporativo `.pfx` ainda indisponivel para assinatura oficial Authenticode.
- Item [FINAL-15] (CI/CD em PR/tag real) segue parcial e nao bloqueia trilha tecnica interna.

## [1.0.0] - 2026-02-15

### Baseline de Release

Esta é a primeira release candidata do instalador Protons com governança formal.

**DataBaselineUTC:** 2026-02-15T00:35:00Z
**Branch:** master
**Status:** NO-GO (em validação, 6 FAIL E2E Windows históricos)

### Adicionado

#### Instaladores Multi-Plataforma
- **MSI (Windows):** Instalador nativo Windows Installer para Win10/Win11
  - Suporte a instalação silenciosa (`/qn`)
  - Integração com Windows Registry
  - Criação de shortcuts no Menu Iniciar
  - Preservação de dados de usuário em `%APPDATA%\Protons`

- **Inno Setup (Windows):** Instalador alternativo para cenários avançados
  - Interface customizável
  - Suporte a instalação silenciosa (`/VERYSILENT`)
  - Preservação de dados de usuário

- **AppImage (Linux):** Pacote portável sem dependências
  - 5/5 testes de contrato em PASS
  - Validação estática de contrato em PASS
  - Suporte Ubuntu 20.04/22.04

- **DEB (Linux):** Pacote nativo Debian/Ubuntu
  - Integração com apt
  - Changelog Debian conforme policy

#### Segurança e Governança
- **Update Manifest:** Sistema de atualização seguro
  - Validação estrita de assinaturas (`--strict-signature` PASS)
  - Testes negativos de segurança (manifesto corrompido, assinatura inválida)
  - Suite completa de testes em PASS

- **SBOM (Software Bill of Materials):**
  - CycloneDX JSON validado
  - SPDX complementar
  - Validação automática via `validate-sbom.sh`

- **Segurança de Release:**
  - Validação de termos proibidos (`validate-prohibited-terms.sh` PASS)
  - Baseline de segurança documentada (`security-baseline-20260211T002214Z.md`)
  - Checklist de cibersegurança com evidências rastreáveis

- **Governança de Release:**
  - RELEASE_APPROVAL.md com papéis formais (Owner, QA, Revisor)
  - Critério GO/NO-GO definido
  - Matriz de evidências (`MATRIZ_EVIDENCIAS_FINAL.md`)
  - Registro de riscos (`RISCOS_PENDENCIAS_FINAL.md`)

#### Automação e Testes
- **Rodada Linux Dedicada (2026-02-11):**
  - 13 scripts de validação executados
  - AppImage: 5/5 PASS
  - Contrato estático: PASS
  - Logs rastreáveis em `saida/validacao-linux-20260211T212023Z/`

- **Infraestrutura de Teste Windows:**
  - VMs `win10-lite` e `win11-lite` configuradas
  - Bootstrap QGA com melhorias técnicas (2026-02-15):
    - Saneamento de CD-ROM: PASS
    - Attach de ISO helper: PASS
    - Cleanup de mídia: PASS
    - Fallback manual formalizado

- **Scripts de Validação:**
  - `validate-prohibited-terms.sh`
  - `validate-sbom.sh`
  - `validate-update-manifest.sh`
  - `validate-version.sh`
  - `validate-script-comments.sh`
  - `lint-build.sh`
  - `test-checklist-graficos.sh`
  - `test-checklist-score-evidence.sh`
  - `audit-host-state.sh`
  - `collect-test-evidence.sh`

#### Documentação
- **Documentação Técnica:**
  - RELEASE_APPROVAL.md (governança formal)
  - MATRIZ_EVIDENCIAS_FINAL.md (44 itens rastreáveis)
  - RISCOS_PENDENCIAS_FINAL.md (riscos conhecidos)
  - CHECKLIST.md (auditoria honesta 10 pilares)
  - RELATORIO-EXECUTIVO-FINAL.txt (resumo executivo)
  - RESULTADO-FINAL.txt (veredito técnico)

- **Guias Operacionais:**
  - RELATORIO_EXECUCAO_WINDOWS_VM.md (infraestrutura de VMs)
  - SETUP_PRECOMMIT_HOOKS.md (hooks de desenvolvimento)

### Problemas Conhecidos (NO-GO)

#### Bloqueios Críticos (Impedem Release)
1. **E2E Windows - 6 FAIL históricos (2026-02-07):**
   - `MSI-UNINST-01`: Program Files não removido na desinstalação
   - `MSI-UNINST-03`: Registry não removido na desinstalação
   - `INNO-02`: Falha em teste de instalação Inno
   - `INNO-03`: Falha em teste de instalação Inno
   - `INNO-04`: Falha em teste de instalação Inno
   - `INNO-UNINST-01`: Program Files não removido na desinstalação Inno

2. **QGA Bloqueado (2026-02-15):**
   - `guest-ping` FAIL em Win10 ("QEMU guest agent is not connected")
   - Regressão in-guest não pode executar sem QGA
   - Win11 não testado nesta rodada (sequência pausada)
   - **Gates bloqueados:** G1, G2, G3, G4 = NÃO

3. **Certificado Corporativo:**
   - Certificado `.pfx` de code signing não disponível
   - Assinatura final de MSI/EXE pendente
   - Timestamp RFC3161 pendente

#### Gaps de Qualidade (Não Bloqueantes, Reduzem Score)
- **Performance (30/100):** Zero medição de tempo de instalação/desinstalação (p95 pendente)
- **UX (25/100):** Sem progress bar, tempo estimado, multi-idioma
- **Resiliência/Rollback (15/100):** MSI rollback nunca testado, Inno sem rollback
- **Compatibilidade (42/100):** QGA bloqueado, regressão automática não validada
- **CI/CD:** Nenhum workflow GitHub Actions (`.github/workflows/` vazio)

### Score Global (Auditoria Honesta - 10 Pilares)

**Nota Global:** 49.2/100 (Meta: >= 85/100)
**Gap para Produção:** -35.8 pontos
**Veredito:** NO-GO

| Pilar | Score | Gap para Meta |
|-------|-------|---------------|
| 1 - Performance | 30 | -60 |
| 2 - Instalação | 58 | -40 |
| 3 - Governança | 64 | -26 |
| 4 - Segurança | 70 | -25 |
| 5 - Robustez | 45 | -50 |
| 6 - Update | 65 | -30 |
| 7 - Operação | 78 | -17 |
| 8 - UX | 25 | -65 |
| 9 - Compatibilidade | 42 | -53 |
| 10 - Resiliência/Rollback | 15 | -80 |

### Próximos Passos Técnicos Obrigatórios

1. **Desbloquear QGA:**
   - Manual 1x em `win10-lite` e `win11-lite`
   - Garantir serviço `qemu-ga` em `Automatic` + `Running`
   - Confirmar `guest-ping` PASS

2. **Reexecutar E2E Windows:**
   - Rebuild MSI/Inno com correções de cleanup
   - Reteste completo (esperado: 6 FAIL → 0 FAIL)
   - Validar Win11 (não testado ainda)

3. **Adquirir Certificado Corporativo:**
   - `.pfx` válido por >= 12 meses
   - Cadeia válida em raiz confiável Windows
   - Timestamp RFC3161 configurado

4. **Implementar Melhorias de Score:**
   - Criar CI/CD (GitHub Actions)
   - Adicionar medição p95 de performance
   - Testar MSI rollback
   - Melhorar UX (progress bar, multi-idioma)

### Evidências Rastreáveis

- **Rodada Windows:** `saida/validacao-windows-20260215T000453Z/`
  - `resumo.csv`, `gates-summary.md`, `mandatory-suite.csv`
  - Logs bootstrap QGA, fallback manual

- **Rodada Linux:** `saida/validacao-linux-20260211T212023Z/`
  - `resumo.csv`, logs AppImage, contrato estático

- **Baseline Segurança:** `saida/security-baseline-20260211T002214Z.md`

- **E2E Histórico:** `saida/test-logs/e2e-results-20260207T174707.txt`

---

## Convenções de Versionamento

Este projeto segue [Semantic Versioning 2.0.0](https://semver.org/):
- **MAJOR:** Quebra compatibilidade (ex: 2.0.0)
- **MINOR:** Adiciona funcionalidade compatível (ex: 1.1.0)
- **PATCH:** Correções compatíveis (ex: 1.0.1)

## Política de Manutenção de Changelog

- **[Adicionado]:** Novas funcionalidades
- **[Modificado]:** Mudanças em funcionalidades existentes
- **[Descontinuado]:** Funcionalidades que serão removidas
- **[Removido]:** Funcionalidades removidas
- **[Corrigido]:** Correções de bugs
- **[Segurança]:** Correções de vulnerabilidades

---

**Aprovação de Release:**
Ver `documentos/RELEASE_APPROVAL.md` para critérios GO/NO-GO e aprovações formais.

**Matriz de Evidências:**
Ver `documentos/MATRIZ_EVIDENCIAS_FINAL.md` para rastreabilidade completa.
