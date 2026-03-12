# EsteiraTarefasControl.cs

## Arquivo
`codigos/painel_principal/funcionalidades/painel/interface/controles/EsteiraTarefasControl.cs`

## Objetivo
Controlar layout temporal de tarefas na esteira e receber drop de ferramenta.

## Responsabilidades principais
- Converter tempo para pixel na renderizacao.
- Detectar cruzamento de ponteiro temporal.
- Exibir hover visual de drop.
- Converter pixel para tempo ao soltar ferramenta.

## Regras criticas
1. `ProcessarDropFerramenta` precisa recalcular conversor com estado atual de viewport.
2. `DefinirDropHover` deve sincronizar fundo ativo/inativo sem flicker.

## Validacao manual
1. Arrastar ferramenta para esteira.
2. Confirmar destaque visual durante hover.
3. Soltar e validar evento de criacao com tempo coerente.

## Ultima validacao
- Data: 2026-02-18
- Build/Testes: ok
