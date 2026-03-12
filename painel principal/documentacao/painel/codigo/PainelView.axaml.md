# PainelView.axaml

## Arquivo
`codigos/painel_principal/funcionalidades/painel/interface/PainelView.axaml`

## Objetivo
Compor layout raiz do painel, incluindo sidebar, topbar, conteudo, modal de notificacoes e overlay de ghost de drag.

## Pontos criticos
1. `BarraLateralPainel` e o componente origem do drag.
2. Overlay de ghost usa `Canvas` fullscreen com `IsHitTestVisible="False"`.
3. `GhostImagem` nao deve receber `Source` por binding string; a imagem e definida no code-behind.
4. Modal de notificacoes permite aprovar/rejeitar solicitacoes e tambem aprovar como administrador.
5. Fluxo de notificacoes reforca regra operacional: senha nunca e exibida.

## Validacao manual
1. Abrir painel.
2. Iniciar drag de ferramenta.
3. Confirmar ghost visivel e sem bloquear cliques.
4. Abrir notificacoes e validar botoes de aprovacao e promocao para admin.

## Ultima validacao
- Data: 2026-02-18
- Status: alinhado ao codigo atual
