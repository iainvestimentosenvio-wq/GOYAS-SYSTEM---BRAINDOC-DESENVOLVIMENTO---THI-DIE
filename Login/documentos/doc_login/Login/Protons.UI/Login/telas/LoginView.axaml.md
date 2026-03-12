# LoginView.axaml

## Arquivo
`Login/Protons.UI/Login/telas/LoginView.axaml`

## Objetivo
Tela de autenticacao com foco em usabilidade e acessibilidade.

## Melhorias aplicadas
- Limites de entrada:
  - E-mail `MaxLength=254`
  - Senha `MaxLength=256`
- Acessibilidade (`AutomationProperties.Name`):
  - E-mail
  - Senha
  - Mostrar/ocultar senha
  - Lembrar meu e-mail
  - Entrar
  - Criar conta

## Fluxo visual
- Campos e botoes ficam desabilitados durante `IsBusy`.
- Exibe barra de progresso ao autenticar.

## Como testar
- Colar texto muito longo no e-mail/senha e validar corte pelo limite.
- Navegar com leitor de tela e conferir nomes de automacao.
