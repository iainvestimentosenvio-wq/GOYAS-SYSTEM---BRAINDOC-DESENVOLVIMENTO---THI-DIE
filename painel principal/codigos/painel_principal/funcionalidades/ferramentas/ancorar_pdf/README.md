# Ferramenta: Ancorar PDF

Automação de extração de dados de PDFs por ancoragem visual de variáveis.

## Arquitetura

```
┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│  Scheduler   │────→│  Fila/Worker │────→│    Motor     │
│  (1s poll)   │     │  (Polly)     │     │  (extração)  │
└─────────────┘     └──────────────┘     └─────────────┘
                                                │
                    ┌───────────────────────────┘
                    ▼
┌─────────────┐  ┌──────────────┐  ┌─────────────────┐
│  Seletor    │  │  Extrator    │  │   Anchorador     │
│  Arquivo    │  │  Texto/OCR   │  │  Espacial Bbox   │
└─────────────┘  └──────────────┘  └─────────────────┘
```

## Fluxo de execução

1. **Scheduler** detecta tarefas vencidas (poll 1s)
2. **Fila** enfileira para workers com Polly (retry 3x, circuit breaker)
3. **Seletor** encontra PDF na pasta monitorada (Jaro-Winkler ≥ limiar)
4. **Extrator** extrai texto (PdfPig nativo + OCR Tesseract fallback)
5. **Anchorador** aplica template de âncoras com busca fuzzy (Jaccard ≥ 70%)
6. **Normalizador** valida e normaliza valores (CPF checksum, CNPJ, moeda, data)
7. **Persistência** salva resultados em `AncorarPdfExecucoes` + `AncorarPdfSaidaVariavel`

## Modos de ancoragem

| Modo | Descrição | Robustez |
|------|-----------|----------|
| `RegiaoFixa` | Coordenadas XY relativas fixas | Frágil (layout deve ser idêntico) |
| `TextoADireita` | Busca label → extrai à direita | Robusto (com fallback fuzzy) |
| `TextoAbaixo` | Busca label → extrai abaixo | Robusto (com fallback fuzzy) |

## Confiança granular

| Tipo | Checksum OK | Checksum falho | Vazio |
|------|-------------|----------------|-------|
| CPF | 1.0 | 0.4 | 0.0 |
| CNPJ | 1.0 | 0.4 | 0.0 |
| Moeda | 0.95 | 0.3 | 0.0 |
| Data | 0.95 | 0.3 | 0.0 |
| Texto | 0.8 | — | 0.0 |

## Limites

- Max âncoras por template: 10
- Max tamanho PDF: 200 MB
- Worker timeout: 30s (configurável 5-300s)
- Retry: 3 tentativas com backoff exponencial
- Cache de análise: 10 entradas com eviction
- Cache de preview: 20 entradas com eviction

## Estrutura de diretórios

- `interface/` — Views Avalonia e code-behind
- `modelos_de_visao/` — ViewModels, handlers, estado
- `dominio/` — Contratos e regras operacionais
- `documentacao/` — ADRs e índice
