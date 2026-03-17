# PROJETO PROTONS — Módulo Login

Módulo de autenticação, auditoria e painel principal do sistema Protons.

## Stack

| Tecnologia | Versão | Uso |
|---|---|---|
| .NET | 8.0 | Runtime e SDK |
| Avalonia UI | 11.3.11 | Framework de UI (AXAML, MVVM) |
| CommunityToolkit.Mvvm | 8.2.1 | ViewModels, RelayCommand, ObservableObject |
| SQLite | — | Banco local (offline, dev e produção lite) |
| PostgreSQL | — | Banco de produção (modo enterprise) |

## Arquitetura

Três camadas com dependência unidirecional — a UI nunca acessa o banco diretamente:

```
UI  →  Core (contratos, serviços, validação)  →  Infrastructure (banco, repositórios)
```

## Estrutura de pastas

```
Login/
├── Protons.Core/          # Domínio: modelos, serviços, repositórios (interfaces), validação
├── Protons.Infrastructure/ # Implementações: SQLite, PostgreSQL, repositórios concretos
├── Protons.UI/            # Avalonia UI: ViewModels, Views, DI, configuração
├── testes/                # Testes automatizados (xUnit)
│   ├── Protons.Core.Tests/
│   ├── Protons.Infrastructure.Tests/
│   └── TestResults/       # Artefatos de execução (ignorados pelo git)
├── documentos/            # Documentação espelho 1:1
│   ├── DOCS_OVERVIEW.md
│   └── doc_login/
└── scripts/               # Scripts de desenvolvimento e CI
```

## Documentação

| Documento | Onde |
|---|---|
| Onboarding e comandos | `../GUIA_CONTINUIDADE_IDE.md` |
| Mapa da documentação | `../INDEX.md` |
| Padrão de docs | `documentos/DOCS_OVERVIEW.md` |

## Build

```bash
# A partir da pasta Login/
dotnet build Protons.sln -c Debug
```

## Testes

```bash
# Testes unitários (Core)
dotnet test testes/Protons.Core.Tests/Protons.Core.Tests.csproj -c Debug --no-build

# Testes de integração (Infrastructure)
dotnet test testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj -c Debug --no-build

# Suite completa
dotnet test Protons.sln -c Debug
```

## Subir o painel em desenvolvimento

```bash
# A partir do workspace "painel principal/" no Cursor
cd "../Login" && bash scripts/hot_reload_painel_direto.sh no-hot-reload
```

Consulte `../GUIA_CONTINUIDADE_IDE.md` para detalhes sobre bypass, BYOK e variáveis de ambiente.

## Regras críticas

- A UI não acessa o banco diretamente — todo acesso passa por `Core` e `Infrastructure`.
- Toda mudança em código exige atualização do doc espelho correspondente no mesmo commit.
- Não commitar artefatos de CI (`testes/TestResults/` está no `.gitignore`).
