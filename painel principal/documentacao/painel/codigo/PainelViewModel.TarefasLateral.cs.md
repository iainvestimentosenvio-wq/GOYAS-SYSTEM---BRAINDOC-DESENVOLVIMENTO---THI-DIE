# PainelViewModel.TarefasLateral.cs

## Arquivo
`codigos/painel_principal/funcionalidades/painel/modelos_de_visao/funcoes/PainelViewModel.TarefasLateral.cs`

## Objetivo
Gerenciar estado da lista lateral de ferramentas e estado do ghost no ViewModel.

## Responsabilidades principais
- Abrir/fechar lista lateral.
- Inicializar catalogo de ferramentas.
- Expor propriedades de posicao/visibilidade do ghost.
- Ajustar offset do ghost por tamanho.

## Regra critica
`DragGhostImagemUri` e metadado; a imagem real do ghost e definida em `PainelView.axaml.cs`.

## Validacao manual
1. Abrir/fechar lista lateral.
2. Iniciar drag e validar mudanca de `DragGhostVisivel`, `DragGhostX` e `DragGhostY`.
3. Finalizar drag e confirmar reset do estado.

## Ultima validacao
- Data: 2026-02-18
- Build/Testes: ok
