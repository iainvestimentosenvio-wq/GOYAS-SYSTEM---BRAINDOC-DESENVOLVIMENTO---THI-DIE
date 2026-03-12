# Doc de Testes - Login

## Onde estao os testes
- Testes do Core: `Login/testes/Protons.Core.Tests/`
- Testes da Infra: `Login/testes/Protons.Infrastructure.Tests/`

## O que cada suite cobre
- `Protons.Core.Tests`:
  - Validacoes (CPF, e-mail, limites de entrada)
  - Hashing de senha e verificacao
  - Fluxo de autenticacao (login, cadastro, lockout, permissoes)
- `Protons.Infrastructure.Tests`:
  - Repositorios SQLite/Postgres
  - Fluxos integrados com auditoria

## Como executar
```bash
# Na raiz de Login/
dotnet test testes/Protons.Core.Tests/Protons.Core.Tests.csproj -c Release
dotnet test testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj -c Release
```

## Registro de resultados
- Registrar cada execucao relevante em:
  `Login/documentos/doc_login/RESULTADOS_EVOLUCAO_TESTES.md`
