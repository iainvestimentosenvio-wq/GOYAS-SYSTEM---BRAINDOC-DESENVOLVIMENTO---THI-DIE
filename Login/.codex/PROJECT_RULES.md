# Regras do Projeto (Codex)

## Objetivo
Garantir que **todo código criado ou alterado** tenha **documentação espelhada** e atualizada, mantendo arquitetura limpa, rastreabilidade e padrão corporativo.

## Escopo
Estas regras se aplicam a **toda** intervenção do Codex no repositório.

## Regras obrigatórias
- **Nenhuma alteração de código sem atualização de docs**.
- **Documentação espelha a árvore do código** (mesmo caminho e mesmo nome do arquivo, porém `.md`).
- **Atualizar índices**: `documentos/DOCS_OVERVIEW.md` e o `INDEX.md` do módulo afetado.
- **Não quebrar arquitetura**: respeitar camadas (UI/Core/Infraestrutura) e MVVM.
- **Não criar docs vazios**: cada doc deve ter objetivo + funcionamento + testes.

## Estrutura mínima esperada
- Código: `Protons.Core/` (ex.: `Protons.Core/Login/Services/AuthService.cs`)
- Doc espelhada: `documentos/doc_login/Protons.Core/Login/Services/AuthService.md`

## Checklist rápido antes de finalizar
- [ ] Código segue MVVM e separação de camadas
- [ ] Doc espelhada criada/atualizada para cada arquivo alterado
- [ ] Índices atualizados (`documentos/DOCS_OVERVIEW.md` e `doc_login/INDEX.md`)
- [ ] Regras de segurança/auditoria preservadas
- [ ] Sem documentação vazia

## Como aplicar na prática
- Use os templates em `documentos/TEMPLATES/`.
- Se criar um arquivo em `Protons.UI/`, `Protons.Core/` ou `Protons.Infrastructure/`, crie o `.md` espelhado em `documentos/doc_login/`.

## Como verificar
- [ ] Conferir correspondência 1:1 entre arquivos de código e docs
- [ ] Revisar índices e links
- [ ] Validar que os exemplos de fluxo continuam corretos
