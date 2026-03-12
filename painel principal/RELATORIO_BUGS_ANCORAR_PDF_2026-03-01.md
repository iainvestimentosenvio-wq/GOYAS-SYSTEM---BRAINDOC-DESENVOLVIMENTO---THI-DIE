# Relatório de Bugs, Falhas e Melhorias — Ancorar PDF

**Data:** 2026-03-01  
**Escopo:** Sistema Ancorar PDF (UI modal, handlers, preview, save, lifecycle)

---

## 1. Bugs identificados

### 1.1 Atualização de UI em thread de background (prioridade alta)

**Local:** `AncorarPdfPdfRenderHandler.CarregarTextoPdfAsync`, `AncorarPdfPreviewState.RenderizarPdfAsync`

**Problema:** Propriedades do ViewModel são atualizadas em thread pool após `ConfigureAwait(false)` ou dentro de callbacks do renderer, sem marshalling para a UI thread.

**Evidência:**
- `AncorarPdfPdfRenderHandler.cs:51` — `await _extratorTexto.ExtrairAsync(...).ConfigureAwait(false)` continua em thread pool; em seguida `_context.SetPdfPaginasCache(paginas)` e `ExtracaoEmAndamento = false` são chamados fora da UI thread.
- `AncorarPdfPreviewState.RenderizarPdfAsync` chama `aplicarBitmap`, `setRenderizando`, `setMensagem` — esses callbacks atualizam a VM e podem ser executados em background.

**Risco:** Em Avalonia, atualizar bindings fora da UI thread pode causar exceções de cross-thread ou comportamento imprevisível.

**Correção sugerida:** Usar `Dispatcher.UIThread.Post` ou `InvokeAsync` para aplicar mudanças na VM, como em `PainelViewModel.Notificacoes.cs` e `PainelReguaTempoView.axaml.cs`.

---

### 1.2 `File.Exists` pode lançar exceção

**Local:** `AncorarPdfPreviewState.cs:94`, `AncorarPdfPdfRenderHandler.cs:46`

**Problema:** `File.Exists(pdfPath)` pode lançar em caminhos inválidos (caracteres especiais, muito longos, etc.).

**Evidência:**
```csharp
if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
```

**Correção sugerida:** Envolver em `try/catch` ou usar `Path.Exists` (se disponível) ou tratar `ArgumentException`/`PathTooLongException`.

---

### 1.3 Swallow de exceções sem log

**Local:** `AncorarPdfPdfRenderHandler.CarregarTextoPdfAsync` (linha 54), `AncorarPdfPreviewState.RenderizarPdfAsync` (linha 125)

**Problema:** Blocos `catch` sem tipo nem log engolem todas as exceções, dificultando diagnóstico.

**Evidência:**
```csharp
catch
{
    _context.SetPdfPaginasCache(null);
}
```

**Correção sugerida:** Registrar pelo menos `Exception.Message` em log ou evento de auditoria e manter o tratamento defensivo.

---

### 1.4 Inconsistência de timezone em `AncorarPdfAgendamentoState`

**Local:** `AncorarPdfAgendamentoState.cs`

**Problema:** `NormalizarTimezoneIdUi` lança `InvalidOperationException` para timezone inválida; `ConverterUtcParaTimezone` captura e retorna `TimeZoneInfo.Local` sem lançar.

**Risco:** Ao abrir tarefa com timezone inválida no banco, `ConverterUtcParaTimezone` usa Local silenciosamente; em validação, `NormalizarTimezoneIdUi` lança. Comportamento inconsistente.

---

## 2. Falhas/Fragilidades de projeto

### 2.1 Timeout de save não cancela operação real

**Local:** `AncorarPdfSaveHandler.SalvarAsync` (linha 60)

**Problema:** `Task.Run` usa `cts.Token`, mas `_service.CriarOuAtualizar` não recebe o token. O cancelamento apenas interrompe o `await`; o `CriarOuAtualizar` pode continuar em background.

**Risco:** Em timeout, a UI mostra erro, mas o backend pode persistir a configuração, gerando duplicidade ou estado inconsistente.

---

### 2.2 `Dispose` do bitmap durante render em UI

**Local:** `AncorarPdfConfiguracaoViewModel.AplicarBitmapRenderizado` (linha 623–629)

