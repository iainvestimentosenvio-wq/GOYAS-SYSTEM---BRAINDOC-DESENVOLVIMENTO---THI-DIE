# Relatorio de Compatibilidade com Instalacoes Legadas

DataBaselineUTC: 2026-02-09T19:00:00Z
Escopo: avaliacao tecnica para instalacoes antigas antes de rollout amplo.

## Premissas
- Versao alvo atual: `1.0.0`
- Stack: `MSI primario + Inno compatibilidade`
- Regra de seguranca: sem validacao real em Windows, status permanece parcial.

## Matriz de compatibilidade (estado atual)
| Cenario legado | Estado | Evidencia | Observacao |
| --- | --- | --- | --- |
| Instalacao existente MSI 1.0.0 -> MSI 1.0.0 (reinstall) | Parcial | `saida/test-logs/e2e-results-20260207T174707.txt:43` | Preservacao de sentinela confirmada no recorte base. |
| Instalacao existente Inno 1.0.0 -> reinstall Inno 1.0.0 | Pendente | `testes/windows/test-inno.ps1` | Script criado; execucao em Windows ainda nao anexada nesta rodada. |
| Preservacao `%APPDATA%\Protons` em uninstall | Parcial | `documentos/MATRIZ_EVIDENCIAS_FINAL.md` | Preservacao existe, mas falhas criticas de cleanup ainda abertas. |
| Upgrade N->N+1 (Windows) | Pendente | `testes/windows/run-regressao.ps1` | Runner pronto, falta rodada em Windows 10/11 limpo. |
| Update por manifesto (MVP full package) | Parcial | `documentos/update.md` | Pipeline de manifesto pronto; falta prova de ciclo completo em Windows real. |

## Riscos de compatibilidade ainda abertos
1. Falhas criticas de uninstall (`MSI-UNINST-01`, `MSI-UNINST-03`, `INNO-UNINST-01`) podem impactar upgrades limpos.
2. Sem execucao em ambiente Windows limpo para todos os cenarios de legado.
3. Sem QA dedicado independente nesta rodada solo.

## Criterio para fechar compatibilidade legado
1. Executar `testes/windows/run-regressao.ps1` em Windows 10 e 11 limpos.
2. Obter `0 FAIL` em install/uninstall/upgrade/silent para MSI e Inno.
3. Publicar evidencias em `documentos/MATRIZ_EVIDENCIAS_FINAL.md` e atualizar riscos.
4. Atualizar este relatorio com estado `Aprovado` por cenario.

## Conclusao atual
- Status: `PENDENTE_DE_VALIDACAO_WINDOWS`
- Recomendacao: usar apenas rollout controlado ate fechamento da matriz de legado.
