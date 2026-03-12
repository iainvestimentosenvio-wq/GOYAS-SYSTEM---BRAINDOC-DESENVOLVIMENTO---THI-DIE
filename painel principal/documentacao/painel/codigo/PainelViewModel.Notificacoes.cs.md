# PainelViewModel.Notificacoes.cs

## Arquivo
`codigos/painel_principal/funcionalidades/painel/modelos_de_visao/funcoes/PainelViewModel.Notificacoes.cs`

## Objetivo
Gerenciar o fluxo de pendencias de cadastro de usuarios no painel.

## Acoes disponiveis
- `LiberarAcesso`: aprova como usuario comum.
- `LiberarComoAdministrador`: aprova e promove para admin.
- `NaoConheco`: rejeita solicitacao.

## Regras de seguranca
- O fluxo de notificacao nunca exibe senha.
- As decisoes exigem contexto admin (`PodeDecidirPendencia`).
- Todas as operacoes registram evento, metrica e erro operacional quando necessario.

## Como testar
1. Abrir modal de notificacoes com pendencia.
2. Executar as tres acoes (aprovar, promover admin, rejeitar).
3. Confirmar remocao da pendencia e mensagem de retorno.
