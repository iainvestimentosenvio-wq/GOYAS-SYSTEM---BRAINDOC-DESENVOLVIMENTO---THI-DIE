---
name: Melhorias Arquitetura Performance Robustez
overview: "Plano aprovado com ajustes obrigatórios. Ordem: Item 3 (dispatcher) -> Item 2 (N+1 KPI+histórico) -> stale guards -> Item 1 (App swap) -> Item 4 (cache) -> Item 5 (attach/detach simétrico) -> Item 6 opcional. Baseline formal obrigatório."
todos: []
isProject: false
---

# Plano de Melhorias: Arquitetura, Performance e Robustez (Aprovado com Ajustes)

## Baseline formal (obrigatório preservar e provar melhoria)


| Métrica                  | Valor atual |
| ------------------------ | ----------- |
| Startup probe warm médio | 440 ms      |
| PainelView cold          | 1079 ms     |
| first_render cold        | 1836 ms     |
| first_render warm        | 448 ms      |
| clientes_busca           | 31 ms       |


A implementação deve preservar esse baseline e demonstrar melhoria (ou pelo menos não regredir).

---

## Ordem de implementação (obrigatória)

1. **Item 3** — Helper seguro Dispatcher + DispatcherTimer.RunOnce
2. **Item 2** — N+1 ampliado (KPI + histórico global)
3. **Guardas de carga stale** — PainelViewModel.TarefasRealizadas
4. **Item 1** — Swap outside lock (só App.axaml.cs)
5. **Item 4** — Invalidação de cache (_mapaClientesPorId)
6. **Item 5** — Attach/detach simétrico (se fizer)
7. **Item 6** — Login async (se ainda fizer sentido)

---

## Item 3 (prioridade 1): Helper seguro para Dispatcher.Post

**Problema:** `Dispatcher.Post` recebe `Action`. Com lambda `async () => { await ... }` vira fronteira **async void** — exceções não propagam.

**Arquivos:**

- [PainelReguaTempoView.axaml.cs](painel principal/codigos/painel_principal/funcionalidades/painel/interface/funcionalidades/tarefas_realizadas/PainelReguaTempoView.axaml.cs) — linha 763
- [MainWindow.axaml.cs](Login/Protons.UI/Login/telas/MainWindow.axaml.cs) — linha 247 e outras

**Solução:**

- Criar helper centralizado (ex.: `Protons.UI.Common`):

```csharp
  public static void PostAsyncSafe(Func<Task> action, string contextoLog, DispatcherPriority priority = DispatcherPriority.Normal)
  {
      Dispatcher.UIThread.Post(async () =>
      {
          try { await action().ConfigureAwait(true); }
          catch (Exception ex) { OpsLogger.WriteError(contextoLog, ex); }
      }, priority);
  }
  

```

- **Casos que só fazem atraso e reposicionamento:** substituir `Post(async () => { await Task.Delay(...); ... })` por `DispatcherTimer.RunOnce`.
- Substituir usos críticos de `Post(async ...)` pelo helper.

---

## Item 2 (prioridade 2): N+1 ampliado — KPI + histórico global

**Maior prioridade de performance.** O plano deve cobrir **ambos** os fluxos. Resolver só um resolve metade do problema.

1. **KPI / tarefas visíveis no escopo:** [PainelViewModel.TarefasRealizadas.cs](painel principal/codigos/painel_principal/funcionalidades/painel/modelos_de_visao/funcoes/PainelViewModel.TarefasRealizadas.cs) — linha 489 (`BuscarTarefasVisiveisNoEscopoAsync`)
2. **Histórico global:** [TarefaService.cs](Login/Protons.Core/Tarefas/servicos/TarefaService.cs) — linha 82 (`BuscarHistoricoGlobal`)

**Solução:**

