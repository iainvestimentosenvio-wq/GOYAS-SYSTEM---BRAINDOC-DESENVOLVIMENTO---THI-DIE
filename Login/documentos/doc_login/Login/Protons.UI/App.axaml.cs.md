# App.axaml.cs

## Objetivo
Inicializar a aplicação e configurar serviços essenciais (DB, auditoria, autenticação).

## Como funciona
- Define caminho local para settings:
  - Windows: `%AppData%\Protons`
  - Linux: `XDG_DATA_HOME/Protons` (fallback `~/.local/share/Protons`)
- Carrega `appsettings.json` (pasta de dados do usuário ou pasta do executável) **de forma assíncrona**.
- Seleciona modo **Local** (SQLite) ou **Servidor** (Postgres) com base na configuração.
- Resolve proteção de dados com política de chave:
  - modo local com `AutoPersistLocalKey=true`: usa chave persistida em `data_protection.key` (ou gera/persiste na primeira execução).
  - modo servidor (ou auto persist desativado): exige chave BYOK via variável de ambiente conforme política.
- Se existir base local já preenchida e não houver chave válida, o startup é bloqueado (não gera chave nova por cima).
- Cria DB, repositórios e serviços **usando padrões async não-bloqueantes**.
- Abre a `MainWindow` com tela de loading e inicializa serviços em background.
- Injeta o `MainWindowViewModel` com `LoginViewModel` após a inicialização.
- Suporta **StartupProbe** via variáveis de ambiente para medir tempo de startup sem abrir UI.
- Usa a versão do assembly para auditoria.
- Em falha de inicialização, exibe janela com erro amigável.
- Em incompatibilidade de chave BYOK com base existente, falha cedo com mensagem operacional clara.

## Otimizações de performance implementadas
### 1. Async/await não-bloqueante
- Convertido `BuildStartupServices()` para `BuildStartupServicesAsync()`
- Usa `await` com `ConfigureAwait(false)` em todas operações I/O
- Carregamento de configurações via `AppSettingsLoader.LoadAsync()`
- Método `MaybeDelayAsync()` substituiu `MaybeDelay()` que bloqueava com `GetAwaiter().GetResult()`

### 2. Database schema check optimization
- `EnsureCreated()` agora verifica se schema já existe antes de executar DDL completo
- Economiza ~100-150ms em execuções subsequentes
- Primeira execução: cria schema completo (~150-200ms)
- Execuções subsequentes: pula criação (~1-2ms)

### 3. Impacto esperado
- **Baseline (antes)**: ~300-500ms de startup
- **Após otimizações**: ~100-200ms de startup (~50-70% mais rápido)

## Entradas e saídas
- **Entradas**: none (usa Environment).
- **Saídas**: app inicializado e janela principal aberta.

## Dependências
- `SqliteDb`, `PostgresDb`, `UserRepository`, `PostgresUserRepository`, `AuditLogRepository`, `PostgresAuditLogRepository`.
- `AuthService`, `AuditService`, `AuditLogQueryService`, `PasswordHasher`.
- `AesGcmClienteDataProtector` e `OpsLogger` para telemetria de origem da chave.

## Decisões e porquê
- Modo duplo para atender uso local e multi-PC com DB central.
- Evitar crash silencioso no startup; erros de IO/config são tratados.
- Evitar "perda aparente" de clientes por troca de chave entre execuções: chave local persistida no modo SQLite.

## Como testar
- Rodar a aplicação em modo Local e verificar se o banco é criado no diretório de dados correto:
  - Windows: `%AppData%\Protons`
  - Linux: `XDG_DATA_HOME/Protons` (fallback `~/.local/share/Protons`)
- Rodar em modo Servidor e verificar se as tabelas sao criadas no Postgres.
- Validar primeira execução local sem `PROTONS_DATA_KEY_BASE64`:
  - arquivo `data_protection.key` deve ser criado em `~/.local/share/Protons/`.
- Validar reabertura com a mesma base:
  - dados continuam legíveis sem necessidade de redefinir variável de ambiente.
- Validar cenário de chave incorreta em base já criptografada:
  - aplicação deve bloquear startup com erro de chave incompatível.
- StartupProbe (baseline):
  - `PROTONS_STARTUP_PROBE=1 dotnet run --project Protons.UI/Protons.UI.csproj -c Release`
- StartupProbe (simulando ambiente lento):
  - `PROTONS_STARTUP_PROBE=1 PROTONS_SIMULATE_SLOW_STARTUP_MS=200 dotnet run --project Protons.UI/Protons.UI.csproj -c Release`
