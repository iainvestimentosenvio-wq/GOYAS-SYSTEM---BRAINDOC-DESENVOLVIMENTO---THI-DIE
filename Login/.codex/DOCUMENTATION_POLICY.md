# Política de Documentação (Codex)

## Objetivo
Garantir que a documentação **espelhe a arquitetura do código** e permaneça atualizada a cada alteração.

## Regra do espelho (obrigatória)
Para cada arquivo de código criado/alterado, **criar/atualizar** o arquivo `.md` correspondente:
- Código: `Protons.Core/Login/Services/AuthService.cs`
- Doc: `documentos/doc_login/Protons.Core/Login/Services/AuthService.md`

## Conteúdo mínimo por doc
Cada arquivo `.md` deve conter, no mínimo:
- **Objetivo**
- **Como funciona** (fluxo/resumo)
- **Entradas e saídas**
- **Dependências**
- **Decisões e porquê**
- **Como testar**
- **Logs/auditoria** (quando aplicável)

## Índices obrigatórios
Sempre atualizar:
- `documentos/DOCS_OVERVIEW.md`
- `documentos/doc_login/INDEX.md` (ou do módulo correspondente)

## Templates
- Use `documentos/TEMPLATES/TEMPLATE_DOC_ARQUIVO.md` para arquivos.
- Use `documentos/TEMPLATES/TEMPLATE_DOC_MODULO.md` para módulos.
- Use `documentos/TEMPLATES/TEMPLATE_CHECKLIST.md` para checklists.

## Checklist de atualização
- [ ] Doc espelhada criada/atualizada para cada arquivo de código
- [ ] Índices atualizados
- [ ] Links verificados
- [ ] Conteúdo mínimo preenchido (objetivo + funcionamento + testes)

## Como verificar
- [ ] Comparar árvore `Protons.UI/`, `Protons.Core/`, `Protons.Infrastructure/` vs `documentos/doc_login/`
- [ ] Revisar últimos arquivos modificados e seus `.md` correspondentes