- `ITarefaRepository`: método `BuscarPorClienteIds(IReadOnlyList<int> clienteIds, ...)` com `ClienteId IN (...)` e filtros de visibilidade no banco.
- `ITarefaService` / `TarefaService`: método que recebe lista de clienteIds e delega ao repositório.
- Substituir ambos os loops por uma única chamada.
- **Índices:** Não mexer antes de medir EXPLAIN (já existem em PostgresDb/SqliteDb).
- **Métricas:** OpsLogger para duração de KPI global, histórico global; validar contra baseline.

---

## Guardas de carga stale (prioridade 3)

**Problema:** [PainelViewModel.TarefasRealizadas.cs](painel principal/codigos/painel_principal/funcionalidades/painel/modelos_de_visao/funcoes/PainelViewModel.TarefasRealizadas.cs) linha 174 — troca rápida de cliente/escopo pode fazer carga antiga sobrescrever estado novo.

**Solução:**

- `CancellationTokenSource` por carga: criar CTS no início de `CarregarTarefasClienteAsync`, cancelar o anterior, passar token para `Task.Run` e operações assíncronas.
- Antes de aplicar resultado, verificar se token foi cancelado; se sim, não atualizar estado.
- Alternativa: load version (contador) — incrementar ao iniciar, comparar antes de aplicar.

---

## Item 1 (prioridade 4): Swap outside lock — só App.axaml.cs

**Problema:** Em [App.axaml.cs](Login/Protons.UI/App.axaml.cs) linhas 823 e 846, stop/dispose ocorre **dentro** do lock. Aumenta contenção e fragilidade de shutdown.

**Solução (obrigatória):** Trocar referência sob lock; fazer Stop/Dispose da instância **antiga** fora do lock. A solução principal é o swap outside lock, **não** `Task.Run(...).GetAwaiter().GetResult()`.

```csharp
IAncorarPdfFilaExecucaoService? antiga = null;
lock (FilaServiceSync)
{
    antiga = _filaService;
    _filaService = service;
}
antiga?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
antiga?.Dispose();
```

**AncorarPdfJobHandler.Processar (linha 46):** Fora do escopo deste plano. Não é prioridade alta. O caminho real usa `ProcessarAsync` em AncorarPdfSchedulerRuntime linha 230.

---

## Item 4 (prioridade 5): Invalidação de cache _mapaClientesPorId

**Primeira correção (obrigatória):** Em [LimparCacheBuscaClientes](painel principal/codigos/painel_principal/funcionalidades/painel/cadastro_clientes/modelos_de_visao/PainelViewModel.CadastroClientes.cs) linha 1559, adicionar `_mapaClientesPorId.Clear()` junto com os outros caches.

**Segunda etapa (depois):** Só então considerar limite de tamanho (LRU/FIFO) se necessário.

---

## Item 5 (prioridade 6): Attach/detach simétrico — obrigatório se incluir

**Proibido:** Remover handlers em `OnDetachedFromVisualTree` ([PainelView.axaml.cs](painel principal/codigos/painel_principal/funcionalidades/painel/interface/PainelView.axaml.cs) linha 170) sem religar em `OnAttachedToVisualTree` — quebra reutilização da view.

**Item 5 só pode entrar se for reescrito como ciclo simétrico attach/detach:**

- `OnDetachedFromVisualTree`: remover `DataContextChanged`, `SizeChanged`, eventos da `BarraLateralPainel`
- `OnAttachedToVisualTree`: religar os mesmos handlers

---

## Item 6 (prioridade 7): Login async — opcional de verdade

**Menor ganho do pacote.** [LoginViewModel.cs](Login/Protons.UI/Login/modelos_de_visao/LoginViewModel.cs) linhas 83, 135 — I/O síncrono existe, mas pelos números atuais não é onde o sistema perde mais tempo. Avaliar apenas se ainda fizer sentido após as demais mudanças.

---

## O que não alterar

- Visual e UX
- Funcionalidades
- APIs públicas (novas sobrecargas onde necessário)
- Testes existentes devem continuar passando

---

## Trilha separada (fora deste plano)

- Datas TEXT no PostgreSQL (PostgresDb.cs linha 269) — débito estrutural; não misturar com este plano.

