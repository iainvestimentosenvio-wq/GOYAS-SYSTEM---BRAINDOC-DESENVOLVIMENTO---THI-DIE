# ViewLocator.cs

## Objetivo
Resolver automaticamente a View correspondente a um ViewModel.

## Como funciona
Substitui o sufixo `ViewModel` por `View`, ajusta `ViewModels` -> `Views` e instancia via reflection.
Se a criação falhar, retorna um controle de fallback com mensagem de erro.

## Logs de diagnóstico (tempo de criação)
Quando `PROTONS_WINDOW_DIAG=1`, o locator registra:
- `viewlocator_start: type=...`
- `viewlocator_elapsed: type=... ms=...`

Isso permite medir se a criação da View está bloqueando a UI.

## Entradas e saídas
- **Entrada**: instância de ViewModel.
- **Saída**: controle Avalonia correspondente.

## Dependências
- Avalonia `IDataTemplate`.

## Como testar
- Navegar entre telas e validar que a view correta aparece.
