# Guia de CI/CD - Protons Instalador

**Data de criação:** 2026-02-15
**Status:** Infraestrutura criada, aguardando execução
**Parte de:** FASE 2 - Trabalho Paralelo (Preparação para Produção)

---

## Índice

- [Visão Geral](#visão-geral)
- [Workflows Disponíveis](#workflows-disponíveis)
- [CI - Continuous Integration](#ci---continuous-integration)
- [Release - Build e Publicação](#release---build-e-publicação)
- [Configuração de Secrets](#configuração-de-secrets)
- [Badges de Status](#badges-de-status)
- [Troubleshooting](#troubleshooting)

---

## Visão Geral

O projeto Protons Instalador possui 2 workflows principais de GitHub Actions:

1. **`instalador-ci.yml`**: Executa validações, testes e checks locais do instalador em push/PR
2. **`instalador-release.yml`**: Executa validações de release e staging de artefatos em tags

**Estado atual:** Workflows criados mas **NÃO TESTADOS** (infraestrutura preparada).

**Bloqueios conhecidos:**
- Certificado corporativo `.pfx` não disponível (assinatura de código mock)
- Builds Windows são mock (aguardam implementação real)
- Nunca executado em CI real (validação sintática pendente)

---

## Workflows Disponíveis

### 1. CI - Continuous Integration (`.github/workflows/instalador-ci.yml`)

**Trigger:**
- Push em branches: `main`, `master`, `develop`, `feature/**`, `fix/**`
- Pull requests para: `main`, `master`, `develop`

**Jobs:**
1. **static-validation**: Validações estáticas (termos proibidos, lint, versão)
2. **security-validation**: SBOM, update manifest, validações estritas
3. **meta-tests**: Testes meta (changelog, checklist, evidências)
4. **test-linux**: Testes Linux em matriz Ubuntu 20.04/22.04 (AppImage, contrato)
5. **audit-host**: Auditoria de estado do host (só em `main`/`master`)
6. **summary**: Resumo consolidado de todos os jobs

**Tempo estimado:** 5-10 minutos (depende de cache)

**Critério de sucesso:** Todos os jobs em `success`

---

### 2. Release - Build e Publicação (`.github/workflows/instalador-release.yml`)

**Trigger:**
- Push de tag no formato `v*.*.*` (ex: `v1.0.0`)
- Workflow manual via `workflow_dispatch`

**Jobs:**
1. **release-prepare**: valida manifesto/update, hashes, debug symbols, SBOM e staging de artefatos do instalador

**Tempo estimado:** 15-30 minutos (depende de builds)

**Critério de sucesso:** Release draft criada com todos os artefatos

---

## CI - Continuous Integration

### Validações Executadas

#### Static Validation
- ✅ `validate-prohibited-terms.sh`: Verifica termos proibidos (senhas, secrets)
- ✅ `lint-build.sh`: Lint de scripts e configurações de build
- ✅ `validate-script-comments.sh`: Valida comentários em scripts
- ✅ `validate-version.sh`: Valida consistência de versão

#### Security Validation
- ✅ `validate-sbom.sh`: Valida SBOM CycloneDX e SPDX
- ✅ `validate-update-manifest.sh --strict-signature`: Valida manifesto de update estrito
- ✅ `test-update-manifest.sh`: Testa cenários positivos e negativos

#### Meta Tests
- ✅ `test-changelog-format.sh`: Valida formato do CHANGELOG.md
- ✅ `test-checklist-graficos.sh`: Valida gráficos do checklist
- ✅ `test-checklist-score-evidence.sh`: Valida evidências de score

#### Linux Tests (Ubuntu 20.04 e 22.04)
- ✅ `test-appimage.sh`: Testa contrato do AppImage (5/5 validações)
- ✅ `test-installer-contract-static.sh`: Testa contrato estático

### Como Executar Localmente

Para simular o CI localmente antes de fazer push:

```bash
# Validações estáticas
bash comum/scripts/validate-prohibited-terms.sh
bash comum/scripts/lint-build.sh
bash comum/scripts/validate-version.sh

# Validações de segurança
bash comum/scripts/validate-sbom.sh
bash comum/scripts/validate-update-manifest.sh --strict-signature
bash comum/scripts/test-update-manifest.sh

# Testes meta
bash testes/meta/test-changelog-format.sh
bash testes/meta/test-checklist-graficos.sh
bash testes/meta/test-checklist-score-evidence.sh

# Testes Linux
bash testes/linux/test-appimage.sh
bash testes/windows/test-installer-contract-static.sh
```

### Artefatos Gerados

Em caso de falha, o CI faz upload de:
- `test-logs-ubuntu-20.04/`: Logs de teste Ubuntu 20.04
- `test-logs-ubuntu-22.04/`: Logs de teste Ubuntu 22.04
- `host-audit/`: Auditoria de estado do host (só em `main`)

**Retenção:** 7 dias (logs), 30 dias (auditoria)

---

## Release - Build e Publicação

### Pré-Requisitos

Antes de criar uma release, garanta que:
1. ✅ CHANGELOG.md atualizado com a versão
2. ✅ Todos os critérios GO/NO-GO atendidos (ver `RELEASE_APPROVAL.md`)
3. ✅ CI passando em `main`/`master`
4. ✅ Versão bumped nos arquivos de configuração

### Como Criar uma Release

#### Opção 1: Tag Manual (Recomendado)

```bash
# 1. Criar tag localmente
git tag -a v1.0.0 -m "Release 1.0.0"

# 2. Push da tag para GitHub
git push origin v1.0.0

# O workflow de release será disparado automaticamente
```

#### Opção 2: Workflow Manual

1. Ir em **Actions** → **Release - Build e Publicação**
2. Clicar em **Run workflow**
3. Informar versão (ex: `1.0.0`)
4. Clicar em **Run workflow**

### Validações GO/NO-GO Automáticas

O job `release-prepare` verifica:

1. ✅ Versão presente no CHANGELOG.md
2. ✅ `validate-prohibited-terms.sh` em PASS
3. ✅ `validate-sbom.sh` em PASS
4. ✅ `validate-update-manifest.sh --strict-signature` em PASS
5. ✅ `test-changelog-format.sh` em PASS
6. ⚠️ Seção [FINAL] presente em `RELEASE_APPROVAL.md`

Se qualquer validação FAIL → **NO-GO** → Release bloqueada

### Artefatos de Release

Uma release bem-sucedida gera:

#### Linux
- `Protons-{version}.AppImage` + `.sha256`
- `protons_{version}_amd64.deb` + `.sha256`

#### Windows (**MOCK por enquanto**)
- `Protons-{version}.msi` + `.sha256`
- `Protons-Setup-{version}.exe` + `.sha256`

#### Metadados
- `SHA256SUMS.txt`: Checksums consolidados
- `sbom.cdx.json`: SBOM CycloneDX
- `sbom.spdx.json`: SBOM SPDX
- `CHANGELOG.md`: Changelog completo

**Retenção:** 365 dias (metadados), 90 dias (builds)

### GitHub Release Draft

O workflow cria uma **GitHub Release em modo DRAFT**. Isso permite:
- Revisar artefatos antes de publicar
- Editar notas de release
- Adicionar binários assinados manualmente (se necessário)
- Publicar quando estiver pronto

**Para publicar:**
1. Ir em **Releases**
2. Localizar a release draft
3. Revisar artefatos e notas
4. Clicar em **Publish release**

---

## Configuração de Secrets

O workflow de release requer secrets do GitHub para funcionalidades avançadas:

### Secrets Necessários

| Secret | Descrição | Status | Usado em |
|--------|-----------|--------|----------|
| `CODE_SIGNING_CERT` | Certificado `.pfx` base64 (corporativo) | ❌ Pendente | etapa futura de assinatura em `release-prepare` |
| `CODE_SIGNING_PASSWORD` | Senha do certificado `.pfx` | ❌ Pendente | etapa futura de assinatura em `release-prepare` |
| `GITHUB_TOKEN` | Token automático do GitHub | ✅ Automático | `create-release` |

### Como Adicionar Secrets

1. Ir em **Settings** → **Secrets and variables** → **Actions**
2. Clicar em **New repository secret**
3. Adicionar `CODE_SIGNING_CERT` e `CODE_SIGNING_PASSWORD` quando disponíveis

**Nota:** Sem certificado, a assinatura de código será **SKIP** (mock).

---

## Badges de Status

Adicione badges ao README.md para mostrar status do CI:

```markdown
![CI Status](https://github.com/{owner}/{repo}/workflows/CI%20-%20Continuous%20Integration/badge.svg)
![Release Status](https://github.com/{owner}/{repo}/workflows/Release%20-%20Build%20e%20Publicação/badge.svg)
```

Substitua `{owner}` e `{repo}` pelos valores corretos.

---

## Troubleshooting

### CI Falhando em `static-validation`

**Problema:** `validate-prohibited-terms.sh` ou `lint-build.sh` falhando

**Solução:**
1. Executar localmente: `bash comum/scripts/validate-prohibited-terms.sh`
2. Corrigir termos proibidos encontrados
3. Commit e push novamente

### Release Bloqueada (NO-GO)

**Problema:** `release-prepare` retorna NO-GO

**Possíveis causas:**
1. Versão não presente no CHANGELOG.md → Adicionar seção da versão
2. SBOM inválido → Rodar `bash comum/scripts/validate-sbom.sh`
3. Update manifest falhou → Rodar `bash comum/scripts/validate-update-manifest.sh --strict-signature`
4. Changelog formato inválido → Rodar `bash testes/meta/test-changelog-format.sh`

**Solução:** Corrigir erros e recriar a tag.

### Artefatos Faltando na Release

**Problema:** GitHub Release criada mas sem artefatos

**Possíveis causas:**
1. Release do instalador falhou → Verificar logs do job `release-prepare`
2. Upload de artefatos falhou → Verificar permissões do GITHUB_TOKEN

**Solução:**
1. Verificar logs de cada job
2. Recriar a release após correções

### Assinatura de Código Não Executando

**Problema:** Artefatos não assinados

**Causa:** Certificado `.pfx` não disponível em secrets

**Solução:**
1. Aguardar certificado corporativo
2. Adicionar `CODE_SIGNING_CERT` e `CODE_SIGNING_PASSWORD` aos secrets
3. Recriar a release

---

## Roadmap de CI/CD

### Implementado ✅
- [x] Workflow CI com validações estáticas
- [x] Workflow CI com testes Linux (matriz Ubuntu)
- [x] Workflow CI com testes meta (changelog, checklist)
- [x] Workflow de release com builds Linux
- [x] Workflow de release com checksums e SBOM
- [x] GitHub Release em modo draft

### Pendente ⏸️
- [ ] Executar CI em PR real (validação de sintaxe)
- [ ] Build Windows real (MSI + Inno Setup)
- [ ] Assinatura de código real (certificado `.pfx`)
- [ ] Testes Windows em CI (requer runners Windows + VMs)
- [ ] Deploy automático (quando GO for atingido)
- [ ] Notificações Slack/Email em falhas
- [ ] Cache de dependências (otimização)
- [ ] Testes de regressão E2E em CI

### Bloqueadores 🚫
- Certificado corporativo `.pfx` não disponível
- QGA Windows bloqueado (impede testes E2E Windows em CI)
- Builds Windows aguardam implementação real

---

## Referências

- **GitHub Actions Docs:** https://docs.github.com/en/actions
- **Semantic Versioning:** https://semver.org/
- **Keep a Changelog:** https://keepachangelog.com/
- **RELEASE_APPROVAL.md:** `documentos/RELEASE_APPROVAL.md`
- **CHANGELOG.md:** `CHANGELOG.md`

---

**Última atualização:** 2026-02-15T12:00:00Z
**Responsável:** u (QA)
**Status:** Infraestrutura criada, aguardando teste real
