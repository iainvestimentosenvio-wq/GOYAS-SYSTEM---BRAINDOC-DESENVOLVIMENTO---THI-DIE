# Guia de Operacao (Claude Code)

## Objetivo
Manter operacoes seguras no repositorio com codigo e documentacao espelhados.

## Regras de seguranca
- Nao executar comandos destrutivos sem necessidade explicita.
- Nao sobrescrever arquivo sem revisar contexto.
- Preferir operacoes idempotentes e rastreaveis.

## Regras de documentacao
- Para cada arquivo de codigo criado/alterado, atualizar o doc espelho correspondente.
- Seguir o padrao 1:1 definido em `documentos/DOCS_OVERVIEW.md`.
- Nao manter docs temporarias/checklists abertas no branch ativo.

## Referencias
- `../INDEX.md`
- `../GUIA_CONTINUIDADE_IDE.md`
- `documentos/doc_login/GUIA_DE_TRABALHO.md`

## Checklist antes de finalizar
- [ ] Arquivos modificados listados
- [ ] Docs espelho atualizadas
- [ ] Build/testes do escopo executados
