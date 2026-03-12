# Decisao de Stack de Update e Distribuicao

DataUTC: 2026-02-09T23:10:00Z  
Escopo: registrar decisao tecnica para distribuicao e update sem mudar arquitetura central do app nesta rodada.

## Tabela comparativa

| Opcao | Cobertura | Mudanca no app | Risco | Decisao |
| --- | --- | --- | --- | --- |
| Stack atual (`update-manifest.json` + `check-update.sh`) | Windows + Linux no fluxo de manifest/hash/assinatura | baixa | baixo | **Manter como motor oficial agora (operacao local)** |
| GitHub Releases + Vercel (distribuicao externa) | Publicacao web + endpoint remoto de metadata | baixa a media | medio | **Planejado para fase futura pos-validacao Windows** |
| WinGet (canal adicional) | Windows (distribuicao e descoberta) | baixa a media | medio | **Planejado como canal complementar pos-validacao Windows** |
| Velopack | Auto-update com canais/delta | media a alta | medio | Manter como alternativa futura (nao adotar nesta rodada) |
| NetSparkle | Feed assinado de update para apps desktop | media a alta | medio | Manter como alternativa futura (nao adotar nesta rodada) |

## Veredito fechado

1. Motor de update permanece: `update-manifest` + `check-update.sh`.
2. Nesta rodada, operacao ativa continua local (`file://`) para manifesto e artefatos.
3. Integracao externa (GitHub Releases + Vercel) fica bloqueada ate fechar validacao Windows.
4. WinGet fica planejado como canal adicional para Windows, sem ativacao nesta rodada.
5. Velopack e NetSparkle ficam em backlog para revisao apos estabilizacao Windows.

## Justificativa objetiva

- Menor risco de regressao no cronograma atual.
- Reaproveita scripts e testes ja implementados e validados localmente.
- Evita dependencias externas antes do gate tecnico Windows.
- Evita migracao de stack durante fase de estabilizacao do instalador.

## Condicao para revisitar esta decisao

- Reabrir comparacao se houver:
  - fechamento do gate Windows (`GO_TECNICO_WINDOWS`) e inicio da etapa externa, ou
  - necessidade comprovada de delta update imediato, ou
  - custo operacional alto com o fluxo atual em producao.
