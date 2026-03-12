# Seletor Cliente Unificado

## Objetivo
Unificar a busca e selecao de cliente em um unico campo no topo do painel, com navegacao por grupo e selecao por clique.

## Como funciona
1. O campo do topo e unico para buscar e selecionar cliente.
2. O botao `v` abre a lista hierarquica.
3. O botao `X` limpa o contexto do cliente selecionado.
4. Ao selecionar um cliente, o painel carrega imediatamente apenas tarefas/esteiras daquele cliente.

## Regras de exibicao
1. Com campo vazio:
- grupos aparecem em ordem alfabetica;
- clique no grupo expande/recolhe clientes daquele grupo;
- clientes sem grupo aparecem abaixo, em ordem alfabetica.
2. Com busca:
- pesquisa por codigo, nome fantasia, nome (razao social) e documento exato;
- com uma palavra, se houver correspondencia de grupo, grupos sao priorizados e expandidos;
- sem correspondencia de grupo, clientes correspondentes sao exibidos.

## Regras de contexto
1. O campo continua sendo usado para filtrar o modal de cadastro.
2. Limpar selecao nao remove clientes cadastrados; apenas remove o contexto ativo no painel.
3. Selecao e feita por `clienteId`, evitando ambiguidade por texto exibido.
