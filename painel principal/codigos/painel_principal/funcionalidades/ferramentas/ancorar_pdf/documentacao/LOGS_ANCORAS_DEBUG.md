# Logs de Âncoras — Análise e Debug

## Objetivo

Os eventos de âncoras do Ancorar PDF são registrados em `log_ops.jsonl` com dados suficientes para analisar testes **sem print**: o que o usuário ancorou, coordenadas, tipo e chave de cada variável.

Isso permite:
- Verificar se o sistema está ancorando corretamente todas as variáveis
- Corrigir bugs com base em evidência de runtime
- Documentar o fluxo de interação do usuário

## Localização do log

- **Linux (padrão):** `~/.local/share/Protons/log_ops.jsonl`
- **Bypass (painel direto):** `~/.local/share/protons-dev/Protons/log_ops.jsonl`
- **Windows:** `%APPDATA%/Protons/log_ops.jsonl`

## Eventos de âncoras

| ID do evento | Quando ocorre | Dados principais |
|--------------|---------------|-------------------|
| `ancorar_pdf_c2_anchor_add` | Usuário adiciona âncora (botão + cor) | nome, chave, pagina, xRel, yRel, wRel, hRel, modo, tipo |
| `ancorar_pdf_c2_preview_render` | Usuário desenha retângulo no PDF | nome, chave, pagina, xRel, yRel, wRel, hRel, modo, tipo |
| `ancorar_pdf_c2_anchor_select` | Usuário seleciona âncora na lista | nome, chave, pagina, xRel, yRel, wRel, hRel, modo, tipo |
| `ancorar_pdf_c2_anchor_remove` | Usuário remove âncora | nome, chave, pagina, xRel, yRel, wRel, hRel, modo, tipo |
| `ancorar_pdf_c2_anchor_replace` | Usuário substitui âncora (cor já usada) | antiga_nome, antiga_chave, nova_nome, nova_chave |
| `ancorar_pdf_c12_smart_click_confirmado` | Usuário confirma detecção Smart Click | nome, chave, tipo, pagina, xRel, yRel |
| `ancorar_pdf_c2_save_ok` | Configuração salva com sucesso | tarefa_id, versao, anchor_count, anchor_chaves |

## Formato dos campos

- **nome:** Nome exibido da variável (ex: `Variável_1`, `CPF`)
- **chave:** Chave técnica (ex: `variavel_1`, `cpf`)
- **pagina:** Número da página (1-based)
- **xRel, yRel:** Coordenadas relativas (0–1) do canto superior esquerdo
- **wRel, hRel:** Largura e altura relativas (0–1)
- **modo:** `RegiaoFixa`, `TextoADireita` ou `TextoAbaixo`
- **tipo:** Tipo esperado (ex: `texto`, `cpf`, `moeda_brl`)
- **anchor_chaves:** Lista de chaves em `save_ok` (ex: `variavel_1,cpf,valor_liquido`)

## Exemplo de análise

```bash
# Ver últimas âncoras adicionadas/removidas
grep -E "ancorar_pdf_c2_anchor_add|ancorar_pdf_c2_anchor_remove|ancorar_pdf_c2_save_ok" ~/.local/share/protons-dev/Protons/log_ops.jsonl | tail -20

# Ver snapshot de âncoras no último save
grep "ancorar_pdf_c2_save_ok" ~/.local/share/protons-dev/Protons/log_ops.jsonl | tail -1
```

## Fluxo típico de teste

1. Usuário abre configuração de tarefa Ancorar PDF
2. Clica em "Selecionar" e desenha retângulos no PDF → `preview_render` com cada âncora
3. Ou usa Smart Click: clica no texto → `smart_click_confirmado`
4. Edita nome/chave na lista se necessário (não logado por campo)
5. Clica em Salvar → `save_ok` com `anchor_count` e `anchor_chaves`

## Sanitização

- Espaços em nomes/chaves são substituídos por `_` no log
- Strings longas são truncadas (sufixo `…`)
- O OpsLogger aplica sanitização de PII (CPF, CNPJ, e-mail) na mensagem final

## Validação automática

Os testes em `Protons.Infrastructure.Tests/Integration/AncorarPdfLogAncorasValidationTests.cs` validam:

- **LogAncoraHelper_FormatarContextoAncora** — formato inclui nome, chave, pagina, coordenadas (InvariantCulture)
- **LogAncoraHelper_Sanitizar** — truncamento e substituição de espaços
- **AdicionarAncora_DeveGravarEventoComNomeChaveEmLogOps** — fluxo completo: adicionar âncora → gravar em log_ops.jsonl → ler e verificar

```bash
dotnet test Login/testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj --filter "AncorarPdfLogAncorasValidationTests"
```

## Referência de código

- `AncorarPdfLogAncoraHelper.cs` — formatação de dados para log
- `AncorarPdfAncorasInteractionHandler.cs` — add, select, remove
- `AncorarPdfPreviewInteractionHandler.cs` — preview_render, replace
- `AncorarPdfSmartClickHandler.cs` — smart_click_confirmado
- `AncorarPdfSaveHandler.cs` — save_ok
