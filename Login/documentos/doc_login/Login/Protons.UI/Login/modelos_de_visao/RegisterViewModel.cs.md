# RegisterViewModel.cs

## Arquivo
`Login/Protons.UI/Login/modelos_de_visao/RegisterViewModel.cs`

## Objetivo
Gerenciar cadastro de novo usuario no login.

## Melhorias aplicadas
- Erros inesperados no cadastro agora vao para log operacional:
  - `OpsLogger.WriteError("registro: erro inesperado", ex)`.

## Fluxo
1. Coleta dados do formulario.
2. Chama `_authService.CriarConta(...)`.
3. Exibe mensagem de sucesso/pendencia conforme retorno.
4. Limpa senha em sucesso.

## Pontos de atencao
- Primeiro usuario cadastrado pode virar admin inicial.
- Demais usuarios entram em pendencia para aprovacao.

## Como testar
- Cadastro valido deve retornar mensagem adequada.
- Erro inesperado deve registrar log sem quebrar UI.
