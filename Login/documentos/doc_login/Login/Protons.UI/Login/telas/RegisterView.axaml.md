# RegisterView.axaml

## Arquivo
`Login/Protons.UI/Login/telas/RegisterView.axaml`

## Objetivo
Tela de cadastro de usuarios do sistema.

## Melhorias aplicadas
- Botao `Salvar` com `Classes="Primary"`.
- Botao `Voltar` com `Classes="Ghost"`.
- Mantida consistencia de `MaxLength` e nomes de automacao nos campos.

## Fluxo visual
- Formulario segue padrao visual do login.
- Inputs desabilitam em `IsBusy`.
- Mostra progresso durante envio.

## Como testar
- Abrir tela de cadastro e validar estilo visual dos botoes.
- Confirmar envio e retorno de mensagem no fluxo normal.
