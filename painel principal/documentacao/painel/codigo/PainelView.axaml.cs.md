# PainelView.axaml.cs

## Arquivo
`codigos/painel_principal/funcionalidades/painel/interface/PainelView.axaml.cs`

## Objetivo
Coordenar ciclo de drag-and-drop entre sidebar e esteiras.

## Responsabilidades principais
- Assinar eventos de drag da sidebar.
- Iniciar/atualizar/encerrar ghost.
- Detectar esteira em hover e processar drop.
- Cancelar sessao em cenarios de escape, modal e perda de foco.

## Regras criticas
1. `CarregarGhostImagem` deve permanecer em code-behind.
2. Em `OnFerramentaDragEnded`, capturar referencias locais antes de chamar `ProcessarDropFerramenta`.
3. `ConverterTopLevelParaVisual` deve usar fallback com `pontoTopLevel`.

## Validacao manual
1. Arrastar e soltar ferramenta dentro da esteira.
2. Arrastar e soltar fora da esteira.
3. Durante drag, pressionar `Esc` e confirmar cancelamento.

## Ultima validacao
- Data: 2026-02-18
- Build/Testes: ok
