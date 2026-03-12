# 10 - Coleta e Importacao de Assets (Logo e Background)

## A) Quando executar este passo
- Executar quando a UI de login estiver funcional e houver assets reais.
- Se fizer parte de validacao de release, registrar em `RESULTADOS_EVOLUCAO_TESTES.md`.

## B) Arquivos necessarios (inputs)
- Logo: PNG (preferencial), idealmente com transparencia.
- Background: JPG ou PNG de boa qualidade.

## C) Destino no repositorio
- `Protons.UI/Assets/Brand/logo_protons.png`
- `Protons.UI/Assets/Brand/login_bg.png`

## D) Regras de qualidade/performance
- Logo: manter arquivo leve.
- Background: evitar imagem excessivamente pesada.
- Tamanho recomendado de tela: 1600x900 ou 1920x1080.

## E) Processo guiado
1. Confirmar caminhos reais dos arquivos.
2. Copiar para `Protons.UI/Assets/Brand/` com nomes padrao.
3. Validar existencia e tamanho no terminal.

Comandos:
```bash
mkdir -p Protons.UI/Assets/Brand
cp "/caminho/logo.png" "Protons.UI/Assets/Brand/logo_protons.png"
cp "/caminho/bg.png" "Protons.UI/Assets/Brand/login_bg.png"
ls -la Protons.UI/Assets/Brand
```

## F) Depois de importar
No projeto Avalonia, manter Build Action dos assets como `AvaloniaResource`.
