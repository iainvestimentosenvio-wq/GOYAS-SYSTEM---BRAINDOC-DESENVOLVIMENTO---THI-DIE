# Architecture Overview — Protons

## Design principles

Protons follows **Clean Architecture** with an **MVVM** presentation layer.

The central rule is: **UI never accesses the database directly.**
All data access goes through domain services in `Protons.Core`, which delegate persistence to `Protons.Infrastructure`.

This means:
- Business rules live in `Protons.Core` — independent of UI and database technology
- Swapping SQLite for PostgreSQL (or any other store) requires changes only in `Protons.Infrastructure`
- ViewModels in `Protons.UI` talk only to service interfaces, never to repositories directly

---

## Layer diagram

```
┌─────────────────────────────────────────────────────┐
│  Protons.UI  (Avalonia MVVM)                        │
│  Views · ViewModels · LocalSettings                 │
│  Depends on: Protons.Core interfaces only           │
└──────────────────────┬──────────────────────────────┘
                       │ calls
┌──────────────────────▼──────────────────────────────┐
│  Protons.Core  (Domain)                             │
│  AuthService · AuditService · AuditLogQueryService  │
│  PasswordHasher · Validators · Domain Models        │
│  Depends on: repository interfaces (no DB code)     │
└──────────────────────┬──────────────────────────────┘
                       │ implements interfaces
┌──────────────────────▼──────────────────────────────┐
│  Protons.Infrastructure  (Persistence)              │
│  UserRepository · AuditLogRepository               │
│  SqliteDb · PostgresDb                              │
│  PostgresUserRepository · PostgresAuditLogRepo      │
└──────────────────────┬──────────────────────────────┘
                       │
              ┌────────┴──────────┐
              │                   │
         ┌────▼────┐        ┌─────▼──────┐
         │  SQLite  │        │ PostgreSQL  │
         │ (local)  │        │ (server)   │
         └──────────┘        └────────────┘
```

---

## Component diagram (Login module)

```
Avalonia Views/ViewModels
    │
    ├──→ IAuthService (AuthService)
    │         ├──→ IUserRepository      ──→ SQLite / Postgres
    │         ├──→ IAuditService        ──→ IAuditLogRepository ──→ SQLite / Postgres
    │         └──→ IPasswordHasher
    │
    └──→ IAuditLogQueryService
              └──→ IAuditLogRepository  ──→ SQLite / Postgres
```

---

## Data flow: user login

```
1. LoginView (UI) → LoginViewModel.LoginCommand
2. LoginViewModel → IAuthService.Autenticar(email, senha)
3. AuthService:
   a. IUserRepository.FindByEmail(email)
   b. IPasswordHasher.Verify(senha, storedHash)
   c. Check lockout state
   d. IAuditService.Registrar(LOGIN_SUCESSO | LOGIN_FALHA)
4. AuthService → returns AuthResult
5. LoginViewModel updates navigation state
6. MainWindowViewModel navigates to HomeView or PainelView
```

---

## Database configuration

Protons supports two persistence modes selected at startup via `appsettings.json`:

| Mode | Storage | Use case |
|---|---|---|
| `Local` | SQLite at `%AppData%\Protons\protons.db` | Single-machine, fully offline |
| `Server` | PostgreSQL (configurable host/port/db) | Multi-PC shared environment |

Tables are created automatically on first run in both modes.

---

## Audit log design

Every security-relevant action produces an `AuditLogEntry` with:

| Field | Description |
|---|---|
| `timestamp` | UTC timestamp |
| `userId` / `email` | Actor identifier |
| `acao` | Event type (LOGIN_SUCESSO, LOCKOUT, etc.) |
| `resultado` | OK / ERRO |
| `detalhes` | Human-readable context (no sensitive data) |
| `maquina` | Hostname |
| `versaoApp` | Application version |

**Optional hash chain:** each entry stores `PrevHash` + `Hash` of its own content, forming a chain. Any modification breaks the chain and is detectable. This operates entirely offline with no external anchor.

Tracked events: `LOGIN_SUCESSO`, `LOGIN_FALHA`, `CRIAR_CONTA`, `APROVAR_USUARIO`, `REJEITAR_USUARIO`, `PROMOVER_ADMIN`, `LOCKOUT`, `LOGOUT`.

---

## Security model

| Concern | Implementation |
|---|---|
| Password storage | PBKDF2 + random salt, never plaintext |
| Failed attempt tracking | In-memory lockout, configurable threshold (default: 5 attempts, 30s) |
| Email enumeration | Generic error message regardless of email existence |
| CPF / sensitive data | Never written to logs or settings files |
| Connection strings | Not committed — managed via `appsettings.json` outside the repo |

---

## Cross-platform support

- UI: Avalonia UI renders natively on Windows and Linux (same codebase)
- Data paths:
  - Windows: `%AppData%\Protons\`
  - Linux: `$XDG_DATA_HOME/Protons` → fallback `~/.local/share/Protons`
- Build: `dotnet build` / `dotnet publish` on either platform
- Installer: separate pipelines for Windows (WiX/Inno) and Linux (AppImage/DEB)

---

## Module boundaries

| Module | Compiled by | Status |
|---|---|---|
| `Login/` | `Login/Protons.sln` | Stable |
| `painel principal/` | Included via `Protons.UI.csproj` `<Compile>` directives | In progress — will become its own project |
| `INSTALADOR/` | Platform-specific scripts | Active development |

*Note: `painel principal/telas/` and `painel principal/modelos_de_visao/` are currently compiled as part of `Protons.UI`. When the dashboard module is extracted, the `.csproj` references will be updated.*
