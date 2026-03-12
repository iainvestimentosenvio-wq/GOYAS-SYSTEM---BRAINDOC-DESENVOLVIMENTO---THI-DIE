# Documento de Testes - Login

## Objetivo
Descrever o escopo dos testes automatizados do modulo Login (Core e Infrastructure) e o que cada conjunto de testes valida.

## Escopo
Projetos de testes:
- `Login/testes/Protons.Core.Tests`
- `Login/testes/Protons.Infrastructure.Tests`

## Suites e finalidade

1) `Login/testes/Protons.Core.Tests/Validation/EmailValidatorTests.cs`
- Serve para validar o comportamento de `EmailValidator.EhValido`.
- Cobre emails validos, formatos invalidos, ausencia de @, separadores invalidos, espacos, maiusculas/minusculas, subdominios, TLDs variados e casos nulos/vazios.

2) `Login/testes/Protons.Core.Tests/Validation/CpfValidatorTests.cs`
- Serve para validar o comportamento de `CpfValidator.EhValido`.
- Cobre CPFs validos (com/sem formatacao), CPFs com digitos repetidos, digitos verificadores incorretos, tamanhos invalidos, formatos diversos e entradas nao numericas.

3) `Login/testes/Protons.Core.Tests/Services/PasswordHasherTests.cs`
- Serve para validar o hashing e a verificacao de senha.
- Cobre sucesso com senha correta, falha com senha incorreta, iteracoes incorretas, base64 invalido, valores nulos/vazios, variacoes pequenas de senha, consistencia do hash, resistencia basica a timing, e senhas com caracteres especiais.

4) `Login/testes/Protons.Core.Tests/Services/AuthServiceTests.cs`
- Serve para validar o fluxo principal de autenticacao e cadastro.
- Cobre login com sucesso e falha, lockout por tentativas, reset de falhas, permissao admin, usuario pendente/bloqueado, auditoria gerada, criar conta (email/CPF/senha/empresa/nome/cargo), aprovacao/rejeicao, listagem de pendentes e logout.

5) `Login/testes/Protons.Infrastructure.Tests/Repositories/SqliteUserRepositoryTests.cs`
- Serve para validar comportamento do repositório SQLite em casos defensivos.
- Cobre GetByEmail com valores nulos/vazios, garantindo retorno nulo sem erro.

6) `Login/testes/Protons.Infrastructure.Tests/Repositories/PostgresUserRepositoryTests.cs`
- Serve para validar comportamento do repositório Postgres em casos defensivos.
- Cobre fallback de enums inválidos e GetByEmail nulo/vazio.
- Requer variável de ambiente `PROTONS_POSTGRES_TEST_CONNECTION_STRING` apontando para um banco de teste.

7) `Login/testes/Protons.Infrastructure.Tests/Repositories/SqliteAuditLogRepositoryTests.cs`
- Serve para validar paginação de logs no SQLite.
- Cobre ordem decrescente por Id e comportamento de páginas.

8) `Login/testes/Protons.Infrastructure.Tests/Integration/AuthFlowSqliteTests.cs`
- Serve para validar o fluxo completo em SQLite usando serviços reais.
- Cobre criar conta → aprovar → login → logout e presença de logs de auditoria.

9) StartupProbe (modo de performance automatizado)
- Serve para medir o tempo de inicialização dos serviços e simular ambiente lento.
- Não abre UI; encerra o app após medir o tempo.
- Útil para detectar regressões de startup sem precisar de outra máquina.

## Como executar
- Comando: `dotnet test testes/Protons.Core.Tests/Protons.Core.Tests.csproj -c Release`
- Comando: `dotnet test testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj -c Release`
- Pasta de execucao: `Login/`
- StartupProbe (baseline): `PROTONS_STARTUP_PROBE=1 dotnet run --project Protons.UI/Protons.UI.csproj -c Release`
- StartupProbe (simulando HDD/CPU lenta): `PROTONS_STARTUP_PROBE=1 PROTONS_SIMULATE_SLOW_STARTUP_MS=200 dotnet run --project Protons.UI/Protons.UI.csproj -c Release`
- Opcional: `PROTONS_STARTUP_PROBE_OUT=<arquivo>` para salvar a saida em arquivo.
- Atalho (sem rebuild): `scripts/run_login_release.sh`
- Atalho (startup probe): `scripts/startup_probe.sh`
- Atalho (publish otimizado): `scripts/publish_login_release.sh`
- Atalho (menu/produção local): `scripts/launch_login.sh`

## Quando usar
- A cada mudanca em inicializacao, configuracoes, IO ou banco.
- Antes de releases ou quando houver relato de lentidao no startup.

## Observacoes
- Este documento descreve o proposito de cada suite. Para detalhes de casos individuais, consultar os arquivos de teste listados acima.
- Os resultados das execucoes ficam registrados em `Login/documentos/doc_login/RESULTADOS_EVOLUCAO_TESTES.md`.
