# Validar Ancorar PDF — Análise e Plano de Correção

> Documento temporário para o agente consultar. Pode ser apagado após correção.

## Problema reportado (screenshot 2026-03-19)

**Cenário:** Usuário clicou em "INSCRIÇÃO ESTADUAL" (label do formulário) no Smart click.

**Esperado:** O sistema reconheceria que o **valor** da variável é o número **111054974** (abaixo do label).

**Obtido:** O sistema detectou "INSCRIÇÃO ESTADUAL" como o valor (tipo texto, 50% confiança).

---

## Causa raiz (análise do código)

### Fluxo atual do Smart click (`AncorarPdfSmartDetector.Detectar`)

1. **HitTest** — Encontra a palavra no ponto do clique.
2. **Agrupar** — Expande para palavras adjacentes na mesma linha.
3. **Classificar** — Aplica regex (CNPJ, CPF, e-mail, moeda, inteiro, etc.).
4. **InferirLabel** — Busca label **acima** ou **à esquerda** do grupo.
5. **Resultado** — `TextoBruto` = texto do grupo; `NomeSugerido` = label (se houver) ou tipo.

### O que acontece ao clicar no label

- Clique em "INSCRIÇÃO ESTADUAL" → grupo = ["INSCRIÇÃO", "ESTADUAL"].
- Classificação: não bate em nenhum regex → **Texto** (0.5).
- InferirLabel: procura label **acima** ou **à esquerda** — não há valor abaixo sendo considerado.
- Resultado: `TextoBruto = "INSCRIÇÃO ESTADUAL"`, `NomeSugerido = "Texto"` (ou label acima, se existir).

O detector **nunca** procura valor **abaixo** do ponto clicado. Ele assume que o clique é no valor e o label está acima/à esquerda.

### Casos de formulário típicos

| Layout | Clique no | Valor esperado | Label esperado |
|--------|-----------|----------------|----------------|
| Label acima, valor abaixo | Label ("INSCRIÇÃO ESTADUAL") | 111054974 (abaixo) | INSCRIÇÃO ESTADUAL |
| Label acima, valor abaixo | Valor (111054974) | 111054974 | INSCRIÇÃO ESTADUAL (acima) ✓ |
| Label à esquerda, valor à direita | Valor | valor | label ✓ |

O fluxo atual cobre apenas: **clique no valor**. Não cobre **clique no label** com valor abaixo.

---

## Solução proposta

### Nova etapa: "clique em label → buscar valor abaixo"

Quando o texto clicado for classificado como **Texto** e parecer um **label conhecido** (ex.: "INSCRIÇÃO ESTADUAL", "CNPJ/CPF", "NOME/RAZÃO SOCIAL", "ENDEREÇO", "MUNICÍPIO", "UF"):

1. Tratar o texto clicado como **label**.
2. Buscar o **valor** na linha **abaixo** (ou à direita, se fizer sentido).
3. Usar esse valor como `TextoBruto` e o texto clicado como `NomeSugerido`.

### Implementação sugerida

1. **Catálogo de labels comuns** — Lista de padrões (ex.: "INSCRIÇÃO ESTADUAL", "INSCRIÇÃO", "CNPJ/CPF", "NOME", "RAZÃO SOCIAL", "ENDEREÇO", "MUNICÍPIO", "UF").
2. **`EhLabelConhecido(string texto)`** — Verifica se o texto bate com algum padrão do catálogo.
3. **`InferirValorAbaixo(BboxRelativo grupoBbox, IReadOnlyList<PdfPalavra> todas)`** — Busca palavras na linha logo abaixo, dentro de `MaxGapLabelAcimaRel` (reutilizar constante).
4. **Fluxo alternativo em `Detectar`** — Se `classificacao.Tipo == Texto` e `EhLabelConhecido(textComEspaco)`:
   - Chamar `InferirValorAbaixo`.
   - Se encontrar valor abaixo: usar esse valor como `TextoBruto`, reclassificar, e `NomeSugerido` = texto clicado (label).
   - Se não encontrar: manter comportamento atual.

### Constantes a adicionar

```csharp
// Gap vertical máximo para buscar valor abaixo do label (simétrico ao MaxGapLabelAcimaRel).
private const double MaxGapValorAbaixoRel = 0.07;
```

### Logs para debug

- Registrar no `log_ops.jsonl` quando o fluxo "label → valor abaixo" for acionado.
- Ex.: `ancorar_pdf_c12_smart_click_label_abaixo label=INSCRIÇÃO_ESTADUAL valor=111054974`
- **Já existente:** `ancorar_pdf_c12_smart_click_confirmado` com nome, chave, tipo, pagina, xRel, yRel.
- **Sugestão:** Adicionar `ancorar_pdf_c12_smart_deteccao` ao detectar (antes de confirmar) com `textoBruto`, `tipo`, `confianca`, `labelInferido` — para analisar o que o detector retornou.

### Como analisar o log

```bash
# Ver últimas detecções smart click
grep "ancorar_pdf_c12" ~/.local/share/protons-dev/Protons/log_ops.jsonl | tail -10
```

---

## Checklist de validação

- [ ] Smart click em "INSCRIÇÃO ESTADUAL" → detecta valor 111054974, nome "INSCRIÇÃO ESTADUAL".
- [ ] Smart click em "CNPJ/CPF" (label) → detecta valor (ex.: 028.073.601-06).
- [ ] Smart click no valor (111054974) → comportamento atual preservado.
- [ ] Testes unitários para `EhLabelConhecido` e `InferirValorAbaixo`.

---

## Referências de código

- `Login/Protons.Infrastructure/Tarefas/servicos/AncorarPdfSmartDetector.cs` — lógica principal.
- `Login/Protons.Core/Tarefas/modelos/AncorarPdfSmartModels.cs` — tipos.
- `painel principal/.../AncorarPdfSmartClickHandler.cs` — UI e confirmação.
- `painel principal/.../documentacao/LOGS_ANCORAS_DEBUG.md` — formato dos logs.
