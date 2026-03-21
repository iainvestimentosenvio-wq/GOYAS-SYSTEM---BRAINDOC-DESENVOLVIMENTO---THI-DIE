# Guia de Continuidade na IDE

Guia objetivo para retomar o projeto sem contexto anterior.

**Stack:** Avalonia UI / .NET 8.0 (AXAML), MVVM, SQLite (offline) / PostgreSQL (produção).

---

## 1. Onde trabalhar

| Módulo | Pasta |
|---|---|
| Backend, UI e testes | `Login/` |
| Painel principal (código + docs espelho) | `painel principal/` |
| Instaladores | `INSTALADOR/` |

O workspace Cursor ativo deve ser `painel principal/`. Todos os comandos abaixo partem desse contexto.

---

## 2. Ciclo inicial obrigatório

```bash
cd "../Login" && dotnet build Protons.sln -c Debug
cd "../Login" && dotnet test Protons.sln -c Debug
```

Se o build falhar antes de qualquer outra ação, corrija o build primeiro.

---

## 3. Subir o painel em desenvolvimento (comando único)

```bash
cd "../Login" && bash scripts/hot_reload_painel_direto.sh
```

Sem hot reload (mais estável para sessões longas):

```bash
cd "../Login" && bash scripts/hot_reload_painel_direto.sh no-hot-reload
```

> Use sempre `bash scripts/...` — nunca `./scripts/...` nem `dotnet run` direto.
> O script configura todas as variáveis de ambiente automaticamente.

**O que este modo faz:** pula a tela de login e abre o painel diretamente com um usuário dev,
acelerando o ciclo de desenvolvimento visual e funcional.

**Restrição de segurança:** o bypass só é ativado em `DEBUG` + ambiente `Development`.
Em `Release`, o login normal é obrigatório.

---

## 4. Persistência local no modo dev

- Banco de dados isolado do perfil de produção: `~/.local/share/protons-dev/Protons/protons.db`
- Não use `/tmp` para testes de persistência — os dados somem entre sessões.
- Para usar outro diretório: defina `PROTONS_XDG_DATA_HOME` antes de executar o script.

---

## 5. Chave de proteção de dados (BYOK)

Se o banco foi criado com uma chave diferente da atual, o sistema entra em **fail-closed** e bloqueia
a leitura de clientes para proteger os dados.

```bash
export PROTONS_DATA_KEY_BASE64="<chave-base64-da-instalação-original>"
cd "../Login" && bash scripts/hot_reload_painel_direto.sh
```

---

## 6. Antes de publicar release

1. Fazer build de release: `dotnet build Protons.sln -c Release`
2. Confirmar que `PROTONS_PAINEL_DIRETO_HABILITADO` **não está definido** no ambiente de release.
3. Validar o fluxo de login normal.

---

## 7. Regras de documentação

- Docs espelho apenas para módulos críticos (ver `Login/documentos/DOCS_OVERVIEW.md`).
- Não criar checklists de execução ou docs de pendência no branch ativo — use issues/commits.
- Um assunto = um documento. Sem duplicatas.

---

## 8. Estratégia de commit

1. Alteração de código.
2. Atualizar doc correspondente (se existir).
3. `dotnet build` + `dotnet test` com 0 falhas.
4. Commit único por tema funcional.

---

## 9. Diagnóstico rápido

| Sintoma | Ação |
|---|---|
| Build falhou | `dotnet build Protons.sln -c Debug` — ler a saída de erro completa |
| Painel não abre | Fazer build primeiro; depois rodar o script novamente |
| Doc inconsistente | Comparar doc com o código correspondente |
| Banco bloqueado | Verificar `PROTONS_DATA_KEY_BASE64` (seção 5) |
