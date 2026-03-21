# Índice — Ferramenta Ancorar PDF

## Objetivo
Centralizar nesta pasta toda a documentação da ferramenta `ancorar_pdf`.

## Documentos ativos

| Arquivo | Descrição |
|---|---|
| `ADR-001_scheduler.md` | Decisão arquitetural de scheduler (Quartz vs Cronos). Status: **aguardando assinatura da equipe**. |
| `ADR-002_preview_renderer.md` | Decisão de preview: Docnet (PDFium) vs Ghostscript. |
| `LOGS_ANCORAS_DEBUG.md` | Formato dos logs de âncoras em log_ops.jsonl e como usar para análise/debug sem print. |

## Estado de implementação

A infraestrutura da ferramenta (`infraestrutura/`) ainda está vazia — nenhum scheduler foi implementado.
O próximo passo é assinar o ADR-001 e iniciar a implementação de `AncorarPdfSchedulerService.cs`.

## Regra de organização
- Todo novo documento da ferramenta deve ser criado nesta pasta.
- Cada documento novo deve ser referenciado neste índice.
- Não criar docs de checklist de execução; use issues/commits para rastrear progresso.
