# ADR-001 — Decisão de Scheduler: Quartz vs Cronos

- **Status:** DECISÃO REGISTRADA — aguardando assinatura formal da equipe
- **Data:** 2026-02-24
- **Autores:** (preencher com nomes da equipe)
- **Revisores:** (preencher)
- **Contexto do projeto:** `ancorar_pdf` — painel PROTONS

---

## 1. Contexto e problema

A ferramenta `ancorar_pdf` precisa de um mecanismo para disparar execuções em horários
configurados (Única, Diária, Semanal, Mensal — conforme `AncorarPdfRecorrencia` em
`dominio/AncorarPdfContratos.cs:7`).

**Requisitos críticos identificados no checklist de performance:**
- Jitter de disparo: p95 <= 1.0 s, p99 <= 2.0 s.
- Misfire (offline no horário): 100% das ocorrências com alerta e opção "executar agora"
  (`AncorarPdfMisfireBacklogPolicy` já implementada em `dominio/AncorarPdfRegrasOperacionais.cs:67`).
- 0 execuções duplicadas em 100.000 execuções de teste.
- Recuperação após restart abrupto: fila operacional em <= 30 s.
- Multi-instância: 3 instâncias simultâneas sem race condition.

**Evidência de código existente:**
- `AncorarPdfMisfireBacklogPolicy.LimiarPadraoMisfire = 15s`
  (`dominio/AncorarPdfRegrasOperacionais.cs:69`).
- `AncorarPdfConfig.Recorrencia` com enum de 4 valores
  (`dominio/AncorarPdfContratos.cs:7-13`).
- Infraestrutura ainda vazia (`infraestrutura/.gitkeep`) — nenhum scheduler implementado.

---

## 2. Opções analisadas

### Opção A — Quartz.NET (persistente, SQLite/PostgreSQL)

**O que é:** scheduler enterprise de fato do ecossistema .NET. Persiste jobs em banco de
dados relacional, suporta clustering nativo, misfire policies configuráveis.

**Vantagens:**
- Persistência nativa: jobs sobrevivem a restart abrupto sem código extra.
- Clustering via row-level lock no banco — elimina race condition multi-instância.
- Misfire policy configurável por job type (IgnoreMisfire, FireAndProceed, DoNothing).
- `CronExpression` nativa; suporte a timezone (necessário: `AncorarPdfConfig.TimezoneLocal`).
- Ecossistema maduro: telemetria, listeners, job listeners para audit trail.

**Desvantagens:**
- Dependência externa (NuGet: `Quartz`, `Quartz.Extensions.Hosting`).
- Necessita migração de schema para tabelas de controle (QRTZ_*).
- Custo de startup ligeiramente maior em máquina fraca.

**Integração com código existente:**
- `AncorarPdfConfig.ProgramadoPorUserId/Nome/EmUtc` → mapeiam para `JobDataMap` do Quartz.
- `AncorarPdfMisfireBacklogPolicy` → complementa a policy nativa do Quartz para UI/alerta.
- `AncorarPdfExecucaoCiclo.ExecutadaComAtraso` → setado via misfire listener.

### Opção B — Cronos + Scheduler próprio (in-memory, sem persistência)

**O que é:** biblioteca `Cronos` para parsing de expressões cron, combinada com timer
interno (`PeriodicTimer` ou `Task.Delay`).

**Vantagens:**
- Zero dependência de banco para agendamento.
- Simples de integrar em projetos sem infraestrutura de banco dedicada.
- Startup instantâneo.

**Desvantagens:**
- **Sem persistência**: perda de todos os agendamentos em restart.
- **Sem clustering nativo**: exige implementação própria de lock distribuído (banco, Redis, etc.)
  para evitar dupla execução multi-instância — risco alto de race condition.
- Misfire: requer implementação manual completa.
- Não atende o requisito de "recuperação em <= 30 s sem perda de tarefas".

### Opção C — Quartz.NET com SQLite local (modo Lite/offline)

**Variante da Opção A** para perfil máquina fraca (sem PostgreSQL):
- Quartz persiste em SQLite (mesmo banco já usado pelo projeto — evidência em
  `../Login/Protons.Infrastructure/Login/banco_de_dados/SqliteDb.cs`).
- Sem clustering (instância única), com lock via SQLite WAL.
- Misfire policy: `FireAndProceedMisfireInstruction` para recorrências curtas.

---

## 3. Decisão recomendada

**Perfil Lite (máquina fraca, instância única, offline):**
→ **Quartz.NET com SQLite** (Opção C).

**Perfil Enterprise Plus (servidor, multi-instância):**
→ **Quartz.NET com PostgreSQL clustering** (Opção A).

**Cronos sozinho:** descartado para produção. Não atende requisitos de persistência
e clustering sem retrabalho significativo.

---

## 4. Riscos e mitigações

| Risco | Severidade | Mitigação |
|-------|-----------|-----------|
| Migration das tabelas QRTZ_ quebrar schema existente | ALTO | Separar schema Quartz em schema `qrtz` dedicado no PostgreSQL; em SQLite, usar banco separado `ancorar_scheduler.db` |
| Jitter de disparo > 1s em máquina fraca sob carga | MÉDIO | Medir em spike (ver RELATORIO_SPIKES); ajustar `MisfireThreshold` do Quartz se necessário (padrão 60 s; reduzir para 15 s alinhado ao limiar atual do código) |
| Dupla execução em failover de instância | ALTO | `DisallowConcurrentExecution` + `@PersistJobDataAfterExecution` no job handler; testar com 3 instâncias simultâneas obrigatório |
| Crescimento de tabelas QRTZ_ em operação longa | BAIXO | Habilitar `AutoCleanupAfterFinishTriggers`; agendar rotina de purge |
| Timezone DST incorreto | MÉDIO | Usar `TimeZoneInfo.FindSystemTimeZoneById(AncorarPdfConfig.TimezoneLocal)` com fallback para UTC; testar DST boundary explicitamente |

---

## 5. Impacto nos arquivos do projeto

- **Criar:** `infraestrutura/AncorarPdfSchedulerService.cs` — wrapper `IHostedService` Quartz.
- **Criar:** `infraestrutura/AncorarPdfJobHandler.cs` — `IJob` Quartz que despacha para pipeline.
- **Criar:** migração de schema Quartz (SQL ou via `SchedulerBuilder.UseJobFactory`).
- **Modificar:** DI registration (arquivo de bootstrap do painel — a identificar no projeto real).

---

## 6. Assinatura de aprovação

| Papel | Nome | Data | Status |
|-------|------|------|--------|
| Arquiteto responsável | _(preencher)_ | — | **PENDENTE** |
| Dev sênior backend | _(preencher)_ | — | **PENDENTE** |
| Revisor de qualidade | _(preencher)_ | — | **PENDENTE** |

> **BLOQUEIO:** Item 7 do Gate de Início ("Decisão de scheduler assinada") só pode ser
> marcado como concluído após preenchimento e aprovação desta tabela pela equipe.
