# Painel - Drag and Drop de Ferramentas

## Escopo
Fluxo de arrastar ferramenta da sidebar e soltar na esteira no painel.

## Camadas
1. Origem: `BarraLateralPainelPrincipalView`.
2. Coordenador: `PainelView`.
3. Destino: `EsteiraTarefasControl`.

## Fluxo resumido
1. Sidebar detecta `PointerPressed` e captura ponteiro.
2. Ao passar distancia minima (6px), dispara `FerramentaDragStarted`.
3. `PainelView` inicia ghost e atualiza posicao/hover durante movimento.
4. No fim do drag, `PainelView` identifica esteira alvo e chama `ProcessarDropFerramenta`.
5. Esteira converte pixel para tempo e dispara `FerramentaSoltaNaEsteira`.

## Regras criticas
1. O card da ferramenta no AXAML deve manter `Background="Transparent"`.
2. A imagem do ghost deve ser carregada em code-behind (nao via binding string->IImage).
3. Antes de processar drop, capturar referencias locais para evitar reentrada com null.
4. Fallback de coordenada deve retornar `pontoTopLevel` (nao `Point(0,0)`).

## Como validar rapidamente
1. Abrir painel.
2. Abrir lista de ferramentas.
3. Arrastar ferramenta pelo centro e pelas bordas do card.
4. Soltar na esteira.
5. Confirmar hover visual e criacao da tarefa na faixa correta.

## Ultima validacao
- Data: 2026-02-18
- Build: ok
- Testes: ok
