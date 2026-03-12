# MainWindow.axaml

## Objetivo
Definir o conteiner principal da janela e o layout base usado pelo Login e Painel.

## Estrutura
- `ContentControl` renderiza `DisplayedViewModel` via `ViewLocator`.
- `ContentControl` usa `HorizontalContentAlignment="Stretch"` e `VerticalContentAlignment="Stretch"` para ocupar a janela toda.
- Fundo do Login usa `ImageBrush` com `login_bg.png` e opacidade leve.
- `DragRegion` e botao de fechar sao exibidos apenas no Login (sem decoracoes do sistema).

## Comportamento relevante
- O tamanho inicial do Login e definido no XAML (460x580), mas o redimensionamento final e controlado no code-behind.
- A janela usa `Background="Transparent"` no Login para permitir chrome customizado.
- `WindowStartupLocation="CenterScreen"` garante centralizacao pelo WM.

## Como testar
- Abrir o app e confirmar que o Login aparece com fundo e botao **X**.
- Trocar para o Painel e confirmar que o conteudo ocupa toda a janela (stretch).
