# BarraLateralPainelPrincipalView.axaml

## Arquivo
`codigos/painel_principal/funcionalidades/painel/interface/partes/BarraLateralPainelPrincipalView.axaml`

## Objetivo
Definir interface da sidebar e card das ferramentas arrastaveis.

## Regra critica
O `Grid` do card de ferramenta deve manter `Background="Transparent"` para receber eventos em toda a area clicavel.

## Validacao manual
1. Abrir lista de ferramentas.
2. Tentar iniciar drag clicando no centro, canto superior esquerdo e canto inferior direito do card.
3. Drag deve iniciar nos 3 pontos.

## Ultima validacao
- Data: 2026-02-18
- Status: alinhado ao codigo atual
