---
name: Fix painel direto launch
overview: "Corrigir o lançamento do painel direto para desenvolvimento: unificar em um único comando funcional, atualizar documentação e regras de IDE, e corrigir os 4 testes falhando."
todos:
  - id: chmod-script
    content: chmod +x no hot_reload_painel_direto.sh
    status: completed
  - id: fix-cursorrules
    content: Atualizar Login/.cursorrules para Avalonia/.NET 8.0 com comando único
    status: completed
  - id: create-cursor-rule
    content: Criar .cursor/rules/painel-direto.mdc no workspace
    status: completed
  - id: consolidate-docs
    content: Atualizar AVISO_PAINEL_DIRETO.md e GUIA_CONTINUIDADE_IDE.md
    status: completed
  - id: fix-tests
    content: Corrigir 4 testes falhando (schema version 10 -> 12 e migrações)
    status: completed
  - id: validate
    content: Rodar build + testes e confirmar 0 falhas
    status: completed
isProject: false
---

# Corrigir lançamento do painel direto e testes

## Diagnóstico

**Build:** PASS (0 erros, 0 warnings)
**Testes:** 4 falhas em 609 total (200+4 fail Infra, 405 pass Core)

### Problemas encontrados

1. **Script sem permissão de execução** -- `Login/scripts/hot_reload_painel_direto.sh` tem `-rw-rw----+` (sem bit `+x`), então `./scripts/...` falha com "Permission denied"
2. `**.cursorrules` desatualizado** -- [Login/.cursorrules](/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/.cursorrules) diz WPF/.NET 4.8, mas o projeto usa Avalonia/.NET 8.0. IAs leem isso e sugerem comandos errados
3. **Sem regra Cursor no workspace** -- A pasta `painel principal/` não tem `.cursor/rules/` dizendo o comando correto
4. **Testes falhando** -- Schema está na versão 12, mas o teste `C2_G3_SchemaV9` espera "10"; outros 3 testes de migração também falham por incompatibilidade de versão

### Arquivos-chave

- Script principal: [Login/scripts/hot_reload_painel_direto.sh](/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/scripts/hot_reload_painel_direto.sh)
- Regras Cursor (desatualizado): [Login/.cursorrules](/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/.cursorrules)
- Doc aviso: [AVISO_PAINEL_DIRETO.md](/srv/DocumentosCompartilhados/PROJETO PROTONS/AVISO_PAINEL_DIRETO.md)
- Guia IDE: [GUIA_CONTINUIDADE_IDE.md](/srv/DocumentosCompartilhados/PROJETO PROTONS/GUIA_CONTINUIDADE_IDE.md)
- Teste falhando: [AncorarPdfChecklist02ConfigPersistenceTests.cs](/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/testes/Protons.Infrastructure.Tests/Repositories/Tarefas/AncorarPdfChecklist02ConfigPersistenceTests.cs) (linha 119: espera "10", deveria ser "12")
- Testes migração: SqliteDbMigrationTests.cs (3 falhas por schema version)

---

## Plano de execução (6 passos)

### Passo 1 -- Dar permissão de execução ao script

- `chmod +x` em `Login/scripts/hot_reload_painel_direto.sh`
- Verificar que o script roda

### Passo 2 -- Atualizar `.cursorrules` (Login/)

- Corrigir de WPF/.NET 4.8 para Avalonia/.NET 8.0
- Adicionar seção "Como rodar o painel direto" com o comando único
- Manter regras de segurança, arquitetura e documentação que ainda se aplicam

### Passo 3 -- Criar regra Cursor no workspace (painel principal/)

- Criar `.cursor/rules/painel-direto.mdc` com o comando único e explicação para qualquer IA de IDE
- Comando canônico (a partir do workspace "painel principal"):

```bash
  cd "../Login" && bash scripts/hot_reload_painel_direto.sh
  

```

### Passo 4 -- Consolidar documentação

- Atualizar `AVISO_PAINEL_DIRETO.md` com caminho relativo ao workspace
- Atualizar `GUIA_CONTINUIDADE_IDE.md` para incluir caminho absoluto e relativo
- Garantir que todas as docs apontem para o mesmo comando

### Passo 5 -- Corrigir os 4 testes falhando

- `AncorarPdfChecklist02ConfigPersistenceTests.cs` linha 119: mudar `"10"` para `"12"`
- `SqliteDbMigrationTests.cs`: investigar e corrigir os 3 testes de migração que falham por versão de schema

### Passo 6 -- Validação final

- `dotnet build` (confirmar 0 erros)
- `dotnet test` (confirmar 0 falhas)
- Testar o script com `chmod +x` e rodar

---

## Comando final (após ajustes)

A partir do workspace "painel principal":

```bash
cd "../Login" && bash scripts/hot_reload_painel_direto.sh
```

Ou de qualquer lugar:

```bash
bash "/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/scripts/hot_reload_painel_direto.sh"
```

