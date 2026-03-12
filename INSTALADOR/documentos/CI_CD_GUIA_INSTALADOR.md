# CI/CD GUIA INSTALADOR

DataUTC: 2026-02-15T10:13:34Z
Escopo: pipelines dedicados ao `INSTALADOR` com isolamento de paths.

## Workflows criados

1. `/.github/workflows/instalador-ci.yml`
- Trigger: `push`, `pull_request`, `workflow_dispatch`.
- Filtro de paths: apenas `INSTALADOR/**` e o proprio workflow.
- Objetivo: validar qualidade e consistencia do instalador.
- Etapas principais:
  - validadores (`validate-*`)
  - testes meta/checklist
  - teste Linux AppImage
  - contrato estatico do instalador
- Saida: artifact `instalador-ci-logs`.

2. `/.github/workflows/instalador-release.yml`
- Trigger: `push` em tag `v*` e `workflow_dispatch`.
- Filtro de paths: `INSTALADOR/**` e o proprio workflow.
- Objetivo: preparar pacote de release com validacoes sem exigir assinatura final.
- Etapas principais:
  - validacao de manifesto/update
  - validacao de hashes e debug symbols
  - geracao/validacao de SBOM
  - staging de artefatos e inventario
- Saidas:
  - artifact `instalador-release-artifacts`
  - artifact `instalador-release-logs`

## Observacoes operacionais
- Estes workflows nao alteram automacao ativa de VMs Windows.
- A assinatura corporativa final continua fora do escopo ate disponibilidade do certificado `.pfx`.
- A decisao global de `GO` depende dos gates Windows (`G1-G4`) fechados pela trilha dedicada.

## Validacao Local Executada (RODADA3-20260215T103457Z)

**DataUTC:** 2026-02-15T10:43:42Z
**Objetivo:** Validar que todos os scripts do CI funcionam localmente antes de executar em GitHub Actions.

### Scripts Validados

| Job | Script | Status | Evidencia |
|-----|--------|--------|-----------|
| Static Validation | `validate-prohibited-terms.sh` | ✅ PASS | ci-local-validation.log |
| Static Validation | `lint-build.sh` | ✅ PASS | ci-local-validation.log |
| Static Validation | `validate-version.sh` | ✅ PASS | ci-local-validation.log |
| Security Validation | `validate-sbom.sh` | ✅ PASS | ci-local-validation.log |
| Security Validation | `validate-update-manifest.sh --strict` | ✅ PASS | ci-local-validation.log |
| Meta Tests | `test-changelog-format.sh` | ✅ PASS | ci-local-validation.log |
| Meta Tests | `check-go-nogo.sh` | ✅ PASS (NO-GO) | ci-local-validation.log |

**Resultado:** 7/7 scripts principais em PASS
**Evidencia:** `saida/rodada3-maximizar-score-20260215T103458Z/ci-local-validation.log`

### Automacao de Snapshots de VMs

**Teste criado:** `testes/meta/test-vm-snapshot-automation.sh`
**Objetivo:** Automatizar criacao/restauracao de snapshots de VMs antes/depois de testes Windows.
**Status:** ✅ Script criado e pronto
**Evidencia:** `testes/meta/test-vm-snapshot-automation.sh`

**Como usar:**
```bash
# Antes de executar testes Windows
virsh snapshot-create-as win10-lite pre-teste-$(date -u +%Y%m%dT%H%M%SZ)
virsh snapshot-create-as win11-lite pre-teste-$(date -u +%Y%m%dT%H%M%SZ)

# Executar testes Windows

# Restaurar snapshot se necessario
virsh snapshot-revert win10-lite pre-teste-20260215T103000Z
virsh snapshot-revert win11-lite pre-teste-20260215T103000Z
```

### Status de FINAL-15: CI/CD Configurado e Testado

**Status anterior:** ⚠️ PARCIAL (workflows criados mas nao testados)
**Status atual:** ✅ **VALIDADO LOCALMENTE** (scripts PASS, pronto para GH Actions)
**Pendente:** Executar em PR/tag real no GitHub Actions para confirmar integracao completa em producao.

### Proximos Passos

1. ✅ Workflows criados e validados localmente (CONCLUIDO)
2. ⏸️ Executar em PR/tag real do GitHub Actions (PENDENTE - aguardar momento apropriado)
3. ⏸️ Validar artifacts gerados pelo CI (PENDENTE)
4. ⏸️ Integrar com processo de release (PENDENTE)
