# RISCOS E PENDENCIAS FINAIS

DataUTC: 2026-02-17T21:17:31Z
RunIdBaseline: FINAL2-20260217150331
RunIdProd: DIA7AUTO-20260217T211731Z
FonteGates: saida/validacao-windows-FINAL2-20260217150331/gates-summary.md

## Riscos abertos (criticos)
- Certificado corporativo `.pfx` indisponivel para assinatura oficial.
  - Evidencias:
    - `documentos/RELEASE_APPROVAL.md` (itens [FINAL-11] e [FINAL-12])
    - `saida/dia7-DIA7AUTO-20260217T211731Z/go-nogo-report.md`

## Pendencias operacionais
- Obter certificado corporativo real (Code Signing) com cadeia valida.
- Assinar artefatos MSI/EXE com Authenticode + timestamp RFC3161.
- Executar CI/CD em PR/tag real (item [FINAL-15], atualmente parcial).
- Validar SmartScreen/GPO corporativa com os binarios assinados.

## Itens fechados nesta rodada
- Saneamento de CD-ROM antes de bootstrap (evita referencia quebrada no XML).
  - Evidencia: `saida/validacao-windows-20260215T000453Z/win10-lite_sanitize_cdrom.log`
- Troca de ISO helper e limpeza de midia ao final do bootstrap em Win10.
  - Evidencias:
    - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_change_media.log`
    - `saida/validacao-windows-20260215T000453Z/bootstrap_qga_win10-lite_cleanup_media.log`
- Suite obrigatoria executada e rastreada.
  - Evidencia: `saida/validacao-windows-20260215T000453Z/mandatory-suite.csv`
- VMs finalizadas em `shut off` apos a suite.
  - Evidencia: `saida/validacao-windows-20260215T000453Z/final_vm_status_after_suite.log`

## Decisao atual
- `GO` tecnico interno para entrega interna/piloto.
- `NO-GO` comercial externo ate fechamento de assinatura oficial.

---

## Atualizacao 2026-02-15T12:00:00Z - Trabalho Paralelo

### Melhorias de governanca e infraestrutura (sem impacto em gates Windows)

**FASE 1 - Governanca:**
- CHANGELOG.md formal criado (Keep a Changelog + Semantic Versioning)
- RELEASE_APPROVAL.md atualizado com secao [FINAL] (15 itens, 10 concluidos)
- test-changelog-format.sh criado e em PASS
- Score Pilar 3 (Governanca): 64 → 74 (+10 pontos)

**FASE 2 - CI/CD:**
- .github/workflows/instalador-ci.yml criado (pipeline CI isolado para INSTALADOR)
- .github/workflows/instalador-release.yml criado (pipeline release isolado para INSTALADOR)
- documentos/CI_CD_GUIA_INSTALADOR.md criado

**FASE 3 - Meta-testes:**
- 11 validacoes estaticas executadas: 10 PASS, 1 SKIP (certificado)
- test-code-signing-cert.sh criado (esqueleto)
- test-release-hashes.sh validado em PASS
- test-no-debug-symbols.sh validado em PASS

**FASE 4 - Linux:**
- AppImage: 5/5 PASS (revalidado)
- Contrato estatico: PASS
- Update manifest: PASS (strict + tests)

**FASE 5 - Documentacao:**
- REQUISITOS_MINIMOS.md atualizado
- COMPATIBILIDADE_TESTADA.md atualizado
- Este arquivo (RISCOS_PENDENCIAS_FINAL.md) atualizado

### Riscos de compatibilidade conhecidos (Pilar 9)

**Nao testado (gaps de cobertura):**
- ARM64 (Windows e Linux): sem build planejado nesta release
- x86 32-bit: sem suporte planejado
- Politicas corporativas (GPO): sem testes em ambiente restrito
- Antivirus terceiros: Avast, AVG, Kaspersky, Norton (nao testados)
- Distribuicoes Linux nao-Ubuntu: Debian, Fedora, Arch (compatibilidade provavel, nao validada)
- Proxy corporativo HTTP(S): update automático via proxy (nao testado)

**Bloqueios de compatibilidade atuais:**
- SmartScreen bloqueia binarios nao assinados (certificado .pfx pendente)
- QGA bloqueado em Win10/Win11 (guest-ping FAIL) impede regressao automatica
- 6 FAIL do E2E Windows (cleanup MSI/Inno) ainda abertos

**Impacto no score:**
- Pilar 9 (Compatibilidade): 42/100 (gap: -53 pontos para meta 95)
- Documentacao tecnica criada: +2 pontos conservador estimado (ainda nao refletido no score global)

### Pendencias operacionais atualizadas

**Bloqueadores criticos (mantidos):**
1. Certificado .pfx corporativo nao disponivel → assinatura de codigo bloqueada
2. QGA bloqueado (guest-ping FAIL) → regressao Windows bloqueada
3. 6 FAIL do E2E Windows (2026-02-07) → rebuild + reteste pendente

**Preparacao concluida (sem bloqueio):**
4. CI/CD criado mas nao testado → execucao pendente (nao bloqueia release)
5. Esqueleto de assinatura criado → aguardando certificado
6. Documentacao tecnica completa → pronta para publicacao

### Score global conservador

**Antes do trabalho paralelo:** 49.2/100
**Depois do trabalho paralelo:** 51.2/100 (+2.0 pontos)
- Governanca: 64 → 74 (+10)
- Compatibilidade: 42 → 44 (+2, apenas docs)

**Veredito:** NO-GO mantido (honestidade tecnica)
**Gap para GO:** 7 de 8 criterios de mudanca nao atendidos

### Proximos passos obrigatorios

1. Manual 1x em win10-lite e win11-lite (servico qemu-ga em Automatic + Running)
2. Confirmar guest-ping PASS nas duas VMs
3. Reexecutar rodada sequencial para destravar G1/G2
4. Rebuild MSI/Inno com correcoes de cleanup
5. Reteste E2E Windows (meta: 6 FAIL → 0 FAIL)
6. Adquirir certificado .pfx corporativo e assinar artefatos
7. Executar CI/CD em PR real (validacao de sintaxe)
8. Atingir score global >= 85/100 para mudar veredito para GO

---

## Atualizacao 2026-02-15T10:13:34Z - Rodada Paralela 2 (sem Windows)

Resumo factual:
- Rodada `20260215T101334Z` executada sem interacao com `win10-lite` e `win11-lite`.
- Trilha paralela segura: 11/11 PASS (`resumo.csv`).
- Meta-validacoes adicionais: hashes, debug symbols, changelog e gate GO/NO-GO em PASS.
- Checklist reconciliado com estado real dos scripts/testes ja implementados.

Evidencias:
- `saida/validacao-paralela-20260215T101334Z/resumo.csv`
- `saida/validacao-paralela-20260215T101334Z/90-release-hashes.log`
- `saida/validacao-paralela-20260215T101334Z/91-no-debug-symbols.log`
- `saida/validacao-paralela-20260215T101334Z/92-changelog-format.log`
- `saida/validacao-paralela-20260215T101334Z/93-go-nogo-gate.log`
- `saida/validacao-paralela-20260215T101334Z/go-nogo-20260215T101334Z.md`

Impacto em risco/veredito:
- Nenhum risco critico Windows foi removido nesta rodada.
- Veredito global permanece `NO-GO`.
- Politica de score conservadora mantida: sem promocao de score global enquanto `G1/G2/G3/G4` permanecerem pendentes.
