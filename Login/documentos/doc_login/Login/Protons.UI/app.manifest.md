# app.manifest

## Objetivo
Definir configuracoes de compatibilidade e DPI no Windows para evitar distorcoes de escala e deslocamento de janela.

## O que foi configurado
- **DPI awareness**: `PerMonitorV2` (melhor comportamento em escalas 125%/150%).
- **Compatibilidade**: Windows 10 testado.

## Impacto
- Evita offsets visuais e redimensionamento incorreto em setups com escala fracionaria.
- Nao altera comportamento em Linux/macOS (manifesto e usado apenas no Windows).

## Como validar
- No Windows, configurar escala 125% e 150%.
- Abrir Login e Painel e verificar centralizacao e tamanho corretos.
- Comparar com comportamento antes do manifesto (se houver).
