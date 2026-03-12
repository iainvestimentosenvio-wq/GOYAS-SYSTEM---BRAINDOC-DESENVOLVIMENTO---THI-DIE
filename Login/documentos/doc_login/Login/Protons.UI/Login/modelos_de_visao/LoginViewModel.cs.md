# LoginViewModel.cs

## Arquivo
`Login/Protons.UI/Login/modelos_de_visao/LoginViewModel.cs`

## Objetivo
Orquestrar o login do usuario e navegacao para o painel.

## Melhorias aplicadas
- Pre-validacao antes de operacao assincrona:
  - e-mail obrigatorio;
  - e-mail em formato valido;
  - senha obrigatoria.
- Evita ativar `IsBusy` e `Task.Run` quando erro e imediato.
- Erros inesperados sao registrados com `OpsLogger.WriteError("login: erro inesperado", ex)`.

## Fluxo
1. Valida campos localmente.
2. Chama `_authService.Autenticar(...)`.
3. Em sucesso, salva email (se configurado) e abre painel.
4. Em falha, mostra mensagem amigavel.

## Pontos de atencao
- `IsBusy` controla comandos e bloqueio visual de entrada.
- Navegacao para painel permanece protegida por `try/catch` com log operacional.

## Como testar
- Tentar login com campos vazios (mensagem imediata).
- Tentar login com e-mail invalido (mensagem imediata).
- Login valido deve navegar ao painel.