**Problema:** `anterior?.Dispose()` é chamado imediatamente após a troca do bitmap. Se o `Image` ainda estiver renderizando o bitmap antigo, pode ocorrer `ObjectDisposedException`.

**Correção sugerida:** Usar `Dispatcher.UIThread.Post` para atrasar o dispose ou usar `SynchronizationContext` para garantir que o dispose ocorra após o próximo frame.

---

### 2.3 Validação de path antes do preview

**Local:** Fluxo de preview e extração de texto

**Problema:** O preview renderiza e extrai texto sem validar antes contra `IAncorarPdfPathPolicy`. A path policy é usada no save; o preview pode usar caminhos que seriam rejeitados no save.

**Correção sugerida:** Validar path policy antes de iniciar render/extracao ou exibir aviso claro quando o PDF não for permitido no save.

---

### 2.4 Semáforo com `WaitAsync(0)`

**Local:** `AncorarPdfSaveOrchestrator.TentarIniciarAsync` (linha 19)

**Problema:** `_saveSemaphore.WaitAsync(0)` é um try-acquire sem espera. Em .NET 6+ é suportado; em versões antigas pode haver incompatibilidade.

**Correção sugerida:** Usar `WaitAsync(TimeSpan.Zero)` se o projeto exigir compatibilidade com versões antigas.

---

## 3. Melhorias recomendadas

### 3.1 Log de auditoria

- Usar `storage/logs/log_ops.jsonl` como fonte única de operações (JSONL).
- Garantir que eventos de falha e timeout no save sejam registrados.

**Evidência:** `_registrarEvento` já existe; o destino precisa ser o JSONL.

---

### 3.2 Tratamento de PDF modelo ausente

**Local:** `AncorarPdfLifecycleHandler.AbrirExistenteAsync`

**Problema:** Se `config.PdfModeloPath` apontar para arquivo inexistente, o preview fica vazio sem mensagem.

**Correção sugerida:** Após `IniciarRenderEextracaoPdf`, verificar se o arquivo existe e, se não, definir `Mensagem = "PDF modelo não encontrado no caminho configurado."`.

---

### 3.3 Testes unitários para handlers

**Problema:** Falta cobertura para `AncorarPdfPdfRenderHandler`, `AncorarPdfSaveHandler`, `AncorarPdfPreviewState` em cenários de erro e edge cases.

**Correção sugerida:** Adicionar testes para:
- `CarregarTextoPdfAsync` com path inválido, exceção no extrator.
- `RenderizarPdfAsync` com path inexistente.
- `SalvarAsync` com timeout e cancelamento.

---

### 3.4 Documentação de threading

**Problema:** Não há documentação clara sobre quais métodos devem ser chamados apenas na UI thread.

**Correção sugerida:** Documentar `IAncorarPdfLifecycleContext`, `IAncorarPdfPdfRenderContext` e handlers indicando requisitos de thread.

---

### 3.5 Refatoração de `AplicarBitmapRenderizado`

**Problema:** Dispor bitmap imediatamente pode causar crash.

**Correção sugerida:** Usar `Dispatcher.UIThread.Post` para atrasar o dispose ou usar `IDisposable` com `Deferral`/`DisposeLater`.

---

## 4. Resumo executivo

| Categoria | Quantidade | Prioridade |
|-----------|------------|------------|
| Bugs | 4 | 1 alta, 3 média |
| Fragilidades | 4 | 2 média, 2 baixa |
| Melhorias | 5 | 5 recomendadas |

**Prioridade imediata:** Corrigir atualizações de UI em thread de background (1.1) e tratamento de exceções sem log (1.3).

---

## 5. Arquivos principais analisados

- `AncorarPdfConfiguracaoViewModel.cs`
- `AncorarPdfPdfRenderHandler.cs`
- `AncorarPdfPreviewState.cs`
- `AncorarPdfSaveHandler.cs`
- `AncorarPdfSaveOrchestrator.cs`
- `AncorarPdfLifecycleHandler.cs`
- `AncorarPdfSmartClickHandler.cs`
- `AncorarPdfPreviewInteractionHandler.cs`
- `AncorarPdfAgendamentoState.cs`
