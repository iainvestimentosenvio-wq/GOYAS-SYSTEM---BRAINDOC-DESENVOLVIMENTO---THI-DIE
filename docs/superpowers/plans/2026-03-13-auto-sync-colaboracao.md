# Auto-Sync Colaborativo Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Estabilizar o auto-sync local e documentar um fluxo colaborativo com branch pessoal por maquina.

**Architecture:** O script Linux sera refatorado para separar funcoes utilitarias da execucao principal, ignorar caminhos locais e operar apenas sobre a branch atual da maquina. A configuracao local sera complementada por `.gitignore` e um guia operacional para o segundo desenvolvedor.

**Tech Stack:** Bash, Git, PowerShell, Markdown

---

## Chunk 1: Teste e refatoracao do auto-sync Linux

### Task 1: Criar teste de regressao do filtro de caminhos

**Files:**
- Create: `.sync/test-auto-sync-linux.sh`
- Modify: `.sync/auto-sync-linux.sh`

- [ ] **Step 1: Write the failing test**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Write minimal implementation**
- [ ] **Step 4: Run test to verify it passes**

### Task 2: Corrigir fluxo Git e loop de log

**Files:**
- Modify: `.sync/auto-sync-linux.sh`
- Modify: `.gitignore`

- [ ] **Step 1: Ignorar `.sync` e artefatos locais**
- [ ] **Step 2: Evitar `pull --rebase` em arvore alterada**
- [ ] **Step 3: Garantir configuracao de upstream quando necessario**
- [ ] **Step 4: Verificar status Git apos ajuste**

## Chunk 2: Documentacao operacional

### Task 3: Documentar fluxo de colaboracao

**Files:**
- Create: `docs/auto-sync-colaboracao.md`

- [ ] **Step 1: Explicar branches pessoais e branch de integracao**
- [ ] **Step 2: Documentar configuracao desta maquina**
- [ ] **Step 3: Incluir passos para a maquina do colega**
- [ ] **Step 4: Incluir prompt para o Codex do colega**

## Chunk 3: Verificacao final

### Task 4: Executar verificacoes

**Files:**
- None

- [ ] **Step 1: Rodar teste do script Linux**
- [ ] **Step 2: Rodar verificacoes Git**
- [ ] **Step 3: Revisar diff final**
