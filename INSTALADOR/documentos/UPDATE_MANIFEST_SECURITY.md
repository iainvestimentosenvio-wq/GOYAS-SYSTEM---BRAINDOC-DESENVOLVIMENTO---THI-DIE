# UPDATE MANIFEST SECURITY

DataUTC: 2026-02-15T10:40:33Z
VersaoReferencia: 1.0.0

## Controles implementados

1. Validação de schema do manifesto.
2. Validação de hash SHA-256 por artefato.
3. Modo estrito para assinatura (`--strict-signature` / `--require-signature`).
4. Verificação criptográfica por chave pública quando fornecida.

## Casos críticos validados nesta rodada

- Chave pública divergente -> FAIL.
- Modo estrito sem chave pública -> FAIL.
- Assinatura inválida -> FAIL.
- Versão regressiva -> não atualiza.

Evidência:
- `saida/validacao-max-score-20260215T104033Z/24-test-update-manifest-pinning.log`

## Limitações atuais

- Sem certificado corporativo final, assinatura de código Windows permanece bloqueada.
- Ciclo HTTP real de update ainda pendente.

## Recomendação operacional

- Manter update em modo estrito para canais de produção.
- Tratar qualquer falha de assinatura como bloqueio de update.
- Registrar logs de auditoria para cada tentativa de atualização.
