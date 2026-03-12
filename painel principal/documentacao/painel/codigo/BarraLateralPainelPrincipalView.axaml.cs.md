# BarraLateralPainelPrincipalView.axaml.cs

## Arquivo
`codigos/painel_principal/funcionalidades/painel/interface/partes/BarraLateralPainelPrincipalView.axaml.cs`

## Objetivo
Implementar camada de origem do drag: captura de ponteiro, deteccao de deslocamento e disparo de eventos.

## Regras criticas
1. Distancia minima para iniciar drag: `6px`.
2. Preservar `VisualOrigem` no evento `FerramentaDragStarted` para screenshot do ghost.
3. Em perda de captura, cancelar drag com seguranca.

## Validacao manual
1. Clique simples no card: nao deve virar drag acidental.
2. Arraste curto (< 6px): nao inicia drag.
3. Arraste > 6px: inicia drag e envia eventos.

## Ultima validacao
- Data: 2026-02-18
- Build/Testes: ok
